using System;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>Census row 6.26 — the Kerala Flood Cess engine, pinned against its own primary sources.</b>
///
/// <para>Every figure asserted here is quoted in <see cref="KeralaFloodCess"/>'s own class documentation with the
/// URL it was read from, so a reviewer can check the assertion against the source without leaving the repository.
/// The three that would move real money if they drifted are called out below, because they are not obvious and a
/// well-meaning simplification would break each of them silently:</para>
/// <list type="number">
///   <item>🔴 <b>The window is closed.</b> The levy ran 01-08-2019 → 31-07-2021 and a supply outside it bears
///     nothing. A rate constant that kept applying after its statute lapsed is the exact defect class this product
///     keeps finding, so the boundary days are pinned on both sides rather than the middle being spot-checked.</item>
///   <item>🔴 <b>A 5% supply bears NO flood cess.</b> The FAQ's 1% limb names Schedules II, III and IV of
///     S.R.O. 360/2017 — 12%, 18% and 28% — and Schedule I (5%) is in neither limb. "The cess is 1% on everything
///     taxable" is the natural wrong reading and it over-collects on the commonest slab in an Indian trading book.</item>
///   <item>🔴 <b>The base excludes CGST and SGST.</b> The FAQ's own worked example is reproduced to the paisa, so
///     computing the cess on the invoice total instead of the value of supply fails by an exact, named figure.</item>
/// </list>
/// </summary>
public class KeralaFloodCessTests
{
    // The GST integrated rates that map to each S.R.O. 360/2017 schedule, so the tests read as the statute does.
    private const int Gst0 = 0;
    private const int GstQuarterPercent = 25;   // Schedule VI (0.125% State tax)
    private const int Gst3 = 300;               // Schedule V  (1.5% State tax)  — gold, diamond etc.
    private const int Gst5 = 500;               // Schedule I  (2.5% State tax)
    private const int Gst12 = 1200;             // Schedule II (6% State tax)
    private const int Gst18 = 1800;             // Schedule III (9% State tax)
    private const int Gst28 = 2800;             // Schedule IV (14% State tax)

    private static readonly DateOnly DuringTheLevy = new(2020, 6, 15);

    // ─────────────────────────────────────────────────────────── the dated window (defect class T1-26)

    /// <summary>
    /// 🔴 <b>THE WINDOW LOCK.</b> S.R.O. 436/2019 commenced the levy on 1 August 2019 and the FAQ gives it "a period
    /// of two years", so it ran to 31 July 2021 inclusive. Both boundary days AND both days just outside are pinned:
    /// widening or narrowing the window by one day fails here, and so does deleting the window test altogether
    /// (which is what a bare rate constant would amount to).
    /// </summary>
    [Fact]
    public void Rate_is_100bp_on_the_first_day_and_zero_the_day_before_and_the_day_after_the_window()
    {
        Assert.Equal(0, KeralaFloodCess.RateBasisPointsOn(new DateOnly(2019, 7, 31), Gst18));   // day before
        Assert.Equal(100, KeralaFloodCess.RateBasisPointsOn(new DateOnly(2019, 8, 1), Gst18));  // commencement
        Assert.Equal(100, KeralaFloodCess.RateBasisPointsOn(new DateOnly(2021, 7, 31), Gst18)); // last day
        Assert.Equal(0, KeralaFloodCess.RateBasisPointsOn(new DateOnly(2021, 8, 1), Gst18));    // day after

        Assert.False(KeralaFloodCess.IsInForceOn(new DateOnly(2019, 7, 31)));
        Assert.True(KeralaFloodCess.IsInForceOn(KeralaFloodCess.Commencement));
        Assert.True(KeralaFloodCess.IsInForceOn(KeralaFloodCess.Cessation));
        Assert.False(KeralaFloodCess.IsInForceOn(new DateOnly(2021, 8, 1)));
    }

    /// <summary>
    /// 🔴 <b>A supply made TODAY bears nothing, by construction.</b> The levy lapsed five years before this build,
    /// so the ONLY correct answer for a current voucher is zero — and it must be zero because the date says so, not
    /// because no company happens to be in Kerala. This is the test that makes the shipped engine safe to leave in
    /// the product.
    /// </summary>
    [Fact]
    public void A_supply_made_after_the_lapse_bears_no_flood_cess_at_any_gst_rate()
    {
        var today = new DateOnly(2026, 9, 6);
        foreach (var rate in new[] { Gst3, Gst5, Gst12, Gst18, Gst28 })
        {
            Assert.Equal(0, KeralaFloodCess.RateBasisPointsOn(today, rate));
            Assert.Equal(0m, KeralaFloodCess.On(Money.FromRupees(1_00_000m), today, rate).Amount);
        }
    }

    // ─────────────────────────────────────────────────────────── the schedule limbs

    /// <summary>
    /// The 1% limb: FAQ Q5's "Schedule II, III &amp; IV of SRO.No.360/2017", which S.R.O. 360/2017's own headings
    /// give as 6%, 9% and 14% State tax — i.e. the 12%, 18% and 28% GST slabs.
    /// </summary>
    [Theory]
    [InlineData(Gst12)]
    [InlineData(Gst18)]
    [InlineData(Gst28)]
    public void Schedules_two_three_and_four_bear_the_one_percent_limb(int gstRateBp)
    {
        Assert.Equal(KfcRateLimb.Standard, KeralaFloodCess.LimbFor(gstRateBp));
        Assert.Equal(100, KeralaFloodCess.RateBasisPointsOn(DuringTheLevy, gstRateBp));
    }

    /// <summary>The 0.25% limb: FAQ Q5's "Fifth Schedule of SRO.No.360/2017 (gold, diamond etc.)", which that
    /// notification's heading gives as 1.5% State tax — the 3% GST slab.</summary>
    [Fact]
    public void The_fifth_schedule_gold_slab_bears_the_quarter_percent_limb()
    {
        Assert.Equal(KfcRateLimb.Reduced, KeralaFloodCess.LimbFor(Gst3));
        Assert.Equal(25, KeralaFloodCess.RateBasisPointsOn(DuringTheLevy, Gst3));
    }

    /// <summary>
    /// 🔴 <b>THE OVER-COLLECTION LOCK, AND THE FIGURE IT GUARDS.</b> Schedule I is 2.5% State tax = the <b>5% GST</b>
    /// slab, and FAQ Q5 names Schedules II, III and IV in the 1% limb and the Fifth in the 0.25% limb — <b>Schedule I
    /// is in neither</b>. Schedule VI (0.125% State tax = 0.25% GST) is likewise absent, and an exempt/nil supply is
    /// excluded outright by Q13.
    ///
    /// <para>On ₹10,00,000 of 5% turnover the wrong reading collects ₹10,000.00 of cess that was never levied. The
    /// limb is asserted by NAME as well as by rate, because <see cref="KfcRateLimb.NotLeviable"/> and a leviable
    /// supply that happens to compute zero are different facts and only the enum keeps them apart.</para>
    /// </summary>
    [Theory]
    [InlineData(Gst0)]
    [InlineData(GstQuarterPercent)]
    [InlineData(Gst5)]
    public void Slabs_outside_the_named_schedules_bear_no_flood_cess_at_all(int gstRateBp)
    {
        Assert.Equal(KfcRateLimb.NotLeviable, KeralaFloodCess.LimbFor(gstRateBp));
        Assert.Equal(0, KeralaFloodCess.RateBasisPointsOn(DuringTheLevy, gstRateBp));
        Assert.Equal(0m, KeralaFloodCess.On(Money.FromRupees(10_00_000m), DuringTheLevy, gstRateBp).Amount);
    }

    // ─────────────────────────────────────────────────────────── the base (FAQ Q10)

    /// <summary>
    /// 🔴 <b>THE FAQ'S OWN WORKED EXAMPLE, REPRODUCED TO THE PAISA.</b> [KFC-FAQ] Q10: "If the value of supply is
    /// Rs.100/- and tax rate of the commodity is 12% GST, the invoice to be raised as shown below: Value of supply –
    /// Rs.100/-; CGST - Rs.6/-; SGST - Rs.6/-; Cess - Rs.1/-; Total sales value - Rs.113/-."
    ///
    /// <para>The cess is <b>₹1.00</b>, which is 1% of the ₹100 value of supply and NOT 1% of the ₹112 GST-inclusive
    /// figure (that would be ₹1.12). Computing the cess on an invoice total instead of the taxable value fails this
    /// assertion by exactly ₹0.12 — the arithmetic proof of Q10's "The CGST and SGST collection shall not be included
    /// in the value of supply."</para>
    /// </summary>
    [Fact]
    public void The_cess_base_excludes_CGST_and_SGST_the_FAQ_worked_example()
    {
        var valueOfSupply = Money.FromRupees(100m);
        var cess = KeralaFloodCess.On(valueOfSupply, DuringTheLevy, Gst12);

        Assert.Equal(1.00m, cess.Amount);

        // The whole invoice the FAQ prints, assembled from the same figures: 100 + 6 + 6 + 1 = 113.
        var cgst = Money.FromRupees(6m);
        var sgst = Money.FromRupees(6m);
        Assert.Equal(113.00m, (valueOfSupply + cgst + sgst + cess).Amount);

        // And the wrong base, named, so the assertion above cannot be read as a coincidence.
        Assert.NotEqual(1.12m, cess.Amount);
    }

    /// <summary>The cess is snapped to the paisa away-from-zero, the same snap every other derived amount in this
    /// product uses. ₹1,234.56 at 0.25% is ₹3.0864 → ₹3.09.</summary>
    [Fact]
    public void The_cess_is_rounded_to_the_paisa_away_from_zero()
    {
        Assert.Equal(3.09m, KeralaFloodCess.On(Money.FromRupees(1_234.56m), DuringTheLevy, Gst3).Amount);
        Assert.Equal(3.0864m, KeralaFloodCess.CessBeforeRounding(Money.FromRupees(1_234.56m), DuringTheLevy, Gst3));
    }

    // ─────────────────────────────────────────────────────────── the applicability predicate

    /// <summary>An ordinary intra-Kerala sale to an unregistered consumer during the window — the case the levy
    /// exists for ([KFC-FAQ] Q19, "If a supply is made to an unregistered tax payer, Kerala Flood Cess is to be
    /// levied").</summary>
    private static KfcSupply LeviableBaseline() => new(
        SupplyDate: DuringTheLevy,
        SupplierIsRegisteredInKerala: true,
        IsInterStateSupply: false,
        RecipientIsRegisteredInKerala: false,
        RecipientSupplyIsInFurtheranceOfBusiness: false,
        SupplyIsTaxable: true,
        SupplierIsCompositionDealer: false);

    [Fact]
    public void An_intra_Kerala_B2C_supply_during_the_window_is_leviable()
        => Assert.True(KeralaFloodCess.IsLeviable(LeviableBaseline()));

    /// <summary>[KFC-FAQ] Q11 — "Kerala Flood Cess is applicable only for intra-state supply."</summary>
    [Fact]
    public void An_inter_state_supply_bears_no_flood_cess()
        => Assert.False(KeralaFloodCess.IsLeviable(LeviableBaseline() with { IsInterStateSupply = true }));

    /// <summary>[KFC-FAQ] Q12/Q21 — a Kerala-registered taxable person buying in furtherance of business is exempt.</summary>
    [Fact]
    public void A_supply_to_a_Kerala_registered_person_in_furtherance_of_business_bears_no_cess()
        => Assert.False(KeralaFloodCess.IsLeviable(LeviableBaseline() with
        {
            RecipientIsRegisteredInKerala = true,
            RecipientSupplyIsInFurtheranceOfBusiness = true,
        }));

    /// <summary>
    /// 🔴 <b>THE OTHER HALF OF Q12, WHICH THE ONE-LINE SUMMARY "B2B IS EXEMPT" LOSES.</b> [KFC-FAQ] Q12 makes the
    /// supply to a Kerala registrant leviable "if the supply is made <b>not</b> in furtherance of business", and Q18
    /// gives the Department's own example — a motor vehicle bought for the buyer's own use IS liable, "Since the
    /// supply is not in furtherance of business". A predicate that exempted every registered buyer would fail here.
    /// </summary>
    [Fact]
    public void A_supply_to_a_Kerala_registered_person_NOT_in_furtherance_of_business_is_leviable()
        => Assert.True(KeralaFloodCess.IsLeviable(LeviableBaseline() with
        {
            RecipientIsRegisteredInKerala = true,
            RecipientSupplyIsInFurtheranceOfBusiness = false,
        }));

    /// <summary>
    /// [KFC-FAQ] Q21 — "Exemption is eligible only for registered taxable person having GST registration in Kerala
    /// GST." A buyer registered in ANOTHER State does not get the exemption; the flag is Kerala-registration, not
    /// registration in general.
    /// </summary>
    [Fact]
    public void A_buyer_registered_outside_Kerala_does_not_get_the_business_exemption()
        => Assert.True(KeralaFloodCess.IsLeviable(LeviableBaseline() with
        {
            RecipientIsRegisteredInKerala = false,       // registered, but not in Kerala
            RecipientSupplyIsInFurtheranceOfBusiness = true,
        }));

    /// <summary>[KFC-FAQ] Q13 — exempted goods or services bear no cess.</summary>
    [Fact]
    public void An_exempt_supply_bears_no_flood_cess()
        => Assert.False(KeralaFloodCess.IsLeviable(LeviableBaseline() with { SupplyIsTaxable = false }));

    /// <summary>[KFC-FAQ] Q14 — "Composition tax payers are exempted from the levy of Kerala Flood Cess".</summary>
    [Fact]
    public void A_composition_supplier_bears_no_flood_cess()
        => Assert.False(KeralaFloodCess.IsLeviable(LeviableBaseline() with { SupplierIsCompositionDealer = true }));

    /// <summary>A supplier outside Kerala does not collect a Kerala State levy ([KFC-FAQ] Q11/Q21).</summary>
    [Fact]
    public void A_supplier_not_registered_in_Kerala_levies_no_flood_cess()
        => Assert.False(KeralaFloodCess.IsLeviable(LeviableBaseline() with { SupplierIsRegisteredInKerala = false }));

    /// <summary>🔴 The predicate carries the window too, so no caller can reach a levy by satisfying the other six
    /// conjuncts on a date after the lapse.</summary>
    [Fact]
    public void The_leviability_predicate_is_false_after_the_lapse_however_the_other_facts_read()
        => Assert.False(KeralaFloodCess.IsLeviable(LeviableBaseline() with { SupplyDate = new DateOnly(2026, 9, 6) }));
}
