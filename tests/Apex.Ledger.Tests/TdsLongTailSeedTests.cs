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
/// 🔴 <b>CENSUS ROW 6.35 — THE TDS LONG TAIL, FIRST INSTALMENT (§194T, §194R, §194S).</b>
///
/// <para><b>Why this file has to exist at all, in its own words.</b> A branch was once <i>named</i> for row 6.35
/// and merged carrying <b>zero</b> of it; the census records that a reader must not read
/// <c>claude/apex-f4-gst-tds-tail</c> in the merge history as coverage, because a three-dot diff showed it
/// touched eleven files and not one of them was a TDS file. The row was claimed and unbuilt, and it was a
/// reviewer who caught that, not the builder. So this file asserts the three sections are <b>seeded, priced,
/// gated and actually withheld through the real engine</b> — not that a viewmodel flag is set somewhere.</para>
///
/// <para><b>Every assertion below fails on the tree as it stood before this slice</b>, where
/// <c>SeedTdsTcsRates.BuildTdsDefaults()</c> returned eight natures and <c>FindNatureOfPaymentByCode("194T")</c>
/// returned <c>null</c>.</para>
///
/// <para><b>The statutory ground, quoted once here and in full in the seed.</b>
/// <list type="bullet">
///   <item><b>§194T(1)</b> — "Any person, being a firm, responsible for paying any sum in the nature of salary,
///     remuneration, commission, bonus or interest to a partner of the firm … deduct income-tax thereon at the
///     rate of <b>ten per cent</b>"; <b>§194T(2)</b> — "No deduction shall be made … where such sum or the
///     aggregate of such sums … <b>does not exceed twenty thousand rupees</b> during the financial year."
///     Inserted by the Finance (No. 2) Act, 2024, <b>w.e.f. 1-4-2025</b>.
///     <c>https://www.incometaxindia.gov.in/w/section-194t</c> and <c>…/w/section-194t-1</c>, read 2026-09-08.</item>
///   <item><b>§194R(1)</b> — "…ensure that tax has been deducted in respect of such benefit or perquisite at the
///     rate of <b>ten per cent</b>"; second proviso — the section does not apply where the value "…<b>does not
///     exceed twenty thousand rupees</b>". <c>…/w/section-194r</c> and <c>…/w/section-194r-2</c>.</item>
///   <item><b>§194S(1)</b> — "deduct an amount equal to <b>one per cent</b> of such sum"; <b>§194S(3)(b)</b> —
///     no deduction where the consideration "is payable by any person other than a specified person and the
///     value or aggregate value … <b>does not exceed ten thousand rupees</b> during the financial year".
///     <c>…/w/section-194s</c> and <c>…/w/section-194s-2</c>.</item>
///   <item><b>§206AA(1)</b> — the no-PAN rate is the higher of the section rate, the rates in force, or twenty
///     per cent; its two "five per cent" provisos name <b>only</b> §194-O and §194Q, so all three of these
///     sections take the plain 20%. <c>…/w/section-206aa-16</c>.</item>
///   <item>The Form-26Q codes <b>94T / 94R / 94S</b> come from the <b>notified form itself</b> —
///     <c>https://www.incometaxindia.gov.in/documents/d/guest/103120000000007861-pdf-2</c>, the
///     "Section | Nature of Payment | Section Code" table on its last two pages, read 2026-09-08. That source
///     matters because <c>FvuWriter</c>, <c>Form26Q</c> and <c>Form16APdf</c> all emit this code into a filed
///     return.</item>
/// </list></para>
/// </summary>
public class TdsLongTailSeedTests
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";

    private static readonly DateOnly Fy = new(2025, 4, 1);
    private static readonly DateOnly May = new(2025, 5, 10);
    private static readonly DateOnly Jun = new(2025, 6, 10);

    private static Company NewFirm()
    {
        var c = CompanyFactory.CreateSeeded("Long Tail Co", Fy);
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan });
        return c;
    }

    private static Domain.Ledger Payee(Company c, string? pan = DeducteePan)
    {
        var l = new Domain.Ledger(
            Guid.NewGuid(), $"Payee-{Guid.NewGuid():N}", c.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, openingIsDebit: false);
        l.DeducteeType = DeducteeType.Individual;
        l.PartyPan = pan;
        c.AddLedger(l);
        return l;
    }

    private static NatureOfPayment Nature(Company c, string code) => c.FindNatureOfPaymentByCode(code)!;

    // =====================================================================================================
    //  1. The three sections exist, with the right figures and the right FORM-26Q code
    // =====================================================================================================

    /// <summary>
    /// 🔴 <b>THE ROW ITSELF.</b> Each new section is present in the seed, is marked predefined, carries the rate
    /// and threshold quoted from the bare Act, and carries the section code taken from the notified Form 26Q.
    ///
    /// <para>The <b>FVU code</b> is asserted character-for-character on purpose. It is not a label: it is what
    /// goes into the filed quarterly statement, and a code copied from a vendor utility or guessed from the
    /// neighbouring rows' pattern would be a wrong figure in a return that a deductor signs.</para>
    /// </summary>
    [Theory]
    // section, name fragment, with-PAN bp, no-PAN bp, Form-26Q code, cumulative-FY threshold ₹
    [InlineData("194T", "partner", 1000, 2000, "94T", 20_000)]
    [InlineData("194R", "perquisite", 1000, 2000, "94R", 20_000)]
    [InlineData("194S", "virtual digital asset", 100, 2000, "94S", 10_000)]
    public void Each_long_tail_section_is_seeded_with_its_statutory_rate_threshold_and_form26Q_code(
        string section, string nameFragment, int withPanBp, int withoutPanBp, string fvu, int cumulativeRupees)
    {
        var n = Assert.Single(SeedTdsTcsRates.BuildTdsDefaults(),
            x => string.Equals(x.SectionCode, section, StringComparison.OrdinalIgnoreCase));

        Assert.Contains(nameFragment, n.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(withPanBp, n.RateWithPanBp);
        Assert.Equal(withoutPanBp, n.RateWithoutPanBp);
        Assert.Equal(fvu, n.FvuSectionCode);
        Assert.Equal(Money.FromRupees(cumulativeRupees), n.CumulativeThreshold);
        Assert.True(n.IsPredefined);

        // None of the three has a single-transaction limb: all three statutory provisos are financial-year
        // aggregates. A stray single-txn figure would deduct on a first small payment the statute exempts.
        Assert.Null(n.SingleTransactionThreshold);

        // 🔴 And none of them may pick up one of the three DERIVED behaviours that belong to other sections.
        // These are derived from the SECTION CODE, so a badly chosen code is all it would take.
        Assert.False(n.ThresholdWindowIsPerMonth, $"§{section} must not take §194-I's per-month rent window.");
        Assert.False(n.RateTurnsOnDeducteeType, $"§{section} must not take §194C's individual/HUF rate split.");
        Assert.False(n.ChargesOnlyExcessOverCumulativeThreshold,
            $"§{section} is a QUALIFYING GATE: once the threshold is exceeded the WHOLE sum bears the tax. " +
            "Only §194Q charges on the excess alone, and giving another section that carve would under-deduct.");

        // The window the engine actually consults must be the stored FY figure, not something else.
        Assert.Equal(Money.FromRupees(cumulativeRupees), n.AggregateThreshold);

        // Seeded at the FY the whole table encodes.
        Assert.Equal(Fy, n.EffectiveFrom);
    }

    /// <summary>The three are reachable on a company by the same lookup every screen and the engine use — a
    /// nature that is in <c>BuildTdsDefaults</c> but not on an enabled company would be seeded and invisible.</summary>
    [Theory]
    [InlineData("194T")]
    [InlineData("194R")]
    [InlineData("194S")]
    public void Enabling_TDS_puts_each_long_tail_section_on_the_company(string section)
    {
        var c = NewFirm();
        Assert.NotNull(c.FindNatureOfPaymentByCode(section));
        Assert.Contains(c.NaturesOfPayment, n => n.SectionCode == section);
    }

    // =====================================================================================================
    //  2. The ENGINE actually withholds — figures, not flags
    // =====================================================================================================

    /// <summary>
    /// 🔴 <b>§194T, THE ONE AN INDIAN FIRM ACTUALLY HITS, DRIVEN THROUGH THE REAL ENGINE WITH REAL MONEY.</b>
    /// A firm credits a partner ₹25,000 of remuneration in one go. ₹25,000 exceeds the ₹20,000 financial-year
    /// limb, and §194T is a qualifying gate rather than an excess-only carve, so the tax is ten per cent of the
    /// <b>whole</b> ₹25,000 = <b>₹2,500.00</b> — not ten per cent of the ₹5,000 excess (₹500.00), which is what
    /// a copy of the §194Q shape would have produced.
    /// </summary>
    [Fact]
    public void S194T_withholds_ten_per_cent_of_the_whole_sum_once_the_twenty_thousand_gate_is_crossed()
    {
        var c = NewFirm();
        var partner = Payee(c);

        var w = new TdsService(c).ComputeWithholding(
            Money.FromRupees(25_000m), Nature(c, "194T"), partner, May);

        Assert.True(w.Applies);
        Assert.Equal(1000, w.RateBasisPoints);
        Assert.Equal(Money.FromRupees(2_500m), w.TdsAmount);
        Assert.NotEqual(Money.FromRupees(500m), w.TdsAmount); // the §194Q excess-only shape, explicitly refused
    }

    /// <summary>
    /// 🔴 <b>THE BOUNDARY, WHICH IS THE HALF THAT GETS WRITTEN WRONG.</b> §194T(2) exempts a sum that "does not
    /// exceed twenty thousand rupees", so <b>exactly ₹20,000 withholds nothing</b> and one paisa more withholds.
    /// The engine's aggregate test is strictly greater, which is what makes this the boundary the statute names;
    /// a section whose proviso instead read "is less than ₹20,000" could NOT be modelled this way, and that is
    /// why §192A and §194EE are on <see cref="SeedTdsTcsRates.LongTailSectionsNotSeeded"/>.
    /// </summary>
    [Theory]
    [InlineData(19_999.99, false, 0)]
    [InlineData(20_000.00, false, 0)]      // "does not exceed" ⇒ AT the figure, nothing is withheld
    [InlineData(20_000.01, true, 2000)]    // one paisa over ⇒ 10% of the WHOLE ₹20,000.01, to the nearest rupee
    public void S194T_deducts_only_ABOVE_twenty_thousand_never_at_it(
        decimal rupees, bool expectApplies, decimal expectTdsRupees)
    {
        var c = NewFirm();
        var partner = Payee(c);

        var w = new TdsService(c).ComputeWithholding(new Money(rupees), Nature(c, "194T"), partner, May);

        Assert.Equal(expectApplies, w.Applies);
        Assert.Equal(Money.FromRupees(expectTdsRupees), w.TdsAmount);
    }

    /// <summary>
    /// The ₹20,000 limb is a <b>financial-year aggregate</b>, not a per-payment test: two ₹12,000 credits in
    /// different months cross it on the second, and the second bears tax on its own full ₹12,000 (the gate is
    /// qualifying — the first payment is not retrospectively taxed by this call).
    /// </summary>
    [Fact]
    public void S194T_aggregates_across_the_financial_year_rather_than_testing_each_payment()
    {
        var c = NewFirm();
        var partner = Payee(c);
        var nature = Nature(c, "194T");
        var svc = new TdsService(c);

        var first = svc.ComputeWithholding(Money.FromRupees(12_000m), nature, partner, May);
        Assert.False(first.Applies);   // ₹12,000 alone does not exceed ₹20,000

        // Book the first credit so the engine's own FY projection can see it.
        BookOne(c, partner, nature, May, Money.FromRupees(12_000m));

        var second = svc.ComputeWithholding(Money.FromRupees(12_000m), nature, partner, Jun);
        Assert.True(second.Applies);   // ₹24,000 aggregate now exceeds ₹20,000
        Assert.Equal(Money.FromRupees(1_200m), second.TdsAmount);
    }

    /// <summary>§194S is priced at <b>one</b> per cent, not ten — the one rate in this instalment that differs,
    /// and the one a copy-paste from the neighbouring rows would silently get wrong by a factor of ten.</summary>
    [Fact]
    public void S194S_withholds_one_per_cent_not_ten()
    {
        var c = NewFirm();
        var seller = Payee(c);

        var w = new TdsService(c).ComputeWithholding(
            Money.FromRupees(2_00_000m), Nature(c, "194S"), seller, May);

        Assert.True(w.Applies);
        Assert.Equal(100, w.RateBasisPoints);
        Assert.Equal(Money.FromRupees(2_000m), w.TdsAmount);
    }

    /// <summary>
    /// No PAN ⇒ §206AA's twenty per cent, on all three. 🔴 <b>NOT five per cent</b>: §206AA's two provisos
    /// substitute "five per cent" for §194-O and §194Q <b>only</b>, and §194Q is a seeded neighbour of these
    /// three, so inheriting its 5% would be the easy mistake to make.
    /// </summary>
    [Theory]
    [InlineData("194T")]
    [InlineData("194R")]
    [InlineData("194S")]
    public void A_deductee_without_a_PAN_takes_the_plain_section_206AA_twenty_per_cent(string section)
    {
        var c = NewFirm();
        var noPan = Payee(c, pan: null);

        var w = new TdsService(c).ComputeWithholding(
            Money.FromRupees(1_00_000m), Nature(c, section), noPan, May);

        Assert.True(w.Applies);
        Assert.Equal(2000, w.RateBasisPoints);
        Assert.Equal(Money.FromRupees(20_000m), w.TdsAmount);
        Assert.False(w.PanApplied);
    }

    // =====================================================================================================
    //  3. The rejection list is a LOCK, not a comment
    // =====================================================================================================

    /// <summary>
    /// 🔴 <b>NO SECTION RECORDED AS "EVALUATED AND REJECTED" MAY APPEAR IN THE SEED.</b>
    ///
    /// <para>This is the guard that a comment could not be. Eleven long-tail candidates were taken to the bare
    /// Act and failed a stated gate — §194-O's rate could not be established because <b>both</b> Department slugs
    /// read "one per cent" while the Department's own chart says 0.1; §194K's slug serves the version omitted in
    /// 1999; §192A and §194EE have "is less than" boundaries this engine's strictly-greater test cannot express
    /// without under-deducting on the boundary rupee. The next seeding pass will be tempted to add them from the
    /// rate chart, which is exactly how the cleartax/disytax citations got in. Removing an entry from
    /// <see cref="SeedTdsTcsRates.LongTailSectionsNotSeeded"/> is the only way past this test, and doing so is a
    /// claim that the gate was cleared against a primary source.</para>
    /// </summary>
    [Fact]
    public void No_section_on_the_rejected_list_has_been_quietly_seeded()
    {
        var rejected = SeedTdsTcsRates.LongTailSectionsNotSeeded;
        Assert.NotEmpty(rejected);

        var seeded = SeedTdsTcsRates.BuildTdsDefaults()
            .Select(n => n.SectionCode.Replace("-", string.Empty).Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var offenders = rejected.Keys
            .Where(k => seeded.Contains(k.Replace("-", string.Empty)))
            .ToList();

        Assert.True(offenders.Count == 0,
            "🔴 A section recorded as EVALUATED AND NOT SEEDED is now in the seed: " +
            string.Join(", ", offenders) + ".\n\n" +
            "Each entry on SeedTdsTcsRates.LongTailSectionsNotSeeded names the gate the section failed and the " +
            "evidence for it. If the gate has genuinely been cleared — a correctly-vintaged primary source " +
            "found for the rate, or the engine taught the inclusive 'is less than' boundary — then DELETE the " +
            "entry in the same change that seeds the row, and say in the row's comment what closed it. Seeding " +
            "the section while leaving the entry in place means the two now contradict each other, and the " +
            "reader has no way to know which is current.");
    }

    /// <summary>Every rejection has to state a reason long enough to be checkable against the statute, and has
    /// to name one of the four gates — otherwise the list decays into a bare list of section numbers that tells
    /// the next author nothing about what to go and do.</summary>
    [Fact]
    public void Every_rejection_names_its_gate_and_states_a_checkable_reason()
    {
        Assert.All(SeedTdsTcsRates.LongTailSectionsNotSeeded, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Value));
            Assert.True(entry.Value.Length >= 80,
                $"Rejection '{entry.Key}' must state WHY, in enough words to be checked against a source.");
            Assert.True(
                entry.Value.Contains("G1", StringComparison.Ordinal)
                || entry.Value.Contains("G2", StringComparison.Ordinal)
                || entry.Value.Contains("G3", StringComparison.Ordinal)
                || entry.Value.Contains("G4", StringComparison.Ordinal),
                $"Rejection '{entry.Key}' must name the gate it failed (G1 rate not in the Act · G2 Act and " +
                "chart disagree · G3 inclusive 'is less than' boundary · G4 deductor outside this product's " +
                "user population). A reason that names no gate cannot be re-checked.");
        });
    }

    /// <summary>🔴 The list must not be allowed to swallow the work: at least one of the three sections this
    /// slice DID ship must be absent from it. A rejection list containing everything would make the guard above
    /// vacuously green forever.</summary>
    [Fact]
    public void The_rejection_list_does_not_cover_the_sections_that_shipped()
    {
        foreach (var shipped in new[] { "194T", "194R", "194S" })
            Assert.False(SeedTdsTcsRates.LongTailSectionsNotSeeded.ContainsKey(shipped),
                $"§{shipped} was shipped by this slice; it cannot also be recorded as rejected.");
    }

    // =====================================================================================================

    private static void BookOne(
        Company c, Domain.Ledger party, NatureOfPayment nature, DateOnly on, Money gross)
    {
        var svc = new TdsService(c);
        var carve = svc.BuildCarveOut(gross, gross, nature, party, on);
        var expense = new Domain.Ledger(
            Guid.NewGuid(), $"Expense-{Guid.NewGuid():N}", c.FindGroupByName("Indirect Expenses")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(expense);

        var lines = new List<EntryLine> { new(expense.Id, gross, DrCr.Debit), carve.PartyLine };
        if (carve.TdsPayableLine is { } payable) lines.Add(payable);

        var journalTypeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), journalTypeId, on, lines));
    }
}
