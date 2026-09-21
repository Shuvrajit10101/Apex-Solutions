using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>Census 6.9 — the GSTR-3B Table-3.1 and Table-4 figures AS THE FORM DEFINES THEM.</b>
///
/// <para>🔴 <b>What these tests exist to stop.</b> Before this file the GSTR-3B screen summed Table 3.1(a) alone
/// under the caption <i>"Total output tax"</i>, and Table 4(A)(5) alone under <i>"Total eligible ITC"</i>. Both
/// captions promise a figure the sum underneath them did not contain: the first omits the whole reverse-charge
/// liability in 3.1(d), the second omits 4(A)(2), 4(A)(3) and every ITC reversal in 4(B). An operator keying the
/// return off that screen under-declares tax and over-claims credit simultaneously. These are therefore
/// wrong-money assertions, not presentation ones, and each is written so that collapsing the formula back to the
/// old one-term version fails it.</para>
///
/// <para><b>Sources, retrieved by content, not asserted from memory.</b>
/// <list type="bullet">
///   <item><b>CBIC Circular No. 170/02/2022-GST</b> (<c>cbic-gst.gov.in/pdf/Circular-170-02-2022-GST.pdf</c>).
///     Para 4.3(D): the net ITC available <i>"will be calculated in Table 4 (C) which is as per the formula
///     (4A - [4B (1) + 4B (2)])"</i>. Its Annexure repeats the formula in the table's own formula column, on the
///     row captioned <i>"(C) Net ITC Available (A)-(B)"</i>, as <c>C=A1+A2+A3+A4+A5-B1-B2</c> — so <b>Table 4(D)
///     does not enter 4(C)</b>. Para 4.3(C), on a Table 4(B)(2) reversal: <i>"Such ITC may be reclaimed in Table
///     4(A)(5) on fulfilment of necessary conditions. Further, all such reclaimed ITC shall also be shown in
///     Table 4(D)(1)."</i> — the reclaim is reported twice, once inside 4(A)(5) and once for information in
///     4(D)(1), which is why <see cref="Reclaim_is_reported_inside_4A5_and_repeated_in_4D1_not_added_twice"/>
///     asserts it does NOT also move 4(C) a second time.</item>
///   <item><b>CGST Act 2017 §2(82) and §49(4)</b> (<c>cbic-gst.gov.in</c>, CGST-Act-Updated-30092020.pdf).
///     §2(82) defines output tax so that it <i>"excludes tax payable by him on reverse charge basis"</i>, and
///     §49(4) confines the electronic credit ledger to <i>"any payment towards output tax"</i>. The 3.1(d)
///     liability is consequently cash-only, so a total that silently folds it in with 3.1(a) would offer a
///     set-off the statute forbids — hence 3.1(a)+3.1(d) is a TOTAL TAX PAYABLE line and the screen keeps the
///     two discharge halves apart.</item>
/// </list></para>
///
/// <para><b>Why the fixture is a directly-constructed record rather than a posted book.</b> These are assertions
/// about a FORMULA, and a posted fixture would let a change in the posting engine move the expected numbers
/// underneath them. Every input here is an independent prime-ish figure, so any term dropped from a formula
/// changes the result — a formula that silently loses <c>+ RcmItcOtherCgst</c> cannot coincidentally still pass.
/// The posted-book counterpart lives in <c>Apex.Desktop.Tests/Gstr3bScreenTablesTests.cs</c>.</para>
/// </summary>
public sealed class Gstr3bTable4ArithmeticTests
{
    private static readonly DateOnly From = new(2025, 4, 1);
    private static readonly DateOnly To = new(2025, 4, 30);

    /// <summary>
    /// A return with every Table-3.1 and Table-4 bucket populated by a DISTINCT amount, so no formula can drop a
    /// term and still produce the expected total by coincidence.
    /// </summary>
    private static Gstr3b Populated() => new(
            From, To,
            TaxableOutwardValue: Money.FromRupees(500_000m),
            ExemptNilNonGstOutward: Money.FromRupees(11_000m),
            OutwardCgst: Money.FromRupees(13_000m),
            OutwardSgst: Money.FromRupees(13_000m),
            OutwardIgst: Money.FromRupees(17_000m),
            ItcCgst: Money.FromRupees(7_000m),
            ItcSgst: Money.FromRupees(7_000m),
            ItcIgst: Money.FromRupees(19_000m))
    {
        // 3.1(d) — inward supplies liable to reverse charge (the cash-only liability).
        RcmOutwardCgst = Money.FromRupees(2_300m),
        RcmOutwardSgst = Money.FromRupees(2_300m),
        RcmOutwardIgst = Money.FromRupees(3_100m),
        RcmOutwardCess = Money.FromRupees(410m),

        // 4(A)(2) import of services — IGST only, by construction.
        RcmItcImportIgst = Money.FromRupees(1_900m),

        // 4(A)(3) other reverse-charge credit.
        RcmItcOtherCgst = Money.FromRupees(530m),
        RcmItcOtherSgst = Money.FromRupees(530m),
        RcmItcOtherIgst = Money.FromRupees(770m),

        // 4(B)(1) absolute reversal (rules 38/42/43, §17(5)).
        ItcReversed4B1Cgst = Money.FromRupees(310m),
        ItcReversed4B1Sgst = Money.FromRupees(310m),
        ItcReversed4B1Igst = Money.FromRupees(430m),

        // 4(B)(2) reclaimable reversal (rule 37/37A).
        ItcReversed4B2Cgst = Money.FromRupees(170m),
        ItcReversed4B2Sgst = Money.FromRupees(170m),
        ItcReversed4B2Igst = Money.FromRupees(230m),

        // 4(D)(1) reclaim of an earlier 4(B)(2) reversal.
        ItcReclaimed4D1Cgst = Money.FromRupees(110m),
        ItcReclaimed4D1Sgst = Money.FromRupees(110m),
        ItcReclaimed4D1Igst = Money.FromRupees(190m),
    };

    // ------------------------------------------------------------------ Table 3.1(a) + 3.1(d)

    /// <summary>
    /// 🔴 <b>The headline wrong-money assertion.</b> The tax payable on this return is 3.1(a) PLUS 3.1(d). The old
    /// screen total was <see cref="Gstr3b.OutwardCgst"/> alone; if that one-term version is restored, every
    /// equality here fails by exactly the reverse-charge liability.
    /// </summary>
    [Fact]
    public void Total_tax_payable_is_31a_plus_31d_and_never_31a_alone()
    {
        var r = Populated();

        Assert.Equal(13_000m + 2_300m, r.OutwardAndRcmTaxCgst.Amount);
        Assert.Equal(13_000m + 2_300m, r.OutwardAndRcmTaxSgst.Amount);
        Assert.Equal(17_000m + 3_100m, r.OutwardAndRcmTaxIgst.Amount);

        // Stated the other way round so the test also fails if 3.1(d) is silently zeroed rather than dropped:
        // the total must differ from the bare 3.1(a) figure by precisely the 3.1(d) amount.
        Assert.Equal(r.RcmOutwardCgst.Amount, r.OutwardAndRcmTaxCgst.Amount - r.OutwardCgst.Amount);
        Assert.Equal(r.RcmOutwardIgst.Amount, r.OutwardAndRcmTaxIgst.Amount - r.OutwardIgst.Amount);
    }

    /// <summary>
    /// A book with no reverse charge at all must be byte-identical to the old behaviour (ER-13): the total
    /// collapses onto 3.1(a). Without this, the fix above could have been "add a constant" and nobody would see it.
    /// </summary>
    [Fact]
    public void With_no_reverse_charge_the_total_collapses_onto_31a()
    {
        var r = new Gstr3b(From, To,
            Money.FromRupees(100_000m), Money.Zero,
            Money.FromRupees(9_000m), Money.FromRupees(9_000m), Money.FromRupees(4_000m),
            Money.FromRupees(1_000m), Money.FromRupees(1_000m), Money.Zero);

        Assert.Equal(9_000m, r.OutwardAndRcmTaxCgst.Amount);
        Assert.Equal(9_000m, r.OutwardAndRcmTaxSgst.Amount);
        Assert.Equal(4_000m, r.OutwardAndRcmTaxIgst.Amount);
    }

    // ------------------------------------------------------------------ Table 4(A)(5) as reported

    /// <summary>
    /// Circular 170 para 4.3(C): a reclaimed reversal is reported <b>in 4(A)(5)</b> and <b>also</b> shown in
    /// 4(D)(1). This engine posts a reclaim as a Journal-base stat-adjustment, which <c>ReadSide</c> excludes from
    /// <see cref="Gstr3b.ItcCgst"/>, so 4(A)(5) as REPORTED is the engine's ITC plus the reclaim.
    /// </summary>
    [Fact]
    public void Table_4A5_as_reported_adds_the_reclaim_to_the_engines_input_credit()
    {
        var r = Populated();

        Assert.Equal(7_000m + 110m, r.ItcReportedAllOtherCgst.Amount);
        Assert.Equal(7_000m + 110m, r.ItcReportedAllOtherSgst.Amount);
        Assert.Equal(19_000m + 190m, r.ItcReportedAllOtherIgst.Amount);
    }

    // ------------------------------------------------------------------ Table 4(C) — the ECL figure

    /// <summary>
    /// 🔴 <b>Net ITC Available, the figure actually credited to the Electronic Credit Ledger.</b> Formula from the
    /// Annexure's own formula column: <c>C = A1 + A2 + A3 + A4 + A5 − B1 − B2</c>. A1 (import of goods) and A4
    /// (ISD) are structurally absent from this book, so the live terms are A2 (IGST only), A3, A5, B1 and B2. The
    /// old screen showed A5 alone under the caption "Total eligible ITC" — restoring that fails all three heads.
    /// </summary>
    [Fact]
    public void Net_itc_available_is_4A_minus_4B_per_head_with_A2_on_igst_only()
    {
        var r = Populated();

        // CGST: A3 530 + A5 (7,000 + 110 reclaim) − B1 310 − B2 170 = 7,160.
        Assert.Equal(530m + 7_110m - 310m - 170m, r.NetItcAvailableCgst.Amount);
        Assert.Equal(7_160m, r.NetItcAvailableCgst.Amount);

        Assert.Equal(530m + 7_110m - 310m - 170m, r.NetItcAvailableSgst.Amount);

        // IGST is the ONLY head carrying A2 (import of services is always IGST):
        //   A2 1,900 + A3 770 + A5 (19,000 + 190) − B1 430 − B2 230 = 21,200.
        Assert.Equal(1_900m + 770m + 19_190m - 430m - 230m, r.NetItcAvailableIgst.Amount);
        Assert.Equal(21_200m, r.NetItcAvailableIgst.Amount);
    }

    /// <summary>
    /// 🔴 <b>Import of services must NOT leak onto CGST or SGST.</b> 4(A)(2) is IGST by construction, so a head
    /// that picked it up would over-state the credit ledger. Driven by making the import figure large enough that
    /// leaking it would be unmistakable.
    /// </summary>
    [Fact]
    public void Import_of_services_credit_never_reaches_the_central_or_state_head()
    {
        var r = Populated() with { RcmItcImportIgst = Money.FromRupees(1_000_000m) };

        Assert.Equal(7_160m, r.NetItcAvailableCgst.Amount);   // unchanged by the million
        Assert.Equal(7_160m, r.NetItcAvailableSgst.Amount);
        Assert.Equal(1_000_000m + 770m + 19_190m - 430m - 230m, r.NetItcAvailableIgst.Amount);
    }

    /// <summary>
    /// 🔴 <b>A reversal must REDUCE the net credit, and by its own amount.</b> This is the half that costs real
    /// money the other way: a screen that omits 4(B) tells the operator they may claim credit they have already
    /// had to give back. Both reversal buckets are moved independently so a formula that subtracts only one of
    /// them fails.
    /// </summary>
    [Fact]
    public void Each_reversal_bucket_reduces_net_itc_by_its_own_amount()
    {
        var r = Populated();
        var baseline = r.NetItcAvailableCgst.Amount;

        var moreB1 = r with { ItcReversed4B1Cgst = Money.FromRupees(310m + 1_000m) };
        Assert.Equal(baseline - 1_000m, moreB1.NetItcAvailableCgst.Amount);

        var moreB2 = r with { ItcReversed4B2Cgst = Money.FromRupees(170m + 400m) };
        Assert.Equal(baseline - 400m, moreB2.NetItcAvailableCgst.Amount);
    }

    /// <summary>
    /// 🔴 <b>The reclaim is reported twice but counted once.</b> Para 4.3(C) puts a reclaim inside 4(A)(5) AND in
    /// 4(D)(1); the Annexure formula <c>C=A1+A2+A3+A4+A5-B1-B2</c> shows 4(D) does not enter 4(C). So raising the
    /// reclaim must move 4(C) by exactly that amount — once, through 4(A)(5) — and never by twice it.
    /// </summary>
    [Fact]
    public void Reclaim_is_reported_inside_4A5_and_repeated_in_4D1_not_added_twice()
    {
        var r = Populated();
        var baseline = r.NetItcAvailableCgst.Amount;

        var moreReclaim = r with { ItcReclaimed4D1Cgst = Money.FromRupees(110m + 900m) };

        Assert.Equal(baseline + 900m, moreReclaim.NetItcAvailableCgst.Amount);       // once
        Assert.NotEqual(baseline + 1_800m, moreReclaim.NetItcAvailableCgst.Amount);  // never twice
        Assert.Equal(7_000m + 1_010m, moreReclaim.ItcReportedAllOtherCgst.Amount);   // and it is visible in 4(A)(5)
    }

    /// <summary>
    /// The existing load-bearing members are NOT redefined by this slice: <see cref="Gstr3b.NetCgst"/> feeds the
    /// offline-JSON Table 6.1 writer and <see cref="Gstr3b.TotalItc"/> feeds <c>GstQrmp</c>. This pins their old
    /// meaning so a later reader cannot assume the new Table-4 arithmetic was pushed into them.
    /// </summary>
    [Fact]
    public void The_pre_existing_net_and_total_members_keep_their_old_meaning()
    {
        var r = Populated();

        Assert.Equal(13_000m - 7_000m, r.NetCgst.Amount);                  // outward − ITC, RCM and reversals absent
        Assert.Equal(7_000m + 7_000m + 19_000m, r.TotalItc.Amount);        // 4(A)(5) only, reclaim absent
        Assert.Equal(13_000m + 13_000m + 17_000m, r.TotalOutwardTax.Amount);
    }
}
