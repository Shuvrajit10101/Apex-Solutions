using System.Collections.Generic;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>§194-I's threshold is a PER-MONTH limb, and until this file the engine tested an annualised financial-year
/// aggregate instead — under-deducting.</b>
///
/// <para>
/// Statute — Income-tax Act 1961 <b>§194-I, first proviso</b> as it stands for <b>FY 2025-26</b>, bare Act text as
/// published by the Income-tax Department (<c>https://www.incometaxindia.gov.in/w/section-194-i-19</c>): "no
/// deduction shall be made under this section, where the income by way of rent credited or paid <b>for a month or
/// part of a month</b> by such person to the account of, or to, the payee, <b>does not exceed fifty thousand
/// rupees</b>". §194-I carries <b>no annual-aggregate limb at all</b>. The rates are unchanged and agree:
/// §194-I(a) two per cent for plant, machinery or equipment; §194-I(b) ten per cent for land, building, furniture
/// or fittings.
/// </para>
///
/// <para>
/// 🔴 <b>THE CONSTRUCTED FAILURE, WITH THE LITERAL FIGURES.</b> The seed shipped a <c>CumulativeThreshold</c> of
/// <b>₹6,00,000 per FINANCIAL YEAR</b> on both §194-I rows — the monthly figure annualised (50,000 × 12) — and the
/// engine had only two threshold windows, per-transaction and per-financial-year. One month's rent of ₹60,000 with
/// nothing else in the year: the statute deducts, because ₹60,000 exceeds the monthly ₹50,000, and at §194-I(b)
/// that is <b>₹6,000.00</b>. Measured on the unfixed engine the withholding was <b>₹0.00</b> — because ₹60,000 is
/// nowhere near ₹6,00,000. An <b>under</b>-deduction, which the deductor answers for under §201 with interest
/// under §201(1A), and which is worse than an over-deduction because it is not recoverable from the department.
/// </para>
///
/// <para>
/// 🔴 <b>IT WAS A WRONG SHAPE, NOT A WRONG NUMBER</b>, which is why the fix is a third threshold window
/// (<see cref="NatureOfPayment.MonthlyThreshold"/>, chosen through
/// <see cref="NatureOfPayment.AggregateThreshold"/>) rather than a smaller seeded figure: dropping ₹6,00,000 to
/// ₹50,000 in the FY field would have <b>over</b>-deducted instead, on the eleventh ordinary ₹5,000 month.
/// </para>
///
/// <para>
/// 🔴 <b>GRANDFATHERING — the user's ruling, and it is NOT §194C's.</b> §194C's grandfathering absorbs a
/// <b>rate</b> disagreement; here the drift is in whether the threshold was <b>crossed at all</b>, so what is
/// pinned is the posted <b>outcome</b> — <see cref="TdsService.GrandfatheredLiability"/>, fed the posted voucher's
/// own stamped <see cref="TdsLineTax.AssessableValue"/> and <see cref="TdsLineTax.TdsAmount"/>, and never a date
/// check. The point of the ruling is that such a voucher stays <b>alterable</b>; the Desktop half of that proof is
/// <c>VoucherAlter194IGrandfatherTests</c>.
/// </para>
///
/// <para><b>Odd paise wherever a boundary does not forbid them</b> (house rule): a ±₹0.50 defect once survived six
/// round-number assertions.</para>
/// </summary>
public class Tds194IMonthlyThresholdTests
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";

    private static readonly DateOnly Fy = new(2025, 4, 1);
    private static readonly DateOnly Apr = new(2025, 4, 10);
    private static readonly DateOnly May = new(2025, 5, 10);

    /// <summary>The figure the seed used to ship as a §194-I <b>financial-year</b> threshold: the monthly ₹50,000
    /// annualised. Every book created before the per-month window still carries it, persisted.</summary>
    private static readonly Money LegacyAnnualised = Money.FromRupees(6_00_000m);

    private static Company NewRentCompany()
    {
        var c = CompanyFactory.CreateSeeded("Rent Co", Fy);
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan });
        return c;
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Domain.Ledger Landlord(Company c, string? pan = DeducteePan)
    {
        var l = AddLedger(c, $"Landlord-{Guid.NewGuid():N}", "Sundry Creditors", false);
        l.DeducteeType = DeducteeType.Individual;
        l.PartyPan = pan;
        return l;
    }

    private static NatureOfPayment Nature(Company c, string code) => c.FindNatureOfPaymentByCode(code)!;

    /// <summary>A §194-I(b) nature carrying the superseded annualised ₹6,00,000 in its stored FY field — the shape
    /// every book created before the per-month window has persisted. NOT added to the company (the section code is
    /// unique per company); it is handed to the engine directly, which is all the engine needs.</summary>
    private static NatureOfPayment LegacyRentNature() =>
        new(Guid.NewGuid(), "194I(b)", "Rent — land/building (legacy book)", 1000, 2000, "4IB",
            singleTransactionThreshold: null, cumulativeThreshold: LegacyAnnualised);

    private static Guid JournalTypeId(Company c) =>
        c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id;

    /// <summary>Posts one <c>Dr Rent / Cr landlord (+ Cr TDS Payable)</c> assessment at its GROSS through the real
    /// carve-out, and returns the carve and the voucher id. The payable leg rides along whenever the carve withheld,
    /// so the voucher balances.</summary>
    private static (TdsService.CarveOut Carve, Guid VoucherId) Book(
        Company c, Domain.Ledger rent, Domain.Ledger landlord, NatureOfPayment nature, DateOnly on, Money gross)
    {
        var svc = new TdsService(c);
        var carve = svc.BuildCarveOut(gross, gross, nature, landlord, on);
        var lines = new List<EntryLine> { new(rent.Id, gross, DrCr.Debit), carve.PartyLine };
        if (carve.TdsPayableLine is { } payable) lines.Add(payable);
        var id = Guid.NewGuid();
        new LedgerService(c).Post(new Voucher(id, JournalTypeId(c), on, lines));
        return (carve, id);
    }

    /// <summary>A company with a rent expense ledger and one PAN-holding landlord, ready to book against.</summary>
    private static (Company C, Domain.Ledger Rent, Domain.Ledger Party, NatureOfPayment Nop) Scene(
        string section = "194I(b)")
    {
        var c = NewRentCompany();
        var rent = AddLedger(c, "Office Rent", "Indirect Expenses", true);
        var party = Landlord(c);
        var nop = Nature(c, section);
        rent.TdsApplicable = true;
        rent.TdsNatureOfPaymentId = nop.Id;
        return (c, rent, party, nop);
    }

    // =================================================================================================
    //  1. The constructed failure
    // =================================================================================================

    /// <summary>
    /// 🔴 <b>THE CONSTRUCTED FAILURE, WITH THE LITERAL WRONG FIGURE.</b> One month's rent of ₹60,000 and nothing
    /// else in the year. §194-I's first proviso deducts, because ₹60,000 exceeds the monthly ₹50,000, and at
    /// §194-I(b)'s ten per cent that is <b>₹6,000.00</b>. The annualised ₹6,00,000 FY rule deducted <b>₹0.00</b>.
    /// </summary>
    [Fact]
    public void One_months_rent_of_sixty_thousand_is_deducted_where_the_annualised_rule_deducted_nothing()
    {
        var (c, _, party, nop) = Scene();

        var w = new TdsService(c).ComputeWithholding(Money.FromRupees(60_000m), nop, party, May);

        Assert.True(w.Applies);
        Assert.Equal(1000, w.RateBasisPoints);
        Assert.Equal(Money.FromRupees(6_000m), w.TdsAmount);
        Assert.NotEqual(Money.Zero, w.TdsAmount);                  // the literal pre-fix figure: ₹0.00
        // And the year's aggregate is nowhere near the ₹6,00,000 the annualised rule waited for — which is exactly
        // why that rule withheld nothing on a bill the statute taxes.
        Assert.Equal(Money.Zero, w.PriorCumulativeInFy);
        Assert.True(Money.FromRupees(60_000m) < LegacyAnnualised);
    }

    /// <summary>The §194-I(a) arm on the same month: two per cent of ₹60,000 = <b>₹1,200.00</b>.</summary>
    [Fact]
    public void The_plant_and_machinery_arm_crosses_the_same_month_at_two_percent()
    {
        var (c, _, party, nop) = Scene("194I(a)");

        var w = new TdsService(c).ComputeWithholding(Money.FromRupees(60_000m), nop, party, May);

        Assert.Equal(200, w.RateBasisPoints);
        Assert.Equal(Money.FromRupees(1_200m), w.TdsAmount);
    }

    /// <summary>
    /// End to end: the party is credited the NET of the monthly deduction and <c>net + TDS == gross</c> to the
    /// paisa. ₹60,000.30 of rent ⇒ ₹6,000.00 withheld (60,000.30 × 10% = 6,000.03, nearest rupee) and ₹54,000.30
    /// credited.
    /// </summary>
    [Fact]
    public void The_carve_out_credits_the_party_the_net_of_the_monthly_deduction()
    {
        var (c, _, party, nop) = Scene();
        var gross = new Money(60_000.30m);

        var carve = new TdsService(c).BuildCarveOut(gross, gross, nop, party, May);

        Assert.True(carve.Applies);
        Assert.Equal(new Money(6_000m), carve.TdsAmount);
        Assert.Equal(new Money(54_000.30m), carve.NetPartyAmount);
        Assert.Equal(gross, carve.NetPartyAmount + carve.TdsAmount);
        Assert.NotNull(carve.TdsPayableLine);
    }

    // =================================================================================================
    //  2. The boundaries the statute names
    // =================================================================================================

    /// <summary>
    /// <b>"DOES NOT EXCEED fifty thousand rupees" is strict.</b> A month at exactly ₹50,000 is not exceeded, so
    /// nothing is deducted — ₹0.00, and the party keeps the whole rent.
    /// </summary>
    [Fact]
    public void Exactly_fifty_thousand_in_a_month_is_not_exceeded_so_nothing_is_deducted()
    {
        var (c, _, party, nop) = Scene();

        var w = new TdsService(c).ComputeWithholding(Money.FromRupees(50_000m), nop, party, May);

        Assert.False(w.Applies);
        Assert.Equal(Money.Zero, w.TdsAmount);
    }

    /// <summary>
    /// 🔴 <b>One paisa over the limb crosses it, and the WHOLE rent bears the tax — §194-I is a qualifying gate,
    /// not an excess-only carve.</b> ₹50,000.01 ⇒ ten per cent of the whole ₹50,000.01 = 5,000.001, nearest rupee
    /// <b>₹5,000.00</b>. An excess-only reading (the §194Q shape) would have charged ten per cent of one paisa and
    /// withheld ₹0.00.
    /// </summary>
    [Theory]
    [InlineData("50000.01", 5_000)]
    [InlineData("50001", 5_000)]
    [InlineData("55000.55", 5_500)]
    public void A_month_above_the_limb_bears_the_tax_on_its_whole_value(string rent, decimal expectedTds)
    {
        var (c, _, party, nop) = Scene();
        var amount = new Money(decimal.Parse(rent, System.Globalization.CultureInfo.InvariantCulture));

        var w = new TdsService(c).ComputeWithholding(amount, nop, party, May);

        Assert.True(w.Applies);
        Assert.Equal(Money.FromRupees(expectedTds), w.TdsAmount);
        Assert.False(nop.ChargesOnlyExcessOverCumulativeThreshold);
    }

    /// <summary>
    /// <b>The month AGGREGATES, and the aggregate is tested with the same strict "exceeds".</b> Two rent bills in
    /// one month: ₹30,000.00 then ₹20,000.00 totals exactly ₹50,000 and is still not exceeded, so the second bill
    /// withholds ₹0.00; ₹30,000.00 then ₹20,000.01 exceeds it, and the second bill bears ten per cent of its own
    /// whole ₹20,000.01 = <b>₹2,000.00</b> (2,000.001, nearest rupee).
    /// </summary>
    [Theory]
    [InlineData("20000.00", false, 0)]
    [InlineData("20000.01", true, 2_000)]
    public void Two_bills_in_one_month_aggregate_to_the_limb(string second, bool applies, decimal expectedTds)
    {
        var (c, rent, party, nop) = Scene();
        var first = Book(c, rent, party, nop, new DateOnly(2025, 5, 3), Money.FromRupees(30_000m));
        Assert.False(first.Carve.Applies);

        var w = new TdsService(c).ComputeWithholding(
            new Money(decimal.Parse(second, System.Globalization.CultureInfo.InvariantCulture)), nop, party,
            new DateOnly(2025, 5, 20));

        Assert.Equal(applies, w.Applies);
        Assert.Equal(Money.FromRupees(expectedTds), w.TdsAmount);
    }

    /// <summary>
    /// 🔴 <b>"OR PART OF A MONTH" WIDENS THE LIMB — IT DOES NOT PRO-RATE IT.</b> Rent of ₹40,000.40 for eleven days
    /// of April (a tenancy that started on the 20th) is <b>not</b> liable, because ₹40,000.40 does not exceed
    /// ₹50,000. Pro-rating the limb to the part-month — 11/30 × 50,000 = ₹18,333.33 — would have made it liable and
    /// taken ₹4,000.00 the statute does not ask for.
    /// </summary>
    [Fact]
    public void A_part_month_is_tested_against_the_whole_fifty_thousand_and_is_never_pro_rated()
    {
        var (c, _, party, nop) = Scene();

        var w = new TdsService(c).ComputeWithholding(new Money(40_000.40m), nop, party, new DateOnly(2025, 4, 20));

        Assert.False(w.Applies);
        Assert.Equal(Money.Zero, w.TdsAmount);
        Assert.NotEqual(Money.FromRupees(4_000m), w.TdsAmount);    // the pro-rated reading's figure
    }

    /// <summary>
    /// And the other half of "or part of a month": a part-month does not <b>escape</b> the section either. Rent of
    /// ₹55,000.55 for the last week of April exceeds ₹50,000 and is liable — <b>₹5,500.00</b>.
    /// </summary>
    [Fact]
    public void A_part_month_above_the_limb_is_still_liable()
    {
        var (c, _, party, nop) = Scene();

        var w = new TdsService(c).ComputeWithholding(new Money(55_000.55m), nop, party, new DateOnly(2025, 4, 24));

        Assert.True(w.Applies);
        Assert.Equal(Money.FromRupees(5_500m), w.TdsAmount);
    }

    /// <summary>
    /// 🔴 <b>TWO PART-MONTHS IN ONE FINANCIAL YEAR ARE TWO WINDOWS, EACH WITH ITS OWN FULL ₹50,000.</b> ₹45,000.45
    /// for the last week of April and ₹45,000.45 for the first week of May: neither exceeds ₹50,000, so neither is
    /// liable, even though together they are ₹90,000.90. The projection proves which window is being read — the
    /// May bill sees ₹0.00 prior <i>in its month</i> and ₹45,000.45 prior <i>in the year</i>.
    /// </summary>
    [Fact]
    public void Two_part_months_in_one_financial_year_are_two_windows_each_with_a_full_allowance()
    {
        var (c, rent, party, nop) = Scene();
        var svc = new TdsService(c);
        var april = Book(c, rent, party, nop, new DateOnly(2025, 4, 24), new Money(45_000.45m));
        Assert.False(april.Carve.Applies);

        var mayOn = new DateOnly(2025, 5, 6);
        Assert.Equal(Money.Zero, svc.ProjectPriorInMonth(party.Id, nop.Id, mayOn));
        Assert.Equal(new Money(45_000.45m), svc.ProjectPriorCumulative(party.Id, nop.Id, mayOn));

        var w = svc.ComputeWithholding(new Money(45_000.45m), nop, party, mayOn);
        Assert.False(w.Applies);
        Assert.Equal(Money.Zero, w.TdsAmount);
    }

    /// <summary>
    /// <b>A month boundary INSIDE the financial year resets the window too</b> — so the window is a calendar month,
    /// not a rolling thirty days and not the year. ₹30,000.30 on 30-Apr and ₹30,000.30 on 1-May: one day apart, and
    /// neither is liable.
    /// </summary>
    [Fact]
    public void A_month_boundary_inside_the_financial_year_resets_the_window()
    {
        var (c, rent, party, nop) = Scene();
        var april = Book(c, rent, party, nop, new DateOnly(2025, 4, 30), new Money(30_000.30m));
        Assert.False(april.Carve.Applies);

        var w = new TdsService(c).ComputeWithholding(
            new Money(30_000.30m), nop, party, new DateOnly(2025, 5, 1));

        Assert.False(w.Applies);
        Assert.Equal(Money.Zero, w.TdsAmount);
    }

    /// <summary>
    /// 🔴 <b>A CALENDAR MONTH CANNOT STRADDLE AN INDIAN FINANCIAL-YEAR BOUNDARY, which is why the monthly window
    /// needs no year arithmetic at all.</b> The FY runs 1 April – 31 March, so both its ends are month boundaries.
    /// ₹30,000.30 on 31-Mar-2026 and ₹30,000.30 on 1-Apr-2026 are a different month <b>and</b> a different year,
    /// and either test alone separates them — neither bill is liable.
    /// </summary>
    [Fact]
    public void A_calendar_month_never_straddles_the_financial_year_boundary()
    {
        var (c, rent, party, nop) = Scene();
        var march = Book(c, rent, party, nop, new DateOnly(2026, 3, 31), new Money(30_000.30m));
        Assert.False(march.Carve.Applies);

        var svc = new TdsService(c);
        var aprilOn = new DateOnly(2026, 4, 1);
        Assert.Equal(Money.Zero, svc.ProjectPriorInMonth(party.Id, nop.Id, aprilOn));

        var w = svc.ComputeWithholding(new Money(30_000.30m), nop, party, aprilOn);
        Assert.False(w.Applies);
    }

    /// <summary>
    /// The same two bills placed <b>inside</b> one March — 1-Mar and 31-Mar, both in FY 2025-26 — do aggregate, and
    /// the second is liable: March totals ₹60,000.60, so the 31-Mar bill bears ten per cent of its own ₹30,000.30 =
    /// <b>₹3,000.00</b>. This is the control for the straddling case above: the dates are a month apart in both, and
    /// only the calendar month tells them apart.
    /// </summary>
    [Fact]
    public void Two_bills_inside_one_march_do_aggregate_and_the_second_is_liable()
    {
        var (c, rent, party, nop) = Scene();
        var early = Book(c, rent, party, nop, new DateOnly(2026, 3, 1), new Money(30_000.30m));
        Assert.False(early.Carve.Applies);

        var w = new TdsService(c).ComputeWithholding(
            new Money(30_000.30m), nop, party, new DateOnly(2026, 3, 31));

        Assert.True(w.Applies);
        Assert.Equal(Money.FromRupees(3_000m), w.TdsAmount);
    }

    // =================================================================================================
    //  3. There is no annual limb — and what became of the persisted ₹6,00,000
    // =================================================================================================

    /// <summary>
    /// 🔴 <b>A FULL YEAR, AND THE WHOLE SIZE OF THE UNDER-DEDUCTION.</b> Twelve months of ₹55,000.55 rent. Every
    /// month exceeds ₹50,000, so every month is liable at ten per cent: <b>₹5,500.00</b> each, <b>₹66,000.00</b>
    /// for the year. Under the annualised ₹6,00,000 FY rule the aggregate only crossed during the ELEVENTH month
    /// (11 × 55,000.55 = ₹6,05,506.05), so the first ten months withheld nothing and the year came to ₹11,000.00 —
    /// <b>₹55,000.00 of tax not deducted</b> from one ordinary tenancy.
    /// </summary>
    [Fact]
    public void Every_month_above_the_limb_is_liable_where_the_annualised_rule_waited_for_the_eleventh()
    {
        var (c, rent, party, nop) = Scene();
        var monthly = new Money(55_000.55m);
        var total = Money.Zero;

        foreach (var on in TwelveMonthsOf(Fy))
        {
            var (carve, _) = Book(c, rent, party, nop, on, monthly);
            Assert.True(carve.Applies);
            Assert.Equal(Money.FromRupees(5_500m), carve.TdsAmount);
            total += carve.TdsAmount;
        }

        Assert.Equal(Money.FromRupees(66_000m), total);
        Assert.NotEqual(Money.FromRupees(11_000m), total);         // the annualised rule's figure for the year
    }

    /// <summary>
    /// 🔴 <b>THE ₹6,00,000 IS NOT MERELY IGNORED — IT IS UNREACHABLE, AND THAT IS THE PROOF THAT NO SHIPPED FIGURE
    /// MOVES FOR A BOOK THAT CARRIES IT.</b> The most rent that can escape a ₹50,000-a-month limb in a year is
    /// twelve months of exactly ₹50,000 — precisely ₹6,00,000 — and "exceeds" is strict at both limbs, so a
    /// ₹6,00,000 annual test could never have fired on any book the monthly test lets through. Run over both a
    /// nature seeded today (no stored FY threshold) and one carrying the legacy ₹6,00,000: the same twelve months,
    /// the same ₹0.00, every month.
    /// </summary>
    [Fact]
    public void The_largest_year_that_escapes_the_monthly_limb_is_exactly_the_six_lakh_the_seed_used_to_ship()
    {
        foreach (var legacy in new[] { false, true })
        {
            var (c, rent, party, seeded) = Scene();
            var nop = legacy ? LegacyRentNature() : seeded;
            var year = Money.Zero;

            foreach (var on in TwelveMonthsOf(Fy))
            {
                var (carve, _) = Book(c, rent, party, nop, on, Money.FromRupees(50_000m));
                Assert.False(carve.Applies);
                Assert.Equal(Money.Zero, carve.TdsAmount);
                year += Money.FromRupees(50_000m);
            }

            // The year sat exactly ON the annualised limb and never crossed it — while no single month ever
            // exceeded ₹50,000 either. Both windows agree on ₹0.00, which is the whole claim.
            var svc = new TdsService(c);
            var yearEnd = new DateOnly(2026, 3, 31);
            Assert.Equal(LegacyAnnualised, year);
            Assert.Equal(LegacyAnnualised, svc.ProjectPriorCumulative(party.Id, nop.Id, yearEnd));
            Assert.Equal(Money.FromRupees(50_000m), svc.ProjectPriorInMonth(party.Id, nop.Id, yearEnd));
        }
    }

    /// <summary>
    /// 🔴 <b>A BOOK CARRYING THE PERSISTED ₹6,00,000 COMPUTES EXACTLY WHAT A BOOK SEEDED TODAY COMPUTES.</b> The
    /// stored field is still there and still readable — it is not migrated, not re-read as a monthly figure and not
    /// deleted — it is simply never consulted on a per-month nature. That is what lets the fix ship without a
    /// schema migration.
    /// </summary>
    [Theory]
    [InlineData("0.01")]
    [InlineData("49999.99")]
    [InlineData("50000.00")]
    [InlineData("50000.01")]
    [InlineData("60000.60")]
    [InlineData("600000.60")]
    [InlineData("700000.70")]
    public void A_book_carrying_the_legacy_annualised_figure_computes_identically_to_one_seeded_today(string rent)
    {
        var (c, _, party, seeded) = Scene();
        var legacy = LegacyRentNature();
        var amount = new Money(decimal.Parse(rent, System.Globalization.CultureInfo.InvariantCulture));
        var svc = new TdsService(c);

        var fresh = svc.ComputeWithholding(amount, seeded, party, May);
        var old = svc.ComputeWithholding(amount, legacy, party, May);

        Assert.Equal(fresh.Applies, old.Applies);
        Assert.Equal(fresh.RateBasisPoints, old.RateBasisPoints);
        Assert.Equal(fresh.TdsAmount, old.TdsAmount);

        // The stored figure is intact and readable; it is the WINDOW that no longer asks for it.
        Assert.Equal(LegacyAnnualised, legacy.CumulativeThreshold);
        Assert.Null(seeded.CumulativeThreshold);
        Assert.Equal(Money.FromRupees(50_000m), legacy.AggregateThreshold);
        Assert.Equal(Money.FromRupees(50_000m), seeded.AggregateThreshold);
    }

    // =================================================================================================
    //  4. Which sections get the window — and, just as important, which do not
    // =================================================================================================

    /// <summary>
    /// <b>Exactly the two §194-I rows in the seed carry a per-month window.</b> The same whole-set shape as
    /// <c>Tds194CDeducteeTypeTests.Exactly_one_seeded_nature_of_payment_branches_on_deductee_type_and_it_is_194C</c>,
    /// so a future seed row cannot quietly acquire it.
    /// </summary>
    [Fact]
    public void Exactly_the_two_194I_rows_in_the_seed_have_a_per_month_window()
    {
        var perMonth = Seed.SeedTdsTcsRates.BuildTdsDefaults()
            .Where(n => n.ThresholdWindowIsPerMonth).Select(n => n.SectionCode).ToList();

        Assert.Equal(new[] { "194I(a)", "194I(b)" }, perMonth);
        Assert.All(Seed.SeedTdsTcsRates.BuildTdsDefaults().Where(n => n.ThresholdWindowIsPerMonth),
            n =>
            {
                Assert.Equal(Money.FromRupees(50_000m), n.MonthlyThreshold);
                Assert.Null(n.CumulativeThreshold);                // the annualised figure is gone from the seed
                Assert.Null(n.SingleTransactionThreshold);         // §194-I has no per-transaction limb either
            });
    }

    /// <summary>
    /// 🔴 <b>THE NEIGHBOURING SECTIONS MUST NOT INHERIT THE RENT WINDOW, AND THEIR CODES ALL BEGIN "194I".</b>
    /// §194-IA is 1% on the purchase of immovable property gated on a ₹50-lakh <i>consideration</i>; §194-IB and
    /// §194-IC are different tests again. A <c>StartsWith("194I")</c> match would have handed §194-IA a
    /// ₹50,000-a-month rent threshold. Nothing outside §194-I itself gets one.
    /// </summary>
    [Theory]
    [InlineData("194IA")]
    [InlineData("194-IA")]
    [InlineData("194IB")]
    [InlineData("194-IB")]
    [InlineData("194IC")]
    [InlineData("194I(c)")]
    [InlineData("194J(a)")]
    [InlineData("194C")]
    [InlineData("194Q")]
    public void A_section_that_is_not_194I_never_gets_the_rent_window(string code)
    {
        var n = new NatureOfPayment(Guid.NewGuid(), code, "Something else", 100, 2000, "XX",
            cumulativeThreshold: Money.FromRupees(50_00_000m));

        Assert.Null(n.MonthlyThreshold);
        Assert.False(n.ThresholdWindowIsPerMonth);
        Assert.Equal(Money.FromRupees(50_00_000m), n.AggregateThreshold);   // its stored FY limb, untouched
    }

    /// <summary>
    /// The window is <b>derived from the persisted section code</b> — which is why it round-trips exactly and needs
    /// no column. A hand-authored master spelled with a hyphen, in lower case, or padded is the same section and
    /// gets the same window.
    /// </summary>
    [Theory]
    [InlineData("194I")]
    [InlineData("194-I")]
    [InlineData("194I(a)")]
    [InlineData("194i(b)")]
    [InlineData("194-I(B)")]
    [InlineData("  194I(a)  ")]
    public void The_window_is_derived_from_the_section_code_and_normalises_hyphens_and_case(string code)
    {
        var n = new NatureOfPayment(Guid.NewGuid(), code, "Rent, hand-authored", 1000, 2000, "4IB");

        Assert.True(n.ThresholdWindowIsPerMonth);
        Assert.Equal(Money.FromRupees(50_000m), n.MonthlyThreshold);
        Assert.Equal(Money.FromRupees(50_000m), n.AggregateThreshold);
    }

    /// <summary>
    /// <b>The sections that are not §194-I are byte-identical.</b> §194J(b)'s ₹50,000 FY aggregate still fires on
    /// the year, not the month: two ₹30,000.30 fees in DIFFERENT months still cross it, where §194-I's window would
    /// have reset in between.
    /// </summary>
    [Fact]
    public void A_financial_year_section_is_untouched_by_the_monthly_window()
    {
        var (c, rent, party, _) = Scene();
        var fees = Nature(c, "194J(b)");
        var first = Book(c, rent, party, fees, Apr, new Money(30_000.30m));
        Assert.False(first.Carve.Applies);

        var w = new TdsService(c).ComputeWithholding(new Money(30_000.30m), fees, party, May);

        Assert.True(w.Applies);
        Assert.Equal(Money.FromRupees(3_000m), w.TdsAmount);
    }

    // =================================================================================================
    //  5. The monthly projection respects the POSTING MOMENT, exactly as the FY one does
    // =================================================================================================

    /// <summary>
    /// The monthly projection counts a posted voucher by default and skips it at its own posting moment — the same
    /// contract <c>TdsCumulativeSelfExclusionTests</c> pins for the FY window, because both run the same loop.
    /// </summary>
    [Fact]
    public void The_monthly_projection_skips_a_voucher_at_its_own_posting_moment()
    {
        var (c, rent, party, nop) = Scene();
        var (_, id) = Book(c, rent, party, nop, May, new Money(30_000.30m));
        var svc = new TdsService(c);

        Assert.Equal(new Money(30_000.30m), svc.ProjectPriorInMonth(party.Id, nop.Id, May));
        Assert.Equal(Money.Zero, svc.ProjectPriorInMonth(party.Id, nop.Id, May, id));
    }

    /// <summary>
    /// 🔴 <b>The half an id-only exclusion would leave live, on the monthly window this time.</b> A sibling posted
    /// LATER and dated in the same month must not count as prior to a voucher being re-carved, or a narration edit
    /// on the FIRST rent bill of a month would acquire a withholding the posting never made. Measured: ₹30,000.30
    /// on 3-May (below the limb), then a second ₹30,000.30 on 20-May which correctly crosses and withholds
    /// ₹3,000.00. Re-carving the first at its posting moment keeps ₹0.00; re-carving it against the whole book
    /// takes ₹3,000.00 out of the party's credit.
    /// </summary>
    [Fact]
    public void A_sibling_posted_later_in_the_same_month_is_not_prior_at_this_vouchers_posting_moment()
    {
        var (c, rent, party, nop) = Scene();
        var gross = new Money(30_000.30m);
        var (first, firstId) = Book(c, rent, party, nop, new DateOnly(2025, 5, 3), gross);
        Assert.False(first.Applies);

        var (second, _) = Book(c, rent, party, nop, new DateOnly(2025, 5, 20), gross);
        Assert.True(second.Applies);
        Assert.Equal(Money.FromRupees(3_000m), second.TdsAmount);

        var svc = new TdsService(c);
        var guarded = svc.BuildCarveOut(
            gross, gross, nop, party, new DateOnly(2025, 5, 3), asPostedBefore: firstId);
        Assert.False(guarded.Applies);
        Assert.Equal(gross, guarded.NetPartyAmount);

        var unguarded = svc.BuildCarveOut(gross, gross, nop, party, new DateOnly(2025, 5, 3));
        Assert.True(unguarded.Applies);
        Assert.Equal(Money.FromRupees(3_000m), unguarded.TdsAmount);
    }

    // =================================================================================================
    //  6. Grandfathering — the posted OUTCOME, not a rate, and never a clock
    // =================================================================================================

    /// <summary>
    /// 🔴 <b>THE RULING.</b> A ₹60,000.60 rent bill posted under the annualised rule withheld <b>nothing</b>. Handed
    /// its own posted outcome explicitly, the engine re-resolves it to <b>₹0.00</b> — so the alteration path's
    /// "nothing moved but the answer did" refusal never fires and the voucher stays alterable. The same call
    /// without the posted outcome takes ₹6,000.00, which is what that refusal would have seen.
    /// </summary>
    [Fact]
    public void A_voucher_posted_under_the_annualised_rule_that_withheld_nothing_keeps_withholding_nothing()
    {
        var (c, _, party, nop) = Scene();
        var svc = new TdsService(c);
        var posted = new Money(60_000.60m);

        var ungrandfathered = svc.ComputeWithholding(posted, nop, party, May);
        Assert.True(ungrandfathered.Applies);
        Assert.Equal(Money.FromRupees(6_000m), ungrandfathered.TdsAmount);

        var grandfathered = svc.ComputeWithholding(
            posted, nop, party, May, postedAssessableValue: posted, postedTdsAmount: Money.Zero);

        Assert.False(grandfathered.Applies);
        Assert.Equal(Money.Zero, grandfathered.TdsAmount);
    }

    /// <summary>
    /// 🔴 <b>AND IT RUNS THE OTHER WAY, WHICH IS EXACTLY WHY A RATE PIN COULD NOT HAVE CARRIED IT.</b> Twelve
    /// ₹40,000 months crossed the annualised ₹6,00,000 partway through the year and withheld ₹4,000 there; under
    /// the statute no single ₹40,000.40 month exceeds ₹50,000 and nothing is due. Re-carving that posted voucher
    /// must not RESTATE a deduction already deposited and reported, so the posted outcome stands: ₹4,000.00, on a
    /// bill today's rule would leave alone.
    /// </summary>
    [Fact]
    public void A_voucher_posted_under_the_annualised_rule_that_did_withhold_keeps_withholding()
    {
        var (c, _, party, nop) = Scene();
        var svc = new TdsService(c);
        var posted = new Money(40_000.40m);

        Assert.False(svc.ComputeWithholding(posted, nop, party, May).Applies);      // today: not liable

        var grandfathered = svc.ComputeWithholding(
            posted, nop, party, May,
            postedAssessableValue: posted, postedTdsAmount: Money.FromRupees(4_000m));

        Assert.True(grandfathered.Applies);
        Assert.Equal(Money.FromRupees(4_000m), grandfathered.TdsAmount);
    }

    /// <summary>
    /// 🔴 <b>THE PIN RELEASES THE MOMENT THE OPERATOR AMENDS THE BASE, IN BOTH DIRECTIONS.</b> Grandfathering keeps
    /// a POSTED figure from being restated; it is not a licence for an AMENDED bill to keep answering for a
    /// different one. Amend the ₹60,000.60 that withheld nothing up to ₹70,000.70 and the statutory ₹7,000.00
    /// applies; amend the ₹40,000.40 that withheld ₹4,000 down to ₹30,000.30 and the statutory ₹0.00 applies.
    /// </summary>
    [Theory]
    [InlineData("60000.60", 0, "70000.70", 7_000)]
    [InlineData("40000.40", 4_000, "30000.30", 0)]
    public void Amending_the_base_releases_the_pin(
        string postedBase, decimal postedTds, string amendedBase, decimal expectedTds)
    {
        var (c, _, party, nop) = Scene();
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        var w = new TdsService(c).ComputeWithholding(
            new Money(decimal.Parse(amendedBase, inv)), nop, party, May,
            postedAssessableValue: new Money(decimal.Parse(postedBase, inv)),
            postedTdsAmount: Money.FromRupees(postedTds));

        Assert.Equal(Money.FromRupees(expectedTds), w.TdsAmount);
    }

    /// <summary>
    /// <b>Grandfathering never reaches a section whose window did not change.</b> §194J(b)'s ₹50,000 FY limb was
    /// never redefined, so a posted outcome handed in against it is ignored and the ordinary threshold test decides
    /// — a ₹1,00,000.10 fee still withholds ₹10,000.00 whatever the caller claims was posted.
    /// </summary>
    [Theory]
    [InlineData("194J(b)", 10_000)]
    [InlineData("194C", 1_000)]
    public void Grandfathering_never_reaches_a_section_whose_window_did_not_change(string code, decimal expectedTds)
    {
        var (c, _, party, _) = Scene();
        var nop = Nature(c, code);

        var w = new TdsService(c).ComputeWithholding(
            new Money(1_00_000.10m), nop, party, May,
            postedAssessableValue: new Money(1_00_000.10m), postedTdsAmount: Money.Zero);

        Assert.True(w.Applies);
        Assert.Equal(Money.FromRupees(expectedTds), w.TdsAmount);
    }

    /// <summary>
    /// <b>A fresh posting has no posted outcome, and half of one is not one.</b> Both facts must travel together —
    /// a base with no amount, or an amount with no base, cannot say whether the voucher withheld — so either alone
    /// falls through to the statutory answer, ₹6,000.00.
    /// </summary>
    [Fact]
    public void A_fresh_posting_and_a_half_supplied_pin_both_get_the_statutory_answer()
    {
        var (c, _, party, nop) = Scene();
        var svc = new TdsService(c);
        var rent = new Money(60_000.60m);
        var six = Money.FromRupees(6_000m);

        Assert.Equal(six, svc.ComputeWithholding(rent, nop, party, May).TdsAmount);
        Assert.Equal(six, svc.ComputeWithholding(
            rent, nop, party, May, postedAssessableValue: rent).TdsAmount);
        Assert.Equal(six, svc.ComputeWithholding(
            rent, nop, party, May, postedTdsAmount: Money.Zero).TdsAmount);
    }

    /// <summary>
    /// The carve-out honours the grandfathered outcome end to end: the party keeps the FULL gross, there is no
    /// TDS-Payable leg at all, and the assessment detail still rides the party line (with ₹0.00) so the projection
    /// and the "TDS Not Deducted" advisory still see the transaction.
    /// </summary>
    [Fact]
    public void The_carve_out_honours_a_grandfathered_zero_end_to_end()
    {
        var (c, _, party, nop) = Scene();
        var gross = new Money(60_000.60m);

        var carve = new TdsService(c).BuildCarveOut(
            gross, gross, nop, party, May,
            postedAssessableValue: gross, postedTdsAmount: Money.Zero);

        Assert.False(carve.Applies);
        Assert.Equal(Money.Zero, carve.TdsAmount);
        Assert.Equal(gross, carve.NetPartyAmount);
        Assert.Null(carve.TdsPayableLine);
        Assert.Equal(Money.Zero, carve.Detail.TdsAmount);
        Assert.Equal(gross, carve.Detail.AssessableValue);
    }

    /// <summary>
    /// And the grandfathered <b>withholding</b> end to end: ₹4,000.00 still carved out of a ₹40,000.40 credit that
    /// today's rule would leave whole, with <c>net + TDS == gross</c> to the paisa.
    /// </summary>
    [Fact]
    public void The_carve_out_honours_a_grandfathered_withholding_end_to_end()
    {
        var (c, _, party, nop) = Scene();
        var gross = new Money(40_000.40m);

        var carve = new TdsService(c).BuildCarveOut(
            gross, gross, nop, party, May,
            postedAssessableValue: gross, postedTdsAmount: Money.FromRupees(4_000m));

        Assert.True(carve.Applies);
        Assert.Equal(Money.FromRupees(4_000m), carve.TdsAmount);
        Assert.Equal(new Money(36_000.40m), carve.NetPartyAmount);
        Assert.Equal(gross, carve.NetPartyAmount + carve.TdsAmount);
    }

    // =================================================================================================
    //  7. The advisory report must state the window the engine tested
    // =================================================================================================

    /// <summary>
    /// 🔴 <b>R2 "TDS Not Deducted" must measure the shortfall from the SAME window the engine tested.</b> Two
    /// below-limb rent bills of ₹45,000.45, one in May and one in June. Each row's threshold is the monthly
    /// ₹50,000 and each shortfall is ₹4,999.55 — measured from that row's OWN month. Projected over the financial
    /// year the June row would have read ₹90,000.90 against ₹50,000 and reported a shortfall of ₹0.00, telling the
    /// operator that a payment which is genuinely not liable is on the point of being taxed.
    /// </summary>
    [Fact]
    public void The_not_deducted_advisory_measures_the_shortfall_from_the_month_not_the_year()
    {
        var (c, rent, party, nop) = Scene();
        Book(c, rent, party, nop, new DateOnly(2025, 5, 12), new Money(45_000.45m));
        Book(c, rent, party, nop, new DateOnly(2025, 6, 12), new Money(45_000.45m));

        var r2 = TdsNotDeductedReport.Build(c, new DateOnly(2025, 7, 31));

        Assert.Equal(2, r2.Rows.Count);
        Assert.All(r2.Rows, row =>
        {
            Assert.Equal("194I(b)", row.Section);
            Assert.Equal(Money.FromRupees(50_000m), row.Threshold);
            Assert.Equal(new Money(45_000.45m), row.AggregateInWindow);
            Assert.Equal(new Money(4_999.55m), row.Shortfall);
        });
    }

    /// <summary>Every 10th of the twelve months of the financial year starting <paramref name="fyStart"/>.</summary>
    private static IEnumerable<DateOnly> TwelveMonthsOf(DateOnly fyStart)
    {
        for (var i = 0; i < 12; i++)
        {
            var m = fyStart.AddMonths(i);
            yield return new DateOnly(m.Year, m.Month, 10);
        }
    }
}
