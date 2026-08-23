using Apex.Ledger.Domain;
using Apex.Ledger.Seed;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>THE §194-I FAMILY LOCK — an ENFORCED invariant where there used to be only a comment.</b>
///
/// <para><b>What this exists to stop.</b> <see cref="NatureOfPayment.MonthlyThreshold"/> matches the
/// <b>whole normalised section code EXACTLY</b> — <c>194I</c>, <c>194I(A)</c>, <c>194I(B)</c> — and that
/// exactness is correct: a <c>StartsWith("194I")</c> would have handed <b>§194-IA</b> (purchase of immovable
/// property) a ₹50,000-a-month <i>rent</i> test it has no business with. But exactness cuts the other way too.
/// Every neighbouring section's code begins with the same four characters, and one of them — <b>§194-IB</b> —
/// carries a <b>per-month limb of its own</b>. A §194-IB row added to the seed today matches nothing in the
/// exact-match arm, so it lands on the <b>financial-year</b> window by default: precisely the under-deduction
/// §194-I was just dug out of, where one month's rent of ₹60,000 withheld ₹0.00 against a statutory
/// ₹6,000.00.</para>
///
/// <para><b>Why the existing lock does not catch it.</b>
/// <c>Tds194IMonthlyThresholdTests.Exactly_the_two_194I_rows_in_the_seed_have_a_per_month_window</c> pins the
/// set of per-month natures to exactly the two §194-I rows. A wrongly-seeded §194-IB row does <b>not</b> get a
/// per-month window — that is the whole defect — so it never enters that set, and the assertion passes just as
/// happily with the wrong row present. It locks what <i>has</i> the window; nothing locked what <b>ought</b>
/// to.</para>
///
/// <para>🔴 <b>THE FAMILY IS DERIVED FROM THE CODE, NEVER LISTED.</b> Membership is
/// <c>normalised.StartsWith("194I")</c> — so §194-ID, §194-IE or anything else the legislature bolts on next
/// is inside the lock the moment it is seeded, without anyone remembering to widen a list. A hard-coded roster
/// of members is exactly what fails to notice the next one. The narrow arm being guarded stays narrow; it is
/// the <b>guard</b> that is wide.</para>
///
/// <para><b>The escape hatch is an allow-list, and every entry states its statutory reason</b> — see
/// <see cref="FamilyMembersWithNoPerMonthLimb"/>. It is for family members with <b>no month in their test at
/// all</b>. It is <b>not</b> for §194-IB.</para>
///
/// <para>🔴 <b>AND THE LOCK PROVES ITSELF, IN BOTH DIRECTIONS, IN THIS FILE.</b> A guard that passes because
/// its predicate matched nothing is the defect it exists to catch, so the green direction asserts the derived
/// family is <b>non-empty</b> and that at least one member is genuinely guarded (i.e. the allow-list has not
/// swallowed the family); and the red direction is run for real against a synthetic §194-IB row in
/// <see cref="The_guard_goes_red_on_a_194IB_row_carrying_a_financial_year_threshold"/>.</para>
/// </summary>
public class Tds194IFamilyWindowGuardTests
{
    // =================================================================================================
    //  The allow-list — family members that legitimately have NO per-month window, each with its reason
    // =================================================================================================

    /// <summary>
    /// Family members that are <b>rightly</b> without a per-month window. Keyed on the <b>normalised whole
    /// code</b>, so a hand-authored "194-IA" and a seeded "194IA" are the same entry.
    ///
    /// <para>🔴 <b>ADDING A ROW HERE IS A STATUTORY CLAIM, NOT A WAY TO GET A BUILD GREEN.</b> The claim is
    /// that the section's threshold has <b>no month in it at all</b>. If the section does have a per-month
    /// limb, the fix is to extend the exact-match arm in <see cref="NatureOfPayment"/> so the row gets its
    /// window — not to silence the guard here.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> FamilyMembersWithNoPerMonthLimb =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["194IA"] =
                "§194-IA is NOT a rent section. It is a ONE-OFF deduction on the PURCHASE OF IMMOVABLE " +
                "PROPERTY, and its limb is a PER-TRANSACTION consideration test — no deduction where the " +
                "consideration for the transfer and the stamp-duty value of the property are both less than " +
                "fifty lakh rupees. There is no month anywhere in that test, and a rent window applied to it " +
                "would be nonsense: a single ₹60-lakh conveyance would be measured against ₹50,000 a month. " +
                "This is the section the exact-match arm in NatureOfPayment was made exact FOR.",
        };

    // =================================================================================================
    //  The derivation — family membership, and the offenders
    // =================================================================================================

    /// <summary>
    /// Mirrors the normalisation inside <see cref="NatureOfPayment"/> (hyphens and spaces removed,
    /// upper-cased). <c>Trim()</c> is a no-op on a constructed master — the constructor already trims — and is
    /// applied anyway so a raw string can be fed to the predicate directly.
    /// <see cref="The_derived_family_is_wider_than_the_exact_match_arm_it_guards"/> pins the mirror against the
    /// domain so the two cannot drift apart.
    /// </summary>
    private static string Normalise(string sectionCode) =>
        sectionCode.Trim().Replace("-", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();

    /// <summary>🔴 <b>DERIVED, never a list of members.</b> Everything whose whole normalised code opens with
    /// the four characters of §194-I: §194-I itself, its (a)/(b) arms, §194-IA, §194-IB, §194-IC, and whatever
    /// letter is added next.</summary>
    private static bool IsInThe194IFamily(string sectionCode) =>
        Normalise(sectionCode).StartsWith("194I", StringComparison.Ordinal);

    /// <summary>The section codes of family members that carry neither a per-month window nor an allow-list
    /// entry. Empty is the invariant.</summary>
    private static IReadOnlyList<string> UnguardedFamilyMembers(IEnumerable<NatureOfPayment> natures) =>
        natures.Where(n => IsInThe194IFamily(n.SectionCode))
               .Where(n => !n.ThresholdWindowIsPerMonth)
               .Where(n => !FamilyMembersWithNoPerMonthLimb.ContainsKey(Normalise(n.SectionCode)))
               .Select(n => n.SectionCode)
               .ToList();

    /// <summary>What the next author reads when the lock fires. It names the offending rows and it names the
    /// fix — and it names the fix that is <b>wrong</b>, because reaching for the allow-list is the natural
    /// move and it would ship the under-deduction rather than close it.</summary>
    private static string FailureMessage(IReadOnlyList<string> offenders) =>
        "🔴 A seeded Nature of Payment whose section code normalises into the §194-I family carries NEITHER a " +
        "MonthlyThreshold NOR an allow-list entry: " + string.Join(", ", offenders) + ".\n" +
        "\n" +
        "§194-I's threshold is a PER-MONTH limb — first proviso, 'for a month or part of a month ... does not " +
        "exceed fifty thousand rupees'. NatureOfPayment.MonthlyThreshold matches the WHOLE normalised code " +
        "EXACTLY (194I, 194I(A), 194I(B)) so that §194-IA cannot inherit a rent window. The consequence is " +
        "that a NEW family row matches nothing and silently takes the FINANCIAL-YEAR window instead. That is " +
        "the exact defect §194-I was dug out of: one month's rent of ₹60,000 withheld ₹0.00 where the Act " +
        "takes ₹6,000.00 — an UNDER-deduction the deductor answers for under §201, with interest under " +
        "§201(1A).\n" +
        "\n" +
        "🔴 IF THE ROW ABOVE IS §194-IB, IT HAS A PER-MONTH LIMB OF ITS OWN — rent paid by an individual or a " +
        "HUF not liable to tax audit, 'rent for a month or part of a month exceeds fifty thousand rupees', at " +
        "a different rate and under a different Form-26Q code. THE FIX IS TO EXTEND THE EXACT-MATCH ARM in " +
        "NatureOfPayment (the IsSection194I predicate behind MonthlyThreshold) so the row is given its monthly " +
        "window, and to add its own figures and tests. DO NOT add it to FamilyMembersWithNoPerMonthLimb: that " +
        "allow-list asserts the section has NO month in its test at all, which of §194-IB is false, and " +
        "silencing the guard would ship the under-deduction rather than close it.\n" +
        "\n" +
        "The allow-list is only for family members whose limb has no month in it — §194-IA's per-transaction " +
        "consideration test is the worked example, and it states its reason in full.";

    // =================================================================================================
    //  1. The lock itself — green on the tree as it stands, and NOT green because it matched nothing
    // =================================================================================================

    /// <summary>
    /// 🔴 <b>THE INVARIANT.</b> Every seeded nature in the §194-I family either carries a
    /// <see cref="NatureOfPayment.MonthlyThreshold"/> or is allow-listed with a stated statutory reason.
    ///
    /// <para><b>The two non-vacuity assertions come first, and they are not decoration.</b> The derived family
    /// must be non-empty — a lock that passes because its predicate matched nothing is the defect it exists to
    /// catch — and at least one member must actually carry the window, which is what stops the allow-list from
    /// being widened until it swallows the family and leaves a permanently, vacuously green test.</para>
    /// </summary>
    [Fact]
    public void Every_seeded_194I_family_nature_either_carries_the_per_month_window_or_is_allow_listed()
    {
        var seeded = SeedTdsTcsRates.BuildTdsDefaults();
        var family = seeded.Where(n => IsInThe194IFamily(n.SectionCode)).ToList();

        Assert.True(family.Count > 0,
            "NON-VACUITY: the derived §194-I family predicate matched NOTHING in the seeded set, so this lock " +
            "is asserting over an empty collection and would pass whatever the seed said. Either the §194-I " +
            "rows have been renamed out of the family (in which case fix the predicate) or the seed has lost " +
            "them (in which case fix the seed). A lock that passes because it found nothing is the defect it " +
            "exists to catch.");

        Assert.True(family.Any(n => n.ThresholdWindowIsPerMonth),
            "NON-VACUITY: every member of the §194-I family is now allow-listed, so this lock can no longer " +
            "fail. The allow-list is a narrow escape hatch for sections with no month in their test, not a " +
            "way to retire the invariant.");

        var offenders = UnguardedFamilyMembers(seeded);
        Assert.True(offenders.Count == 0, FailureMessage(offenders));
    }

    // =================================================================================================
    //  2. The RED direction, run for real and kept in the repo
    // =================================================================================================

    /// <summary>
    /// 🔴 <b>THE PROOF THAT THE LOCK BITES.</b> A §194-IB row — the real shape of the mistake: a plausible
    /// rate, its own Form-26Q code, and a <b>financial-year</b> threshold where the statute has a per-month
    /// one — is added to the seeded set, and the guard must name it.
    ///
    /// <para>This is the same experiment as temporarily editing the seed, kept permanently in the repo so the
    /// lock's discriminating power is re-proved on every run rather than resting on one session's word. It
    /// also pins the <b>message</b>, because a guard that fires with unhelpful text sends the next author to
    /// the allow-list.</para>
    /// </summary>
    [Fact]
    public void The_guard_goes_red_on_a_194IB_row_carrying_a_financial_year_threshold()
    {
        var wronglySeeded = new NatureOfPayment(
            Guid.NewGuid(), "194IB", "Rent — individual/HUF not liable to tax audit", 500, 2000, "4IB",
            cumulativeThreshold: Money.FromRupees(6_00_000m), isPredefined: true);

        var offenders = UnguardedFamilyMembers(SeedTdsTcsRates.BuildTdsDefaults().Append(wronglySeeded));

        Assert.Equal(new[] { "194IB" }, offenders);

        var message = FailureMessage(offenders);
        Assert.Contains("194IB", message);
        Assert.Contains("§194-IB", message);
        Assert.Contains("EXTEND THE EXACT-MATCH ARM", message);
        Assert.Contains("DO NOT add it to FamilyMembersWithNoPerMonthLimb", message);
    }

    /// <summary>The hyphenated spelling is the same row and is caught identically — the guard normalises before
    /// it decides, exactly as the domain does.</summary>
    [Fact]
    public void The_guard_goes_red_on_the_hyphenated_spelling_too()
    {
        var wronglySeeded = new NatureOfPayment(
            Guid.NewGuid(), "194-IB", "Rent — individual/HUF not liable to tax audit", 500, 2000, "4IB",
            cumulativeThreshold: Money.FromRupees(6_00_000m), isPredefined: true);

        Assert.Equal(new[] { "194-IB" }, UnguardedFamilyMembers(new[] { wronglySeeded }));
    }

    /// <summary>An allow-listed family member is <b>not</b> flagged, however it is spelled — which is what
    /// keeps §194-IA seedable without loosening the guard for anything else.</summary>
    [Theory]
    [InlineData("194IA")]
    [InlineData("194-IA")]
    [InlineData("194 I A")]
    public void An_allow_listed_family_member_is_not_flagged(string code)
    {
        var propertyPurchase = new NatureOfPayment(
            Guid.NewGuid(), code, "Purchase of immovable property", 100, 2000, "4IA",
            singleTransactionThreshold: Money.FromRupees(50_00_000m), isPredefined: true);

        Assert.True(IsInThe194IFamily(code));                       // it IS in the family …
        Assert.False(propertyPurchase.ThresholdWindowIsPerMonth);   // … and rightly has no rent window …
        Assert.Empty(UnguardedFamilyMembers(new[] { propertyPurchase }));  // … so the reason carries it.
    }

    // =================================================================================================
    //  3. The allow-list has to stay honest
    // =================================================================================================

    /// <summary>
    /// Every allow-list entry must itself be in the family (a stray key would sit there forever, exempting
    /// nothing and misleading whoever reads it), must be stored under its own normalised form, and must carry
    /// a reason substantial enough to be an actual statutory justification rather than a shrug.
    /// </summary>
    [Fact]
    public void Every_allow_list_entry_is_in_the_family_normalised_and_states_a_reason()
    {
        Assert.NotEmpty(FamilyMembersWithNoPerMonthLimb);

        Assert.All(FamilyMembersWithNoPerMonthLimb, entry =>
        {
            Assert.True(IsInThe194IFamily(entry.Key),
                "Allow-list key '" + entry.Key + "' is not in the §194-I family, so it exempts nothing. " +
                "Remove it, or fix the code it was meant to name.");
            Assert.True(Normalise(entry.Key) == entry.Key,
                "Allow-list key '" + entry.Key + "' is not stored in normalised form, so it will never match " +
                "a seeded code. Store it as " + Normalise(entry.Key) + ".");
            Assert.False(string.IsNullOrWhiteSpace(entry.Value));
            Assert.True(entry.Value.Length >= 80,
                "Allow-list entry '" + entry.Key + "' must state WHY the section has no per-month limb, in " +
                "enough words to be checkable against the statute.");
        });
    }

    // =================================================================================================
    //  4. The mirror must not drift from the domain
    // =================================================================================================

    /// <summary>
    /// 🔴 <b>THE GUARD IS DELIBERATELY WIDER THAN THE ARM IT GUARDS, and this pins both halves against the
    /// domain.</b> Every spelling the exact-match arm accepts is inside the derived family (otherwise the lock
    /// would not cover the very section it protects), and every neighbour the arm rejects is <b>also</b>
    /// inside the family (otherwise the lock would not notice the next one being seeded). If the domain's
    /// normalisation is ever changed, this is what fails.
    /// </summary>
    [Theory]
    // Spellings the exact-match arm ACCEPTS — in the family, and per-month.
    [InlineData("194I", true)]
    [InlineData("194-I", true)]
    [InlineData("194I(a)", true)]
    [InlineData("194i(b)", true)]
    [InlineData("194-I(B)", true)]
    // Neighbours the exact-match arm REJECTS — in the family all the same, which is the point.
    [InlineData("194IA", false)]
    [InlineData("194-IA", false)]
    [InlineData("194IB", false)]
    [InlineData("194-IB", false)]
    [InlineData("194IC", false)]
    [InlineData("194ID", false)]
    public void The_derived_family_is_wider_than_the_exact_match_arm_it_guards(string code, bool expectPerMonth)
    {
        var nature = new NatureOfPayment(Guid.NewGuid(), code, "Anything", 1000, 2000, "XX");

        Assert.True(IsInThe194IFamily(code), "'" + code + "' must be inside the derived §194-I family.");
        Assert.Equal(expectPerMonth, nature.ThresholdWindowIsPerMonth);
    }

    /// <summary>Sections outside the family are outside the lock — it must not start policing §194-J or §194-Q,
    /// whose financial-year windows are correct.</summary>
    [Theory]
    [InlineData("194A")]
    [InlineData("194C")]
    [InlineData("194H")]
    [InlineData("194J(a)")]
    [InlineData("194J(b)")]
    [InlineData("194M")]
    [InlineData("194N")]
    [InlineData("194Q")]
    public void A_section_outside_the_family_is_outside_the_lock(string code)
    {
        Assert.False(IsInThe194IFamily(code));

        var nature = new NatureOfPayment(Guid.NewGuid(), code, "Anything", 1000, 2000, "XX",
            cumulativeThreshold: Money.FromRupees(50_000m));

        Assert.Empty(UnguardedFamilyMembers(new[] { nature }));
    }
}
