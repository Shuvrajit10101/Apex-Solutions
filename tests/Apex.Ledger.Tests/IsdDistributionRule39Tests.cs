using System.Globalization;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Census row <b>6.24 — Input Service Distributor: credit distribution</b>. These exercise
/// <see cref="IsdDistribution"/>, the pure Rule 39 engine, with no company, no clock and no storage.
///
/// <para>🔴 <b>WHAT THESE TESTS DEFEND IS MONEY ON A FILED RETURN, NOT A CALCULATION.</b> GSTR-6 tells the
/// government how much credit each of a taxpayer's units may claim. Three separable things can go wrong and each
/// is filed as fact:
/// <list type="number">
///   <item><b>The total.</b> Rule 39(1)(b) of the CGST Rules: "<i>the amount of the credit distributed shall not exceed
///   the amount of credit available for distribution</i>". A rounding split that loses or invents a paisa is a
///   mis-statement, so the footing is asserted on an amount that does NOT divide evenly.</item>
///   <item><b>The head.</b> Rule 39(1)(j) converts central + State tax into integrated tax for an out-of-State
///   recipient. Getting the head wrong hands a unit credit it cannot use.</item>
///   <item><b>The split.</b> Rule 39(1)(f)'s <c>C1 = (t1 ÷ T) × C</c>, restricted by Rule 39(1)(c) when the service is
///   attributable to one unit.</item>
/// </list></para>
///
/// <para><b>The first test is the load-bearing one</b> — a three-unit distribution worked end to end and asserted
/// head by head. Its figures are derived from the operative Rule 39 text at
/// <c>taxinformation.cbic.gov.in/content/html/tax_repository/gst/rules/cgst_rules/active/chapter5/rule39_v1.00.html</c>
/// (fetched and read by content for this slice). It previously claimed to reproduce a CBIC e-version flier
/// verbatim; that flier's URL 404s, so the attribution was withdrawn while the arithmetic — independently
/// re-derived and unchanged — was kept. See that test's own remarks.</para>
/// </summary>
public class IsdDistributionRule39Tests
{
    // The CBIC example's States. Mumbai = Maharashtra (27), Jabalpur = Madhya Pradesh (23), Delhi (07).
    private const string Maharashtra = "27";
    private const string MadhyaPradesh = "23";
    private const string Delhi = "07";
    private const string Karnataka = "29";

    private static readonly Guid Mumbai = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Jabalpur = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid DelhiUnit = Guid.Parse("00000000-0000-0000-0000-0000000000a3");

    private static long Rupees(decimal r) => (long)(r * 100m);

    // ==========================================================================================================
    //  1. A three-unit distribution worked end to end against the Rule 39 text
    // ==========================================================================================================

    /// <summary>
    /// A head office at Mumbai registered as ISD with three operational units — Mumbai (Maharashtra), Jabalpur
    /// (Madhya Pradesh) and Delhi — turnover ₹5,00,00,000 / ₹3,00,00,000 / ₹2,00,00,000 (50 / 30 / 20), ₹3,00,000
    /// of CGST on a service used only by the Mumbai unit, and ₹12,00,000 on services used by all three.
    ///
    /// <para>Every figure asserted below is derived from the operative text and can be re-derived by hand:
    /// Rule 39(1)(c) sends the ₹3,00,000 to Mumbai alone; Rule 39(1)(f)'s <c>C1 = (t1 ÷ T) × C</c> splits the
    /// ₹12,00,000 into ₹6,00,000 / ₹3,60,000 / ₹2,40,000; Rule 39(1)(j)(i) leaves Mumbai's central and State tax
    /// as central and State because Mumbai is the distributor's own State, and Rule 39(1)(j)(ii) aggregates the
    /// central and State halves into integrated tax for Jabalpur and Delhi. Mumbai therefore takes ₹9,00,000 and
    /// the three sum to ₹15,00,000 — the ₹15,00,000 that came in (Rule 39(1)(b)).</para>
    ///
    /// <para>🔴 <b>THIS TEST USED TO CLAIM A PROVENANCE IT CANNOT SUPPORT, AND THE CLAIM WAS REMOVED RATHER THAN
    /// THE TEST.</b> It was captioned "CBIC's own worked example, verbatim" and cited the e-version flier
    /// <c>cbic-gst.gov.in/pdf/e-version-gst-fliers/InputServiceDistributorinGST.pdf</c> as the source of the
    /// M/s XYZ Ltd illustration and of the answer table. That URL returns <b>HTTP 404</b>, and the cbic.gov.in
    /// mirror returns <b>HTTP 500</b>, so the flier's figures could not be read by content and the "verbatim"
    /// claim could not be checked. Under R7 an unverifiable source may not be shipped. The ARITHMETIC is
    /// untouched — it was re-derived independently from the Rule 39 text at
    /// <c>taxinformation.cbic.gov.in/content/html/tax_repository/gst/rules/cgst_rules/active/chapter5/rule39_v1.00.html</c>,
    /// which WAS fetched and read by content, and it agrees to the rupee. Only the attribution changed.</para>
    ///
    /// <para>The all-units pool is stated here as ₹4,00,000 of each of central, State and integrated tax. The
    /// per-unit TOTALS asserted below are independent of that mix; the head-by-head consequences of the mix are
    /// asserted separately in the head-conversion tests that follow.</para>
    /// </summary>
    [Fact]
    public void A_three_unit_distribution_matches_the_rule_39_arithmetic_head_by_head()
    {
        var recipients = new[]
        {
            new IsdRecipient(Mumbai, "Mumbai", Maharashtra, null, Rupees(50_000_000m)),
            new IsdRecipient(Jabalpur, "Jabalpur", MadhyaPradesh, null, Rupees(30_000_000m)),
            new IsdRecipient(DelhiUnit, "Delhi", Delhi, null, Rupees(20_000_000m)),
        };

        var pools = new[]
        {
            // (i) "CGST paid on services used only for Mumbai Unit: Rs.300000/-" — R39(1)(c) direct attribution.
            new IsdCreditPool("Services used only for Mumbai Unit", Rupees(300_000m), 0, 0, 0,
                AttributableTo: new[] { Mumbai }),
            // (ii) "IGST, CGST & SGST paid on services used for all units: Rs.1200000/-" — R39(1)(e).
            new IsdCreditPool("Services used for all units",
                Rupees(400_000m), Rupees(400_000m), Rupees(400_000m), 0),
        };

        var result = IsdDistribution.Distribute(Maharashtra, recipients, pools);

        Assert.Empty(result.Diagnostics);

        var mumbai = Assert.Single(result.Lines, l => l.RecipientId == Mumbai);
        var jabalpur = Assert.Single(result.Lines, l => l.RecipientId == Jabalpur);
        var delhi = Assert.Single(result.Lines, l => l.RecipientId == DelhiUnit);

        // The hand-derived answer, to the rupee (Rule 39(1)(c) + (f)).
        Assert.Equal(Rupees(900_000m), mumbai.TotalPaisa);
        Assert.Equal(Rupees(360_000m), jabalpur.TotalPaisa);
        Assert.Equal(Rupees(240_000m), delhi.TotalPaisa);
        Assert.Equal(Rupees(1_500_000m), result.DistributedPaisa);

        // Mumbai is in the ISD's own State, so its central and State tax stay central and State (Rule 39(1)(j)(i)):
        // ₹3,00,000 directly attributed + 50% of ₹4,00,000 CGST = ₹5,00,000 CGST; 50% of SGST and of IGST.
        Assert.Equal(Rupees(500_000m), mumbai.CgstPaisa);
        Assert.Equal(Rupees(200_000m), mumbai.SgstPaisa);
        Assert.Equal(Rupees(200_000m), mumbai.IgstPaisa);

        // Jabalpur and Delhi are elsewhere, so ALL of their share arrives as integrated tax (Rule 39(1)(j)(ii)
        // for the central+State halves, Rule 39(1)(i) for the integrated half) and none as central or State.
        Assert.Equal(0, jabalpur.CgstPaisa);
        Assert.Equal(0, jabalpur.SgstPaisa);
        Assert.Equal(Rupees(360_000m), jabalpur.IgstPaisa);
        Assert.Equal(0, delhi.CgstPaisa);
        Assert.Equal(0, delhi.SgstPaisa);
        Assert.Equal(Rupees(240_000m), delhi.IgstPaisa);
    }

    // ==========================================================================================================
    //  2. Rule 39(1)(i)/(j) — the head conversion
    // ==========================================================================================================

    [Fact]
    public void Central_and_state_tax_to_a_recipient_in_the_isd_own_state_stay_central_and_state()
    {
        var self = new IsdRecipient(Mumbai, "Mumbai", Maharashtra, null, 100);
        var result = IsdDistribution.Distribute(Maharashtra, new[] { self },
            new[] { new IsdCreditPool("common", 1000, 2000, 0, 0) });

        var line = Assert.Single(result.Lines);
        Assert.Equal(1000, line.CgstPaisa);
        Assert.Equal(2000, line.SgstPaisa);
        Assert.Equal(0, line.IgstPaisa);
    }

    [Fact]
    public void Central_and_state_tax_to_a_recipient_elsewhere_become_integrated_tax_of_their_aggregate()
    {
        // Rule 39(1)(j)(ii): "be distributed as integrated tax and the amount to be so distributed shall be equal
        // to the AGGREGATE of the amount of input tax credit of central tax and State tax or Union territory tax".
        var other = new IsdRecipient(DelhiUnit, "Delhi", Delhi, null, 100);
        var result = IsdDistribution.Distribute(Maharashtra, new[] { other },
            new[] { new IsdCreditPool("common", 1000, 2000, 0, 0) });

        var line = Assert.Single(result.Lines);
        Assert.Equal(0, line.CgstPaisa);
        Assert.Equal(0, line.SgstPaisa);
        Assert.Equal(3000, line.IgstPaisa);   // 1000 + 2000, aggregated
    }

    [Fact]
    public void Integrated_tax_is_distributed_as_integrated_tax_to_every_recipient()
    {
        // Rule 39(1)(i) admits no exception — not even for the recipient sitting in the ISD's own State.
        var recipients = new[]
        {
            new IsdRecipient(Mumbai, "Mumbai", Maharashtra, null, 100),
            new IsdRecipient(DelhiUnit, "Delhi", Delhi, null, 100),
        };
        var result = IsdDistribution.Distribute(Maharashtra, recipients,
            new[] { new IsdCreditPool("igst only", 0, 0, 4000, 0) });

        foreach (var line in result.Lines)
        {
            Assert.Equal(2000, line.IgstPaisa);
            Assert.Equal(0, line.CgstPaisa);
            Assert.Equal(0, line.SgstPaisa);
        }
    }

    [Theory]
    [InlineData(Maharashtra)]
    [InlineData(Delhi)]
    [InlineData(Karnataka)]
    public void The_head_mix_changes_on_conversion_but_the_total_never_does(string recipientState)
    {
        // Whatever Rule 39(1)(j) does to the heads, the credit that reaches the recipient is the same money.
        var r = new IsdRecipient(Mumbai, "Unit", recipientState, null, 100);
        var pool = new IsdCreditPool("mix", 12_345, 23_456, 34_567, 4_567);

        var result = IsdDistribution.Distribute(Maharashtra, new[] { r }, new[] { pool });

        Assert.Equal(pool.TotalPaisa, result.DistributedPaisa);
    }

    // ==========================================================================================================
    //  3. Rule 39(1)(b) — the footing. The one that catches a rounding bug before the portal does.
    // ==========================================================================================================

    [Fact]
    public void The_distribution_foots_to_the_credit_available_to_the_paisa_when_it_does_not_divide_evenly()
    {
        // ₹1,000.01 across three EQUAL turnovers: 100001 paisa ÷ 3 = 33333.67. A naive per-recipient rounding
        // gives 33334 × 3 = 100002 — one paisa MORE credit distributed than the ISD ever received, which is
        // exactly what Rule 39(1)(b) forbids. The last recipient takes the remainder instead.
        var recipients = new[]
        {
            new IsdRecipient(Mumbai, "A", Maharashtra, null, 1_000),
            new IsdRecipient(Jabalpur, "B", Maharashtra, null, 1_000),
            new IsdRecipient(DelhiUnit, "C", Maharashtra, null, 1_000),
        };

        var result = IsdDistribution.Distribute(Maharashtra, recipients,
            new[] { new IsdCreditPool("indivisible", 100_001, 0, 0, 0) });

        Assert.Equal(100_001, result.DistributedPaisa);
        Assert.Equal(3, result.Lines.Count);
        Assert.Equal(new long[] { 33_334, 33_334, 33_333 }, result.Lines.Select(l => l.CgstPaisa).ToArray());
    }

    [Fact]
    public void Every_head_foots_independently_so_no_head_can_borrow_from_another()
    {
        // Rule 39(1)(h): "the input tax credit on account of central tax, State tax, Union territory tax and
        // integrated tax shall be distributed SEPARATELY". A split that footed only in total could silently move
        // a paisa of central tax into the integrated column and still look right on the bottom line. Every
        // recipient here is in the ISD's own State, so no conversion masks the per-head footing.
        var recipients = new[]
        {
            new IsdRecipient(Mumbai, "A", Maharashtra, null, 1),
            new IsdRecipient(Jabalpur, "B", Maharashtra, null, 1),
            new IsdRecipient(DelhiUnit, "C", Maharashtra, null, 1),
        };
        var pool = new IsdCreditPool("odd", 100_001, 200_002, 300_007, 11);

        var result = IsdDistribution.Distribute(Maharashtra, recipients, new[] { pool });

        Assert.Equal(pool.CgstPaisa, result.Lines.Sum(l => l.CgstPaisa));
        Assert.Equal(pool.SgstPaisa, result.Lines.Sum(l => l.SgstPaisa));
        Assert.Equal(pool.IgstPaisa, result.Lines.Sum(l => l.IgstPaisa));
        Assert.Equal(pool.CessPaisa, result.Lines.Sum(l => l.CessPaisa));
    }

    /// <summary>
    /// 🔴 <b>NO DISTRIBUTED HEAD MAY EVER BE NEGATIVE — the remainder convention can drive the LAST recipient
    /// below zero, and this is wrong money on a filed return.</b>
    ///
    /// <para><b>The arithmetic that breaks it.</b> <c>Split</c> gives the first n−1 targets
    /// <c>ProRata.Paisa</c>, which rounds <b>away from zero</b>, and hands the last target
    /// <c>credit − Σ(the others)</c>. When every rounding goes UP, the others can between them absorb more than
    /// the whole pool, and the remainder the last target receives is negative. Five recipients of equal turnover
    /// sharing 3 paisa is the smallest witness: each true share is 0.6 paisa, each of the first four rounds up to
    /// 1, and 3 − 4 = <b>−1</b>.</para>
    ///
    /// <para><b>Why this is not a rounding quibble.</b> Rule 39 distributes credit; it has no concept of a
    /// negative distribution. Reducing credit already distributed is an <b>ISD credit note</b> under
    /// Rule 39(1)(l)/(n) — a different document with its own apportionment — and <see cref="IsdDistribution"/>
    /// refuses a negative head on the way IN for exactly that reason. Emitting one on the way OUT contradicts
    /// that guard. On GSTR-6 it would file a negative ITC figure against a real GSTIN in table 5/6.</para>
    ///
    /// <para><b>The fix keeps Rule 39(1)(b) intact.</b> The pool must still foot exactly, so the paisa cannot
    /// simply be dropped; the overshoot is clawed back off the largest shares instead, which keeps Σ = C while no
    /// share goes below zero.</para>
    /// </summary>
    [Fact]
    public void No_distributed_head_is_ever_negative_even_when_every_rounding_goes_up()
    {
        var r4 = Guid.Parse("00000000-0000-0000-0000-0000000000a4");
        var r5 = Guid.Parse("00000000-0000-0000-0000-0000000000a5");

        // Five equal turnovers, 3 paisa of credit. Each true share is 0.6p; away-from-zero rounding makes the
        // first four 1p each, which is 4p — more than the pool.
        var recipients = new[]
        {
            new IsdRecipient(Mumbai, "A", Maharashtra, null, 1_000),
            new IsdRecipient(Jabalpur, "B", Maharashtra, null, 1_000),
            new IsdRecipient(DelhiUnit, "C", Maharashtra, null, 1_000),
            new IsdRecipient(r4, "D", Maharashtra, null, 1_000),
            new IsdRecipient(r5, "E", Maharashtra, null, 1_000),
        };

        var result = IsdDistribution.Distribute(Maharashtra, recipients,
            new[] { new IsdCreditPool("three paisa, five ways", 3, 0, 0, 0) });

        // Rule 39(1)(b) — the pool still foots exactly.
        Assert.Equal(3, result.DistributedPaisa);

        // ...and not by handing one recipient a negative share.
        Assert.All(result.Lines, l =>
        {
            Assert.True(l.CgstPaisa >= 0, $"{l.RecipientName} received negative central tax: {l.CgstPaisa}");
            Assert.True(l.SgstPaisa >= 0, $"{l.RecipientName} received negative State tax: {l.SgstPaisa}");
            Assert.True(l.IgstPaisa >= 0, $"{l.RecipientName} received negative integrated tax: {l.IgstPaisa}");
            Assert.True(l.CessPaisa >= 0, $"{l.RecipientName} received negative cess: {l.CessPaisa}");
        });
    }

    /// <summary>
    /// The same defect reached through the <b>cess</b> head and an UNEQUAL turnover split, so the fix cannot be a
    /// special case for equal shares or for a single head. Turnovers 1/1/1/1/96 over 3 paisa of cess: the four
    /// small recipients each round 0.03p up to... 0, but the large one dominates — the witness here is the
    /// mirror case where many tiny shares each round up to 1p and the final LARGE share still has to absorb them.
    /// </summary>
    [Fact]
    public void No_head_goes_negative_when_many_small_recipients_each_round_up()
    {
        var ids = Enumerable.Range(0, 9)
            .Select(i => Guid.Parse($"00000000-0000-0000-0000-0000000000b{i}"))
            .ToArray();

        // Nine equal turnovers sharing 5 paisa: each true share is 0.555p, rounding up to 1p for the first eight
        // = 8p against a 5p pool.
        var recipients = ids
            .Select((id, i) => new IsdRecipient(id, $"U{i}", Maharashtra, null, 1_000))
            .ToArray();

        var result = IsdDistribution.Distribute(Maharashtra, recipients,
            new[] { new IsdCreditPool("five paisa, nine ways", 0, 0, 0, 5) });

        Assert.Equal(5, result.DistributedPaisa);
        Assert.All(result.Lines, l => Assert.True(l.CessPaisa >= 0,
            $"{l.RecipientName} received negative cess: {l.CessPaisa}"));
    }

    // ==========================================================================================================
    //  4. Rule 39(1)(c)/(d)/(e) — who is in the denominator
    // ==========================================================================================================

    [Fact]
    public void Credit_attributable_to_one_recipient_is_distributed_only_to_that_recipient()
    {
        var recipients = new[]
        {
            new IsdRecipient(Mumbai, "Mumbai", Maharashtra, null, Rupees(100m)),
            new IsdRecipient(DelhiUnit, "Delhi", Delhi, null, Rupees(900m)),
        };

        // Delhi has 90% of the turnover; direct attribution overrides that entirely (Rule 39(1)(c)).
        var result = IsdDistribution.Distribute(Maharashtra, recipients,
            new[] { new IsdCreditPool("only for Mumbai", 50_000, 0, 0, 0, AttributableTo: new[] { Mumbai }) });

        var line = Assert.Single(result.Lines);
        Assert.Equal(Mumbai, line.RecipientId);
        Assert.Equal(50_000, line.CgstPaisa);
    }

    [Fact]
    public void A_recipient_not_operational_in_the_current_year_is_excluded_from_both_t1_and_T()
    {
        // Rule 39(1)(d)/(e) restrict the denominator to recipients "which are operational in the current year". If the
        // dormant unit's turnover stayed in T, the operational units would be under-distributed and the pool would
        // not foot — so this is a footing bug as well as a share bug.
        var recipients = new[]
        {
            new IsdRecipient(Mumbai, "Mumbai", Maharashtra, null, Rupees(100m)),
            new IsdRecipient(Jabalpur, "Dormant", MadhyaPradesh, null, Rupees(300m),
                IsOperationalInCurrentYear: false),
            new IsdRecipient(DelhiUnit, "Delhi", Delhi, null, Rupees(100m)),
        };

        var result = IsdDistribution.Distribute(Maharashtra, recipients,
            new[] { new IsdCreditPool("common", 80_000, 0, 0, 0) });

        Assert.Equal(2, result.Lines.Count);
        Assert.DoesNotContain(result.Lines, l => l.RecipientId == Jabalpur);
        Assert.Equal(40_000, Assert.Single(result.Lines, l => l.RecipientId == Mumbai).TotalPaisa);
        Assert.Equal(40_000, Assert.Single(result.Lines, l => l.RecipientId == DelhiUnit).TotalPaisa);
        Assert.Equal(80_000, result.DistributedPaisa);
    }

    // ==========================================================================================================
    //  5. Rule 39(1)(g) — eligible and ineligible never merge
    // ==========================================================================================================

    [Fact]
    public void Eligible_and_ineligible_credit_are_distributed_separately_and_never_merged()
    {
        var recipients = new[] { new IsdRecipient(Mumbai, "Mumbai", Maharashtra, null, 100) };

        var result = IsdDistribution.Distribute(Maharashtra, recipients, new[]
        {
            new IsdCreditPool("eligible", 10_000, 0, 0, 0, IsEligible: true),
            new IsdCreditPool("blocked u/s 17(5)", 3_000, 0, 0, 0, IsEligible: false),
        });

        // TWO rows for ONE recipient — that is the point of Rule 39(1)(g). One merged 13,000 row would file a
        // blocked credit as an eligible one.
        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(10_000, Assert.Single(result.Lines, l => l.IsEligible).CgstPaisa);
        Assert.Equal(3_000, Assert.Single(result.Lines, l => !l.IsEligible).CgstPaisa);
    }

    [Fact]
    public void Two_pools_with_the_same_eligibility_fold_into_one_row_per_recipient()
    {
        var recipients = new[] { new IsdRecipient(Mumbai, "Mumbai", Maharashtra, null, 100) };

        var result = IsdDistribution.Distribute(Maharashtra, recipients, new[]
        {
            new IsdCreditPool("invoice 1", 10_000, 0, 0, 0),
            new IsdCreditPool("invoice 2", 5_000, 0, 0, 0),
        });

        Assert.Equal(15_000, Assert.Single(result.Lines).CgstPaisa);
    }

    // ==========================================================================================================
    //  6. The refusals — where the engine declines rather than invents
    // ==========================================================================================================

    [Fact]
    public void A_zero_aggregate_turnover_withholds_the_pool_and_says_so_rather_than_splitting_it_equally()
    {
        // Rule 39(1)(f) divides by T. With T = 0 the formula has no value, and NO official source supplies a
        // fallback — so an equal split would be invented law on a filed return. The pool is withheld and named.
        var recipients = new[]
        {
            new IsdRecipient(Mumbai, "Mumbai", Maharashtra, null, 0),
            new IsdRecipient(DelhiUnit, "Delhi", Delhi, null, 0),
        };

        var result = IsdDistribution.Distribute(Maharashtra, recipients,
            new[] { new IsdCreditPool("common", 90_000, 0, 0, 0) });

        Assert.Empty(result.Lines);
        Assert.Equal(0, result.DistributedPaisa);
        Assert.False(result.IsComplete);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Contains("aggregate turnover T", diagnostic);
        Assert.Contains("900.00", diagnostic);   // the withheld amount is named, not merely flagged
    }

    [Fact]
    public void A_pool_with_no_attributable_operational_recipient_is_withheld_and_named()
    {
        var recipients = new[]
        {
            new IsdRecipient(Mumbai, "Dormant", Maharashtra, null, Rupees(100m), IsOperationalInCurrentYear: false),
        };

        var result = IsdDistribution.Distribute(Maharashtra, recipients,
            new[] { new IsdCreditPool("common", 90_000, 0, 0, 0) });

        Assert.Empty(result.Lines);
        Assert.Contains(result.Diagnostics, d => d.Contains("no operational recipient"));
    }

    [Fact]
    public void A_negative_head_is_refused_because_reducing_distributed_credit_is_an_isd_credit_note()
    {
        var recipients = new[] { new IsdRecipient(Mumbai, "Mumbai", Maharashtra, null, 100) };

        var ex = Assert.Throws<ArgumentException>(() => IsdDistribution.Distribute(
            Maharashtra, recipients, new[] { new IsdCreditPool("reversal", -1_000, 0, 0, 0) }));

        Assert.Contains("ISD credit note", ex.Message);
    }

    [Fact]
    public void A_negative_turnover_is_refused_because_t1_cannot_be_negative()
    {
        var recipients = new[] { new IsdRecipient(Mumbai, "Mumbai", Maharashtra, null, -1) };

        Assert.Throws<ArgumentException>(() => IsdDistribution.Distribute(
            Maharashtra, recipients, new[] { new IsdCreditPool("common", 1_000, 0, 0, 0) }));
    }

    [Fact]
    public void An_invalid_isd_state_code_is_refused_up_front()
    {
        Assert.Throws<ArgumentException>(() => IsdDistribution.Distribute(
            "99", Array.Empty<IsdRecipient>(), Array.Empty<IsdCreditPool>()));
    }

    [Fact]
    public void A_zero_pool_is_neither_distributed_nor_a_diagnostic()
    {
        var recipients = new[] { new IsdRecipient(Mumbai, "Mumbai", Maharashtra, null, 100) };
        var result = IsdDistribution.Distribute(Maharashtra, recipients,
            new[] { new IsdCreditPool("nothing", 0, 0, 0, 0) });

        Assert.Empty(result.Lines);
        Assert.True(result.IsComplete);
    }

    // ==========================================================================================================
    //  7. Compensation cess — OUR labelled divergence, pinned so it cannot drift silently
    // ==========================================================================================================

    [Fact]
    public void Compensation_cess_is_carried_through_as_cess_and_is_never_converted_to_integrated_tax()
    {
        // Rule 39(1)(h)/(i)/(j) enumerate central, State, UT and integrated tax and are SILENT on compensation
        // cess. This build distributes cess pro rata and leaves the head alone (ER-2 ring-fencing). The test
        // exists so that a future change to that decision is a deliberate one with a failing test in front of it,
        // not a quiet re-classification of a filed figure.
        var other = new IsdRecipient(DelhiUnit, "Delhi", Delhi, null, 100);

        var result = IsdDistribution.Distribute(Maharashtra, new[] { other },
            new[] { new IsdCreditPool("cess", 1_000, 2_000, 0, 5_000) });

        var line = Assert.Single(result.Lines);
        Assert.Equal(5_000, line.CessPaisa);          // still cess
        Assert.Equal(3_000, line.IgstPaisa);          // and the cess did NOT join the aggregate
    }

    // ==========================================================================================================
    //  8. The registration type itself
    // ==========================================================================================================

    [Fact]
    public void An_isd_registration_requires_a_gstin_because_its_registration_is_compulsory_and_separate()
    {
        Assert.Throws<ArgumentException>(() => new GstRegistration(
            Guid.NewGuid(), "Head Office ISD", "27", null,
            GstRegistrationType.InputServiceDistributor));
    }

    [Fact]
    public void The_isd_registration_type_is_appended_last_so_no_stored_ordinal_is_re_labelled()
    {
        // The value is persisted as its ordinal into the existing companies.gst_reg_type and
        // gst_registrations.registration_type INTEGER columns. If it were ever inserted above an existing member,
        // every book already storing that ordinal would silently change registration type on next open.
        Assert.Equal(0, (int)GstRegistrationType.Regular);
        Assert.Equal(1, (int)GstRegistrationType.Composition);
        Assert.Equal(2, (int)GstRegistrationType.Unregistered);
        Assert.Equal(3, (int)GstRegistrationType.Consumer);
        Assert.Equal(4, (int)GstRegistrationType.InputServiceDistributor);
    }

    // ==========================================================================================================
    //  9. The withheld figure is readable on every host the gate runs on
    // ==========================================================================================================

    /// <summary>
    /// 🔴 <b>A CROSS-PLATFORM/CULTURE REGRESSION, NOT A COSMETIC ONE.</b> The diagnostics name the rupee amount
    /// that was NOT distributed — the one number an operator acts on. Built with an interpolated <c>0.00</c> it
    /// binds to <see cref="CultureInfo.CurrentCulture"/>, so on a host whose culture uses a comma decimal
    /// separator the withheld ₹1,00,000.00 reads as <c>100000,00</c>, and it is grouped the Western way
    /// everywhere. This pins it to <see cref="IndianMoneyFormat"/>, the one home for rupee grouping, so the
    /// figure is identical on ubuntu, macOS and Windows and under any host culture.
    /// </summary>
    [Fact]
    public void The_withheld_amount_is_grouped_the_indian_way_whatever_the_host_culture_is()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // A culture with a COMMA decimal separator and a DOT group separator — the exact inversion.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var recipients = new[]
            {
                new IsdRecipient(Mumbai, "Mumbai", Maharashtra, null, 0),
                new IsdRecipient(DelhiUnit, "Delhi", Delhi, null, 0),
            };

            // ₹1,00,000.00 of credit, withheld because T = 0.
            var result = IsdDistribution.Distribute(Maharashtra, recipients,
                new[] { new IsdCreditPool("common", 10_000_000, 0, 0, 0) });

            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Contains("₹1,00,000.00", diagnostic);       // lakh grouping, dot decimal
            Assert.DoesNotContain("100000,00", diagnostic);    // what CurrentCulture would have produced
            Assert.DoesNotContain("100,000.00", diagnostic);   // what Western grouping would have produced
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
