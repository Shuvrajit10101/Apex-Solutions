using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>§194C's with-PAN rate turns on the DEDUCTEE'S LEGAL STATUS, and until this file it did not.</b>
/// <para>
/// Statute — Income-tax Act 1961 <b>§194C(1)</b>, bare Act text as published by the Income-tax Department
/// (<c>https://www.incometaxindia.gov.in/w/section-194c</c>): the deductor shall "deduct an amount equal to —
/// (i) <b>one per cent</b> where the payment is being made or credit is being given to an <b>individual or a Hindu
/// undivided family</b>; (ii) <b>two per cent</b> where the payment is being made or credit is being given to a
/// <b>person other than an individual or a Hindu undivided family</b>". The same split is stated on the
/// Department's own rate chart for <b>Assessment Year 2026-27</b> (= FY 2025-26, the year this seed encodes) —
/// "Section 194C: Payment to contractor/sub-contractor — a) HUF/Individuals 1 — b) Others 2"
/// (<c>https://www.incometaxindia.gov.in/w/tds-rates-1</c>).
/// </para>
/// <para>
/// 🔴 <b>THE CONSTRUCTED FAILURE.</b> <see cref="TdsService.ComputeWithholding"/> resolved the rate as
/// <c>panApplied ? RateWithPanBp : RateWithoutPanBp</c> and read <see cref="Domain.Ledger.DeducteeType"/> NOWHERE,
/// so Individual, HUF, Firm and Company all resolved the seeded 100 bp. Measured on a PAN-holding <b>Company</b>
/// contractor and a ₹50,000 bill (liable through §194C's ₹30,000 single-transaction limb), the engine withheld
/// <b>₹500.00 at 100 bp</b> where §194C(1)(ii) requires <b>₹1,000.00 at 200 bp</b> — a ₹500.00 under-deduction on
/// one ordinary contractor bill, and the deductor's liability under §201.
/// </para>
/// <para>
/// 🔴 <b>GRANDFATHERING — the user's ruling, and it is a fact about the VOUCHER, not about the clock.</b> Before
/// the bifurcation existed EVERY §194C voucher resolved <see cref="NatureOfPayment.RateWithPanBp"/> (the Ind/HUF
/// arm), including those whose deductee was a company or a firm. The alteration path pins
/// <c>RateBasisPoints</c> off the POSTED voucher and refuses a disagreement, so turning the branch on would make
/// every one of those vouchers unalterable. The grandfathering is therefore carried by an <b>explicit argument</b>
/// — <c>postedRateBasisPoints</c>, read off the voucher's own stamped <see cref="TdsLineTax.RateBasisPoints"/> —
/// and never by a date comparison. See <see cref="TdsService.GrandfatheredRate"/> for exactly what it will and
/// will not absorb.
/// </para>
/// </summary>
public class Tds194CDeducteeTypeTests
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";

    private static readonly DateOnly Fy = new(2025, 4, 1);
    private static readonly DateOnly D1 = new(2025, 5, 10);

    private static Company NewTdsCompany()
    {
        var c = CompanyFactory.CreateSeeded("Contractor Co", Fy);
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan });
        return c;
    }

    private static Domain.Ledger Contractor(Company c, DeducteeType? type, string? pan = DeducteePan)
    {
        var nop = c.FindNatureOfPaymentByCode("194C")!;
        var l = new Domain.Ledger(
            Guid.NewGuid(), $"Contractor-{Guid.NewGuid():N}", c.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, false);
        c.AddLedger(l);
        l.TdsApplicable = true; l.TdsNatureOfPaymentId = nop.Id; l.DeducteeType = type; l.PartyPan = pan;
        return l;
    }

    private static NatureOfPayment Nature(Company c, string code) => c.FindNatureOfPaymentByCode(code)!;

    // =================================================================================================
    //  1. The bifurcation itself — §194C(1)(i) vs §194C(1)(ii)
    // =================================================================================================

    /// <summary>
    /// 🔴 THE CONSTRUCTED FAILURE, WITH THE LITERAL WRONG FIGURE. A ₹50,000 §194C contract bill to a PAN-holding
    /// <b>Company</b>. §194C(1)(ii) charges <b>2%</b> = <b>₹1,000.00</b>. Before the branch the engine resolved
    /// <b>100 bp</b> and withheld <b>₹500.00</b>.
    /// </summary>
    [Fact]
    public void A_company_contractor_is_deducted_at_two_percent_not_one()
    {
        var c = NewTdsCompany();
        var w = new TdsService(c).ComputeWithholding(
            Money.FromRupees(50_000m), Nature(c, "194C"), Contractor(c, DeducteeType.Company), D1);

        Assert.True(w.Applies);
        Assert.Equal(200, w.RateBasisPoints);
        Assert.Equal(Money.FromRupees(1_000m), w.TdsAmount);
        Assert.NotEqual(Money.FromRupees(500m), w.TdsAmount); // the literal pre-fix figure
    }

    /// <summary>§194C(1)(i) — an <b>individual</b> contractor stays at 1% / ₹500.00 on the same bill.</summary>
    [Fact]
    public void An_individual_contractor_stays_at_one_percent()
    {
        var c = NewTdsCompany();
        var w = new TdsService(c).ComputeWithholding(
            Money.FromRupees(50_000m), Nature(c, "194C"), Contractor(c, DeducteeType.Individual), D1);

        Assert.Equal(100, w.RateBasisPoints);
        Assert.Equal(Money.FromRupees(500m), w.TdsAmount);
    }

    /// <summary>
    /// Every arm of <see cref="DeducteeType"/>, against the statutory words. Only <c>Individual</c> and
    /// <c>HinduUndividedFamily</c> are the §194C(1)(i) 1% arm; every other legal status — company, firm, AOP, BOI,
    /// local authority, government, artificial juridical person — is the §194C(1)(ii) 2% arm.
    /// </summary>
    [Theory]
    [InlineData(DeducteeType.Individual, 100, 500)]
    [InlineData(DeducteeType.HinduUndividedFamily, 100, 500)]
    [InlineData(DeducteeType.Company, 200, 1_000)]
    [InlineData(DeducteeType.Firm, 200, 1_000)]
    [InlineData(DeducteeType.AssociationOfPersons, 200, 1_000)]
    [InlineData(DeducteeType.BodyOfIndividuals, 200, 1_000)]
    [InlineData(DeducteeType.LocalAuthority, 200, 1_000)]
    [InlineData(DeducteeType.Government, 200, 1_000)]
    [InlineData(DeducteeType.ArtificialJuridicalPerson, 200, 1_000)]
    public void Every_deductee_type_resolves_its_statutory_arm(DeducteeType type, int expectedBp, decimal expectedTds)
    {
        var c = NewTdsCompany();
        var w = new TdsService(c).ComputeWithholding(
            Money.FromRupees(50_000m), Nature(c, "194C"), Contractor(c, type), D1);

        Assert.Equal(expectedBp, w.RateBasisPoints);
        Assert.Equal(Money.FromRupees(expectedTds), w.TdsAmount);
    }

    /// <summary>
    /// <b>No PAN ⇒ §206AA, and the deductee type does not enter.</b> §206AA(1) charges the higher of the section
    /// rate, the rates in force, or 20% — a single figure with no individual/HUF concession, so a company and an
    /// individual both land on 2000 bp.
    /// </summary>
    [Theory]
    [InlineData(DeducteeType.Individual)]
    [InlineData(DeducteeType.Company)]
    public void Without_a_pan_both_arms_collapse_onto_the_206AA_rate(DeducteeType type)
    {
        var c = NewTdsCompany();
        var w = new TdsService(c).ComputeWithholding(
            Money.FromRupees(50_000m), Nature(c, "194C"), Contractor(c, type, pan: null), D1);

        Assert.False(w.PanApplied);
        Assert.Equal(2000, w.RateBasisPoints);
        Assert.Equal(Money.FromRupees(10_000m), w.TdsAmount);
    }

    /// <summary>
    /// A §194C deductee with <b>no legal status recorded</b> is refused by name rather than silently given the
    /// 1% concession §194C(1)(i) grants only to an individual or a HUF. The entry screen cannot produce this shape
    /// (a party is only recognised as a deductee when it carries a <c>DeducteeType</c>), so the refusal guards the
    /// engine API and the import path, not the operator.
    /// </summary>
    [Fact]
    public void A_deductee_with_no_recorded_legal_status_is_refused_by_name()
    {
        var c = NewTdsCompany();
        var party = Contractor(c, type: null);

        var ex = Assert.Throws<InvalidOperationException>(() => new TdsService(c).ComputeWithholding(
            Money.FromRupees(50_000m), Nature(c, "194C"), party, D1));

        Assert.Contains(party.Name, ex.Message);
        Assert.Contains("194C", ex.Message);
        Assert.Contains("individual or a Hindu undivided family", ex.Message);
    }

    /// <summary>
    /// The branch is <b>section-gated</b>: §194J(b) has one with-PAN rate whatever the deductee is, so a company
    /// and an individual both resolve 1000 bp. A leak here would have doubled every professional-fee deduction.
    /// </summary>
    [Theory]
    [InlineData(DeducteeType.Individual)]
    [InlineData(DeducteeType.Company)]
    public void A_section_without_a_deductee_type_branch_is_untouched(DeducteeType type)
    {
        var c = NewTdsCompany();
        var party = Contractor(c, type);
        party.TdsNatureOfPaymentId = Nature(c, "194J(b)").Id;

        var w = new TdsService(c).ComputeWithholding(
            Money.FromRupees(1_00_000m), Nature(c, "194J(b)"), party, D1);

        Assert.Equal(1000, w.RateBasisPoints);
        Assert.Equal(Money.FromRupees(10_000m), w.TdsAmount);
    }

    /// <summary>
    /// <b>§194C is the only seeded nature whose rate turns on the deductee.</b> The same shape as
    /// <c>Exactly_one_seeded_nature_of_payment_charges_on_the_excess_and_it_is_194Q</c>: a whole-set assertion, so
    /// a future seed row cannot quietly acquire the branch.
    /// </summary>
    [Fact]
    public void Exactly_one_seeded_nature_of_payment_branches_on_deductee_type_and_it_is_194C()
    {
        var branching = Seed.SeedTdsTcsRates.BuildTdsDefaults()
            .Where(n => n.RateTurnsOnDeducteeType).Select(n => n.SectionCode).ToList();

        Assert.Equal(new[] { "194C" }, branching);
        Assert.Equal(200, Seed.SeedTdsTcsRates.BuildTdsDefaults()
            .Single(n => n.SectionCode == "194C").RateWithPanOtherThanIndividualBp);
    }

    /// <summary>
    /// The derived property round-trips off the persisted <c>SectionCode</c> alone — the reason it is derived
    /// rather than stored is that a stored second rate needs a <c>nature_of_payment</c> column and therefore a
    /// schema migration. A hand-built §194C nature carrying a different base rate still exposes the 2% arm.
    /// </summary>
    [Fact]
    public void The_other_than_individual_arm_is_derived_from_the_section_code_not_stored()
    {
        var mine = new NatureOfPayment(
            Guid.NewGuid(), "194C", "My contractors", 100, 2000, "94C",
            Money.FromRupees(30_000m), Money.FromRupees(1_00_000m));
        var notMine = new NatureOfPayment(Guid.NewGuid(), "194J(b)", "Fees", 1000, 2000, "94J-B");

        Assert.True(mine.RateTurnsOnDeducteeType);
        Assert.Equal(200, mine.RateWithPanOtherThanIndividualBp);
        Assert.False(notMine.RateTurnsOnDeducteeType);
        Assert.Null(notMine.RateWithPanOtherThanIndividualBp);
    }

    // =================================================================================================
    //  2. Grandfathering — explicit and pinned, never a date check
    // =================================================================================================

    /// <summary>
    /// 🔴 <b>THE RULING.</b> A §194C voucher posted BEFORE the bifurcation carries 100 bp although its deductee is
    /// a company. Handed that posted rate explicitly, the engine re-resolves it to <b>100 bp</b>, not 200 — so the
    /// alteration path's rate pin agrees and the voucher stays alterable. Nothing here reads a clock.
    /// </summary>
    [Fact]
    public void A_voucher_posted_before_the_bifurcation_re_resolves_at_the_rate_it_was_posted_with()
    {
        var c = NewTdsCompany();
        var party = Contractor(c, DeducteeType.Company);
        var svc = new TdsService(c);

        var fresh = svc.ComputeWithholding(Money.FromRupees(50_000m), Nature(c, "194C"), party, D1);
        Assert.Equal(200, fresh.RateBasisPoints);

        var grandfathered = svc.ComputeWithholding(
            Money.FromRupees(50_000m), Nature(c, "194C"), party, D1, postedRateBasisPoints: 100);

        Assert.Equal(100, grandfathered.RateBasisPoints);
        Assert.Equal(Money.FromRupees(500m), grandfathered.TdsAmount);
    }

    /// <summary>
    /// <b>Grandfathering is one-directional.</b> It absorbs only "posted on the seeded Ind/HUF arm, now resolves
    /// the other-than-individual arm" — the one shape a pre-bifurcation voucher can have. A voucher posted at 200 bp
    /// whose party was RE-TYPED to an individual afterwards still resolves 100 bp, so the alteration path's pin
    /// sees the disagreement and refuses.
    /// </summary>
    [Fact]
    public void A_party_re_typed_down_to_an_individual_after_posting_is_not_grandfathered()
    {
        var c = NewTdsCompany();
        var party = Contractor(c, DeducteeType.Individual);

        var w = new TdsService(c).ComputeWithholding(
            Money.FromRupees(50_000m), Nature(c, "194C"), party, D1, postedRateBasisPoints: 200);

        Assert.Equal(100, w.RateBasisPoints);
    }

    /// <summary>
    /// <b>Grandfathering never reaches the §206AA arm.</b> A deductee whose PAN was removed after posting resolves
    /// 2000 bp and stays there even though a posted rate is supplied — that drift is the alteration path's to
    /// refuse, and the existing PAN-drift refusal must keep firing.
    /// </summary>
    [Fact]
    public void A_pan_removed_after_posting_is_never_grandfathered_back_onto_a_with_pan_rate()
    {
        var c = NewTdsCompany();
        var party = Contractor(c, DeducteeType.Company, pan: null);

        var w = new TdsService(c).ComputeWithholding(
            Money.FromRupees(50_000m), Nature(c, "194C"), party, D1, postedRateBasisPoints: 100);

        Assert.False(w.PanApplied);
        Assert.Equal(2000, w.RateBasisPoints);
    }

    /// <summary>
    /// <b>Grandfathering never reaches a section without the branch.</b> A §194J(b) voucher whose posted rate
    /// disagrees with today's resolution is left disagreeing, so a moved rate master is still refused.
    /// </summary>
    [Fact]
    public void A_section_without_the_branch_is_never_grandfathered()
    {
        var c = NewTdsCompany();
        var party = Contractor(c, DeducteeType.Company);
        party.TdsNatureOfPaymentId = Nature(c, "194J(b)").Id;

        var w = new TdsService(c).ComputeWithholding(
            Money.FromRupees(1_00_000m), Nature(c, "194J(b)"), party, D1, postedRateBasisPoints: 750);

        Assert.Equal(1000, w.RateBasisPoints);
    }

    /// <summary>
    /// <b>Grandfathering absorbs only the section's own two arms.</b> A posted rate that is neither 100 bp nor
    /// 200 bp on §194C — a hand-edited or imported figure — is not honoured, so an arbitrary stamped rate cannot
    /// be resurrected by supplying it.
    /// </summary>
    [Theory]
    [InlineData(50)]
    [InlineData(150)]
    [InlineData(2000)]
    public void A_posted_rate_outside_the_sections_own_two_arms_is_not_grandfathered(int postedBp)
    {
        var c = NewTdsCompany();
        var w = new TdsService(c).ComputeWithholding(
            Money.FromRupees(50_000m), Nature(c, "194C"), Contractor(c, DeducteeType.Company), D1,
            postedRateBasisPoints: postedBp);

        Assert.Equal(200, w.RateBasisPoints);
    }

    /// <summary>
    /// 🔴 <b>THE No-PAN GUARD IS NOT DEAD, AND THIS IS THE CASE THAT PROVES IT.</b> Deleting
    /// <c>if (!panApplied) return resolvedBp;</c> from <see cref="TdsService.GrandfatheredRate"/> survived the
    /// whole suite once, because on the SEEDED §194C the no-PAN rate is 2000 bp and the directional test
    /// (<c>resolved == the other-than-individual arm</c>) fails on its own. It stops failing the moment a
    /// hand-authored §194C nature carries a no-PAN rate that COLLIDES with the 2% arm: 200 bp resolved for want of
    /// a PAN then looks exactly like 200 bp resolved for a company, and a §206AA deduction would be grandfathered
    /// back down to 1%. §206AA(1) is a floor that the deductee's legal status never modifies, so the guard fires
    /// first and this voucher stays at its §206AA rate.
    /// </summary>
    [Fact]
    public void A_hand_authored_194C_whose_no_pan_rate_collides_with_the_two_percent_arm_is_still_not_grandfathered()
    {
        var c = NewTdsCompany();
        var colliding = new NatureOfPayment(
            Guid.NewGuid(), "194C", "Contractors, odd no-PAN rate", 100, 200, "94C",
            Money.FromRupees(30_000m), Money.FromRupees(1_00_000m));

        var w = new TdsService(c).ComputeWithholding(
            Money.FromRupees(50_000m), colliding, Contractor(c, DeducteeType.Company, pan: null), D1,
            postedRateBasisPoints: 100);

        Assert.False(w.PanApplied);
        Assert.Equal(200, w.RateBasisPoints);                    // the §206AA arm, not the grandfathered 100
        Assert.Equal(Money.FromRupees(1_000m), w.TdsAmount);
    }

    /// <summary>
    /// The carve-out honours the grandfathered rate end-to-end: the party is credited the NET derived from the
    /// grandfathered ₹500.00, and <c>net + TDS == gross</c> to the paisa.
    /// </summary>
    [Fact]
    public void The_carve_out_credits_the_party_the_net_of_the_grandfathered_deduction()
    {
        var c = NewTdsCompany();
        var party = Contractor(c, DeducteeType.Company);

        var carve = new TdsService(c).BuildCarveOut(
            Money.FromRupees(50_000.30m), Money.FromRupees(50_000.30m), Nature(c, "194C"), party, D1,
            postedRateBasisPoints: 100);

        Assert.Equal(100, carve.Withholding.RateBasisPoints);
        Assert.Equal(Money.FromRupees(500m), carve.TdsAmount);
        Assert.Equal(Money.FromRupees(49_500.30m), carve.NetPartyAmount);
        Assert.Equal(Money.FromRupees(50_000.30m), carve.NetPartyAmount + carve.TdsAmount);
    }
}
