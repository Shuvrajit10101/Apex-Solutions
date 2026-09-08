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
/// 🔴 <b>CENSUS ROW 6.35 — THE TDS LONG TAIL, SECOND INSTALMENT: §194G, §194K, §194LA and §194-O.</b> (§192A and
/// §194EE, added in the same pass, are proved in <see cref="TdsInclusiveThresholdTests"/> because what unblocked
/// them was the boundary flag.)
///
/// <para>🔴 <b>WHAT UNBLOCKED THESE FOUR WAS NOT NEW LAW — IT WAS OUR OWN VINTAGE ERROR, AND TWO OF THEM WOULD
/// HAVE SHIPPED WRONG FIGURES HAD THE EARLIER READING BEEN BELIEVED.</b> The previous pass rejected all four on
/// gate G2 ("the Act text must agree with the Department's rate chart") after reading the plain and low-numbered
/// slugs. Every one of those slugs serves an <b>archived</b> text. The Department publishes each historical
/// version of a section under its own numbered slug in <b>no predictable order</b> — for §194G the years run 2000,
/// 2009, 2001, 2010, 1991, … — so a slug's number means nothing and its <b>"Year" metadata field</b> means
/// everything. Reading that field off each slug located the Year-2025 (= FY 2025-26) text of all four, and at that
/// vintage all four agree with the chart. The two figures that would have been wrong:
/// <list type="bullet">
///   <item><b>§194-O</b> — the archived slugs read "one per cent"; the FY 2025-26 text reads <b>0.1 per cent</b>.
///     Ten times the correct deduction.</item>
///   <item><b>§194LA</b> — the archived slug read "one hundred thousand rupees"; the FY 2025-26 text reads
///     <b>five lakh rupees</b>. A ₹1,00,001 compensation would have borne ₹10,000.00 the statute exempts.</item>
/// </list></para>
///
/// <para><b>The statute, quoted from the Year-2025 page in each case.</b>
/// <list type="bullet">
///   <item><b>§194G(1)</b>, <c>https://www.incometaxindia.gov.in/w/section-194g-34</c>: any person paying a person
///     "stocking, distributing, purchasing or selling lottery tickets, any income by way of commission,
///     remuneration or prize … <b>in an amount exceeding twenty thousand rupees</b> shall … deduct income-tax
///     thereon at the rate of <b>two per cent</b>." Note the limb qualifies the <b>income being paid</b>, and the
///     section has no aggregate limb and no "during the financial year" anywhere — hence a SINGLE-TRANSACTION
///     threshold.</item>
///   <item><b>§194K</b>, <c>…/w/section-194k-30</c>: income in respect of units of a §10(23D) Mutual Fund, of the
///     Administrator of the specified undertaking or of the specified company — "deduct income-tax thereon at the
///     rate of <b>ten per cent</b>"; proviso (i) exempts an FY aggregate that "<b>does not exceed ten thousand
///     rupees</b>". This is the section re-inserted in 2020, <b>not</b> the one omitted in 1999 that the plain
///     slug serves (20%/15%, "no deduction … on or after the 1st day of June, 1999").</item>
///   <item><b>§194LA</b>, <c>…/w/section-194la-21</c>: compensation on the compulsory acquisition of immovable
///     property other than agricultural land — "deduct an amount equal to <b>ten per cent</b> of such sum";
///     proviso: no deduction where the FY aggregate "<b>does not exceed five lakh rupees</b>".</item>
///   <item><b>§194-O(1)</b>, <c>…/w/section-194-o-6</c>: an e-commerce operator "shall … deduct income-tax at the
///     rate of <b>0.1 per cent</b> of the gross amount of such sales or services or both"; <b>§194-O(2)</b>: no
///     deduction from a sum paid to a participant "<b>being an individual or Hindu undivided family</b>, where the
///     gross amount … does not exceed <b>five lakh rupees</b> <b>and</b> such e-commerce participant <b>has
///     furnished his Permanent Account Number or Aadhaar number</b>".</item>
///   <item><b>§206AA</b>, <c>…/w/section-206aa-16</c> (Year 2025): the no-PAN rate is the higher of the section
///     rate, the rates in force, or twenty per cent — <b>except</b> that its first proviso reads "twenty per cent"
///     as "<b>five per cent</b>" where the deduction is under <b>§194-O</b>. So §194-O's no-PAN rate is 5% and the
///     other three take the plain 20%.</item>
/// </list>
/// Form-26Q codes <c>94G</c>, <c>94K</c>, <c>4LA</c> and <c>94O</c> are from the notified form's own section-code
/// table; see <see cref="Tds194IFvuSectionCodeTests"/>, which holds the whole seed against it.</para>
///
/// <para>🔴 <b>AND G4 IS WITHDRAWN.</b> All four had also been rejected under a gate reading "the deductor must
/// plausibly be this product's user". That gate was ours, not the vendor's. Under R7 the vendor's published
/// documentation is the fidelity ground truth, and TallyPrime's own Nature-of-Payment helper list
/// (<c>https://help.tallysolutions.com/tds-nature-of-payment-income-tax-act/</c>, read 2026-09-08) ships every one
/// of them — "Income from Stocking, Distributing, Purchasing or Selling Lottery Tickets", "Income from Units of
/// Specified Mutual Fund…", "Payment of Compensation on Acquisition of Certain Immovable Property" and "Payments
/// to Any E-Commerce Participant". The master is a selectable list, not a prediction about who the operator is.
/// </para>
///
/// <para><b>Every assertion below fails on the tree as it stood before this slice</b>, where
/// <c>FindNatureOfPaymentByCode("194G")</c> and its three siblings returned <c>null</c>.</para>
/// </summary>
public class TdsLongTailSecondInstalmentTests
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";

    private static readonly DateOnly Fy = new(2025, 4, 1);
    private static readonly DateOnly May = new(2025, 5, 10);
    private static readonly DateOnly Jun = new(2025, 6, 10);

    private static Company NewBook()
    {
        var c = CompanyFactory.CreateSeeded("Long Tail Co", Fy);
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan });
        return c;
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Domain.Ledger Payee(
        Company c, DeducteeType? type = DeducteeType.Individual, string? pan = DeducteePan)
    {
        var l = AddLedger(c, $"Payee-{Guid.NewGuid():N}", "Sundry Creditors", false);
        l.DeducteeType = type;
        l.PartyPan = pan;
        return l;
    }

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
    //  1. All six are actually in the master — the "branch named for the row, carrying none of it" guard
    // =================================================================================================

    /// <summary>
    /// The six sections are seeded, predefined, priced and coded. A branch was once <i>named</i> for row 6.35 and
    /// merged carrying zero of it, caught by a reviewer rather than the builder — so the row's own existence is
    /// asserted before any behaviour is.
    /// </summary>
    [Theory]
    [InlineData("192A", 1000, 2000, "192A")]
    [InlineData("194EE", 1000, 2000, "4EE")]
    [InlineData("194G", 200, 2000, "94G")]
    [InlineData("194K", 1000, 2000, "94K")]
    [InlineData("194LA", 1000, 2000, "4LA")]
    [InlineData("194-O", 10, 500, "94O")]
    public void The_section_is_seeded_with_its_sourced_rate_and_its_notified_code(
        string section, int withPanBp, int withoutPanBp, string fvu)
    {
        var n = NewBook().FindNatureOfPaymentByCode(section);

        Assert.NotNull(n);
        Assert.True(n!.IsPredefined);
        Assert.Equal(withPanBp, n.RateWithPanBp);
        Assert.Equal(withoutPanBp, n.RateWithoutPanBp);
        Assert.Equal(fvu, n.NotifiedFvuSectionCode);
        Assert.Equal(new DateOnly(2025, 4, 1), n.EffectiveFrom);
        // None of the six is a §194C-style deductee-type rate branch or a §194-I per-month window; asserting it
        // keeps a future edit from handing one of them a rule that belongs to another section.
        Assert.False(n.RateTurnsOnDeducteeType);
        Assert.False(n.ThresholdWindowIsPerMonth);
        Assert.False(n.ChargesOnlyExcessOverCumulativeThreshold);
    }

    // =================================================================================================
    //  2. §194G — a PER-PAYMENT limb, which an FY aggregate would have got wrong
    // =================================================================================================

    /// <summary>₹20,001 of lottery commission — "in an amount exceeding twenty thousand rupees" — bears two per
    /// cent of the whole payment: ₹400.02, nearest rupee <b>₹400.00</b>.</summary>
    [Fact]
    public void A_194G_commission_above_its_per_payment_limb_is_taxed_at_two_percent()
    {
        var c = NewBook();
        var nop = c.FindNatureOfPaymentByCode("194G")!;

        var w = new TdsService(c).ComputeWithholding(Money.FromRupees(20_001m), nop, Payee(c), May);

        Assert.True(w.Applies);
        Assert.Equal(200, w.RateBasisPoints);
        Assert.Equal(Money.FromRupees(400m), w.TdsAmount);
    }

    /// <summary>At exactly ₹20,000 it is exempt — the Act's "exceeding" is strict, so this limb is NOT
    /// inclusive.</summary>
    [Fact]
    public void A_194G_commission_of_exactly_twenty_thousand_is_exempt()
    {
        var c = NewBook();
        var nop = c.FindNatureOfPaymentByCode("194G")!;

        Assert.False(nop.AggregateThresholdIsInclusive);
        Assert.False(new TdsService(c).ComputeWithholding(Money.FromRupees(20_000m), nop, Payee(c), May).Applies);
    }

    /// <summary>
    /// 🔴 <b>THE SHAPE TEST, AND THE DEFECT IT LOCKS OUT.</b> §194G's limb qualifies the income being paid — it is
    /// a per-payment limb, and the section carries no aggregate limb at all. Two ₹15,000 commissions in one
    /// financial year total ₹30,000 and are <b>both exempt</b>, because neither payment exceeds ₹20,000. Seeding
    /// the ₹20,000 as a cumulative-FY threshold instead would have made the second one liable and withheld ₹300.00
    /// the statute does not ask for.
    /// </summary>
    [Fact]
    public void Two_194G_commissions_below_the_limb_stay_exempt_however_they_accumulate()
    {
        var c = NewBook();
        var nop = c.FindNatureOfPaymentByCode("194G")!;
        var party = Payee(c);

        Assert.NotNull(nop.SingleTransactionThreshold);
        Assert.Null(nop.CumulativeThreshold);          // the shape: no aggregate limb exists on this section
        Assert.Null(nop.AggregateThreshold);

        Assert.False(new TdsService(c).ComputeWithholding(Money.FromRupees(15_000m), nop, party, May).Applies);
        Book(c, party, nop, May, Money.FromRupees(15_000m));

        var second = new TdsService(c).ComputeWithholding(Money.FromRupees(15_000m), nop, party, Jun);
        Assert.Equal(Money.FromRupees(15_000m), second.PriorCumulativeInFy);   // the year HAS accumulated…
        Assert.False(second.Applies);                                          // …and it changes nothing here
        Assert.Equal(Money.Zero, second.TdsAmount);
    }

    // =================================================================================================
    //  3. §194K and §194LA — ordinary FY aggregates, at their corrected figures
    // =================================================================================================

    /// <summary>₹10,000.50 of unit income crosses §194K's ₹10,000 FY limb: ten per cent is ₹1,000.05, nearest
    /// rupee <b>₹1,000.00</b>.</summary>
    [Fact]
    public void A_194K_unit_income_above_ten_thousand_is_taxed_at_ten_percent()
    {
        var c = NewBook();
        var nop = c.FindNatureOfPaymentByCode("194K")!;

        var w = new TdsService(c).ComputeWithholding(new Money(10_000.50m), nop, Payee(c), May);

        Assert.True(w.Applies);
        Assert.Equal(1000, w.RateBasisPoints);
        Assert.Equal(Money.FromRupees(1_000m), w.TdsAmount);
    }

    /// <summary>
    /// 🔴 <b>§194K's THRESHOLD FIGURE ITSELF, PINNED FROM BOTH SIDES — added because a mutation proved it was
    /// not.</b> Dropping the seeded ₹10,000 to ₹1,000 left every test in this file green: the ₹10,000.50 case
    /// above crosses a ₹1,000 limb just as happily as a ₹10,000 one, so it measured the RATE and not the LIMB.
    /// The proviso reads "does not exceed 92[ten] thousand rupees", so ₹10,000 exactly is exempt and ₹10,001 is
    /// liable — ₹1,000.00 on the whole sum. A boundary is only pinned by a pair of assertions straddling it.
    /// </summary>
    [Fact]
    public void The_194K_threshold_is_exactly_ten_thousand_and_is_exclusive()
    {
        var c = NewBook();
        var nop = c.FindNatureOfPaymentByCode("194K")!;
        var svc = new TdsService(c);

        Assert.Equal(Money.FromRupees(10_000m), nop.AggregateThreshold);
        Assert.Null(nop.SingleTransactionThreshold);

        Assert.False(svc.ComputeWithholding(Money.FromRupees(10_000m), nop, Payee(c), May).Applies);

        var over = svc.ComputeWithholding(Money.FromRupees(10_001m), nop, Payee(c), May);
        Assert.True(over.Applies);
        Assert.Equal(Money.FromRupees(1_000m), over.TdsAmount);
    }

    /// <summary>
    /// The same straddle for §194G's per-payment limb and §192A/§194EE's inclusive ones is in
    /// <see cref="TdsInclusiveThresholdTests"/>; this pins the remaining second-instalment figures here so no
    /// section's threshold rests on a single one-sided assertion. §194LA ₹5,00,000 and §194-O ₹5,00,000 are
    /// straddled by their own tests above.
    /// </summary>
    [Theory]
    [InlineData("194G", 20_000)]
    [InlineData("194K", 10_000)]
    [InlineData("194LA", 5_00_000)]
    [InlineData("194-O", 5_00_000)]
    [InlineData("192A", 50_000)]
    [InlineData("194EE", 2_500)]
    public void Each_second_instalment_section_carries_exactly_its_sourced_threshold_figure(
        string section, int rupees)
    {
        var nop = NewBook().FindNatureOfPaymentByCode(section)!;
        var limb = nop.SingleTransactionThreshold ?? nop.AggregateThreshold;

        Assert.Equal(Money.FromRupees(rupees), limb);
    }

    /// <summary>
    /// 🔴 <b>§194LA's THRESHOLD IS ₹5,00,000, AND THE ARCHIVED ₹1,00,000 WOULD HAVE OVER-DEDUCTED.</b> A
    /// compensation of ₹1,00,001 — one rupee over the figure the earlier pass read — is <b>exempt</b>, because the
    /// FY 2025-26 proviso exempts an aggregate that does not exceed five lakh rupees. Had the archived figure been
    /// seeded this payment would have borne <b>₹10,000.00</b>.
    /// </summary>
    [Fact]
    public void A_194LA_compensation_just_over_the_archived_figure_is_exempt_at_the_real_threshold()
    {
        var c = NewBook();
        var nop = c.FindNatureOfPaymentByCode("194LA")!;

        Assert.Equal(Money.FromRupees(5_00_000m), nop.AggregateThreshold);

        var w = new TdsService(c).ComputeWithholding(Money.FromRupees(1_00_001m), nop, Payee(c), May);

        Assert.False(w.Applies);
        Assert.Equal(Money.Zero, w.TdsAmount);
        Assert.NotEqual(Money.FromRupees(10_000m), w.TdsAmount);   // the literal figure the archived text implied
    }

    /// <summary>Above the real threshold it is liable, on the whole sum: ₹5,00,001 at ten per cent is
    /// <b>₹50,000.00</b> (₹50,000.10, nearest rupee).</summary>
    [Fact]
    public void A_194LA_compensation_above_five_lakh_is_taxed_on_the_whole_sum()
    {
        var c = NewBook();
        var nop = c.FindNatureOfPaymentByCode("194LA")!;

        var w = new TdsService(c).ComputeWithholding(Money.FromRupees(5_00_001m), nop, Payee(c), May);

        Assert.True(w.Applies);
        Assert.Equal(Money.FromRupees(50_000m), w.TdsAmount);
    }

    // =================================================================================================
    //  4. §194-O — the conditional exemption, which either approximation would have got wrong
    // =================================================================================================

    /// <summary>
    /// 🔴 <b>THE FIRST WRONG ANSWER A FLAT THRESHOLD WOULD HAVE GIVEN.</b> §194-O(2) grants the exemption only to a
    /// participant "being an individual or Hindu undivided family". A <b>company</b> participant has no exemption
    /// and is liable from the first rupee. On a ₹4,00,000 payout — comfortably under ₹5,00,000 — the statute takes
    /// 0.1 per cent, <b>₹400.00</b>. A plain cumulative limb would have withheld <b>₹0.00</b>.
    /// </summary>
    [Fact]
    public void A_company_e_commerce_participant_is_liable_below_the_threshold()
    {
        var c = NewBook();
        var nop = c.FindNatureOfPaymentByCode("194-O")!;
        var party = Payee(c, DeducteeType.Company);

        var w = new TdsService(c).ComputeWithholding(Money.FromRupees(4_00_000m), nop, party, May);

        Assert.True(w.Applies);
        Assert.Equal(10, w.RateBasisPoints);
        Assert.Equal(Money.FromRupees(400m), w.TdsAmount);
        Assert.NotEqual(Money.Zero, w.TdsAmount);   // the literal figure a flat ₹5,00,000 limb would have given
    }

    /// <summary>A firm is likewise outside "individual or Hindu undivided family" and liable from the first
    /// rupee — ₹1,000 of payout bears ₹1.00.</summary>
    [Fact]
    public void A_firm_e_commerce_participant_is_liable_from_the_first_rupee()
    {
        var c = NewBook();
        var nop = c.FindNatureOfPaymentByCode("194-O")!;

        var w = new TdsService(c).ComputeWithholding(Money.FromRupees(1_000m), nop, Payee(c, DeducteeType.Firm), May);

        Assert.True(w.Applies);
        Assert.Equal(Money.FromRupees(1m), w.TdsAmount);
    }

    /// <summary>
    /// 🔴 <b>THE SECOND WRONG ANSWER — the opposite one, which seeding no threshold at all would have given.</b> An
    /// <b>individual</b> participant who has furnished a PAN and is under ₹5,00,000 for the year is exactly who
    /// §194-O(2) protects, and nothing is withheld. Both this test and the one above must pass, and no single flat
    /// threshold can satisfy both.
    /// </summary>
    [Fact]
    public void An_individual_participant_with_a_PAN_below_the_threshold_is_exempt()
    {
        var c = NewBook();
        var nop = c.FindNatureOfPaymentByCode("194-O")!;

        var w = new TdsService(c).ComputeWithholding(
            Money.FromRupees(4_00_000m), nop, Payee(c, DeducteeType.Individual), May);

        Assert.False(w.Applies);
        Assert.Equal(Money.Zero, w.TdsAmount);
    }

    /// <summary>A HUF participant gets the same protection — the sub-section names both.</summary>
    [Fact]
    public void A_HUF_participant_with_a_PAN_below_the_threshold_is_exempt()
    {
        var c = NewBook();
        var nop = c.FindNatureOfPaymentByCode("194-O")!;

        Assert.False(new TdsService(c).ComputeWithholding(
            Money.FromRupees(4_00_000m), nop, Payee(c, DeducteeType.HinduUndividedFamily), May).Applies);
    }

    /// <summary>The exemption's own boundary is "does not exceed", so it is <b>exclusive</b>: at exactly ₹5,00,000
    /// the protected individual is still exempt, and one rupee over it the whole sum is liable —
    /// ₹5,00,001 × 0.1% = ₹500.00.</summary>
    [Fact]
    public void The_protected_individuals_exemption_ends_strictly_above_five_lakh()
    {
        var c = NewBook();
        var nop = c.FindNatureOfPaymentByCode("194-O")!;
        var svc = new TdsService(c);

        Assert.False(nop.AggregateThresholdIsInclusive);
        Assert.False(svc.ComputeWithholding(Money.FromRupees(5_00_000m), nop, Payee(c), May).Applies);

        var over = svc.ComputeWithholding(Money.FromRupees(5_00_001m), nop, Payee(c), May);
        Assert.True(over.Applies);
        Assert.Equal(Money.FromRupees(500m), over.TdsAmount);
    }

    /// <summary>
    /// 🔴 <b>NO PAN ⇒ NO EXEMPTION <i>AND</i> THE §206AA RATE — the two move together, and both are §194-O
    /// specific.</b> The exemption requires the participant to have "furnished his Permanent Account Number or
    /// Aadhaar number", so an individual without one is liable from the first rupee; and §206AA's <b>first</b>
    /// proviso substitutes "five per cent" for "twenty per cent" on §194-O alone. ₹1,00,000 of payout therefore
    /// bears <b>₹5,000.00</b>, not ₹100.00 and not ₹20,000.00.
    /// </summary>
    [Fact]
    public void An_individual_participant_without_a_PAN_is_liable_from_the_first_rupee_at_five_percent()
    {
        var c = NewBook();
        var nop = c.FindNatureOfPaymentByCode("194-O")!;
        var party = Payee(c, DeducteeType.Individual, pan: null);

        var w = new TdsService(c).ComputeWithholding(Money.FromRupees(1_00_000m), nop, party, May);

        Assert.True(w.Applies);
        Assert.False(w.PanApplied);
        Assert.Equal(500, w.RateBasisPoints);                       // 5%, not §206AA's usual 20%
        Assert.Equal(Money.FromRupees(5_000m), w.TdsAmount);
        Assert.NotEqual(Money.FromRupees(20_000m), w.TdsAmount);
    }

    /// <summary>
    /// An unrecorded deductee type is <b>refused by name</b> rather than guessed, exactly as §194C refuses one:
    /// guessing "individual" would skip a deduction a company owes, and guessing "company" would withhold from a
    /// protected individual seller. Both directions move money, so neither is taken.
    /// </summary>
    [Fact]
    public void A_194O_participant_with_no_deductee_type_recorded_is_refused_rather_than_guessed()
    {
        var c = NewBook();
        var nop = c.FindNatureOfPaymentByCode("194-O")!;
        var party = Payee(c, type: null);

        var ex = Assert.Throws<InvalidOperationException>(
            () => new TdsService(c).ComputeWithholding(Money.FromRupees(4_00_000m), nop, party, May));

        Assert.Contains("deductee type", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("194-O", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 🔴 <b>THE CONDITIONAL EXEMPTION IS §194-O's ALONE.</b> Every other seeded section's threshold is
    /// unconditional, so a company deductee gets exactly the same limb an individual does. Asserted over the whole
    /// seed so a future row cannot quietly acquire the condition — which would silently stop protecting whoever
    /// its own statute protects.
    /// </summary>
    [Fact]
    public void Exactly_one_seeded_nature_has_a_deductee_conditional_threshold_and_it_is_194O()
    {
        var conditional = SeedTdsTcsRates.BuildTdsDefaults()
            .Where(n => n.AggregateThresholdAppliesOnlyToIndividualHufWithPan)
            .Select(n => n.SectionCode)
            .ToList();

        Assert.Equal(new[] { "194-O" }, conditional);
    }

    /// <summary>
    /// And an ordinary section is unmoved by a company deductee: §194J(b) at ₹60,000 withholds ₹6,000.00 whether
    /// the payee is an individual or a company. This is the regression the §194-O work could have caused.
    /// </summary>
    [Theory]
    [InlineData(DeducteeType.Individual)]
    [InlineData(DeducteeType.Company)]
    [InlineData(DeducteeType.Firm)]
    public void An_unconditional_sections_threshold_is_the_same_for_every_deductee_type(DeducteeType type)
    {
        var c = NewBook();
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;

        var w = new TdsService(c).ComputeWithholding(Money.FromRupees(60_000m), nop, Payee(c, type), May);

        Assert.True(w.Applies);
        Assert.Equal(Money.FromRupees(6_000m), w.TdsAmount);
    }

    // =================================================================================================
    //  5. The rejected-sections register still bites
    // =================================================================================================

    /// <summary>
    /// The six sections seeded in this pass are gone from <see cref="SeedTdsTcsRates.LongTailSectionsNotSeeded"/>,
    /// and every section still named there is genuinely absent from the seed. The register is the mechanism that
    /// stops the next pass seeding a rate off the chart, so it has to stay true in both directions.
    /// </summary>
    [Fact]
    public void The_rejected_register_and_the_seed_do_not_overlap_and_the_six_have_left_it()
    {
        var seeded = SeedTdsTcsRates.BuildTdsDefaults().Select(n => n.SectionCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rejected = SeedTdsTcsRates.LongTailSectionsNotSeeded.Keys.ToList();

        foreach (var section in new[] { "192A", "194EE", "194G", "194K", "194LA", "194-O" })
        {
            Assert.Contains(section, seeded);
            Assert.DoesNotContain(section, rejected, StringComparer.OrdinalIgnoreCase);
        }

        var overlap = rejected.Where(seeded.Contains).ToList();
        Assert.True(overlap.Count == 0,
            "These sections are BOTH seeded and recorded as rejected: " + string.Join(", ", overlap));

        // Every surviving entry carries a reason naming the gate it fails, so a removal is a claim someone made.
        Assert.All(SeedTdsTcsRates.LongTailSectionsNotSeeded,
            kv => Assert.Matches(@"G[1-4]", kv.Value));
    }
}
