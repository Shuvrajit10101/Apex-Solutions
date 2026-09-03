using System;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>W0-10 review (findings #1/#6/#8) — recovering the INTEGRATED rate from a leg that only carries HALF of it.</b>
///
/// <para><c>GstService.ComputeInvoiceTax</c> stamps a CGST/SGST leg with <c>halfBp = integratedBp / 2</c> using
/// <b>integer division</b>, so the odd basis point of an odd integrated rate is not in the posted data at all. Doubling
/// the half back — what every reader did — reads 25 bp (0.25%, rough diamonds) as 24, which made the printed breakup
/// row state a rate its own money contradicts, and merged a 25 bp group and a 24 bp group into one row.
/// <see cref="GstReportSupport.IntegratedRateOf"/> now discriminates the only two arithmetically possible candidates,
/// <c>2h</c> and <c>2h+1</c>, by asking which one the engine's own <c>ComputeLineTax</c> turns into the tax the leg
/// ACTUALLY carries.</para>
///
/// <para>These are unit tests on the shared reader itself, because five consumers share it — the printed breakup,
/// <c>InvoiceTaxableValue</c>, GSTR-1, the e-invoice INV-01 payload and the e-Way Part-A — so the document, the return
/// and both payloads move together or not at all. The end-to-end document assertions live in
/// <c>Apex.Desktop.Tests.ItemInvoicePostedTaxTests</c>.</para>
///
/// <para>Fixtures are odd-paisa (₹47,296.73 = 60.125 Nos @ ₹786.64) except where the point IS a small base.</para>
/// </summary>
public sealed class PostedIntegratedRateRecoveryTests
{
    private static readonly Money Supply = Money.FromRupees(47_296.73m);

    // ================================================================ 1 — the odd rate is recovered exactly

    /// <summary>
    /// 0.25% on ₹47,296.73 posts ₹118.24, split 59.12 / 59.12, on legs stamped <c>25 / 2 == 12</c>. Doubling reads 24;
    /// only 25 turns this base into this money.
    /// <para><b>Bite:</b> <c>return gst.RateBasisPoints * 2;</c> ⇒ 24.</para>
    /// </summary>
    [Fact]
    public void An_odd_rate_is_recovered_from_the_tax_the_leg_carries()
    {
        Assert.Equal(25, GstReportSupport.IntegratedRateOf(
            new GstLineTax(GstTaxHead.Central, 12, Supply), Money.FromRupees(59.12m)));
        Assert.Equal(25, GstReportSupport.IntegratedRateOf(
            new GstLineTax(GstTaxHead.State, 12, Supply), Money.FromRupees(59.12m)));
    }

    /// <summary>Its even neighbour must stay put: 0.24% on the same base posts ₹3.79 on ₹1,579.47, split 1.90 / 1.89,
    /// and the legs are stamped 12 as well — the collision that merged two rate groups into one row.</summary>
    [Fact]
    public void The_even_neighbour_at_the_same_stamped_half_stays_at_its_own_rate()
    {
        var basis = Money.FromRupees(1_579.47m);
        Assert.Equal(24, GstReportSupport.IntegratedRateOf(
            new GstLineTax(GstTaxHead.Central, 12, basis), Money.FromRupees(1.90m)));
        Assert.Equal(24, GstReportSupport.IntegratedRateOf(
            new GstLineTax(GstTaxHead.State, 12, basis), Money.FromRupees(1.89m)));
    }

    /// <summary>An ordinary 18% intra leg is untouched — the whole point of preferring the doubled half (ER-13).</summary>
    [Fact]
    public void An_ordinary_even_rate_is_byte_identical()
    {
        Assert.Equal(1800, GstReportSupport.IntegratedRateOf(
            new GstLineTax(GstTaxHead.Central, 900, Supply), Money.FromRupees(4_256.71m)));
        Assert.Equal(1800, GstReportSupport.IntegratedRateOf(
            new GstLineTax(GstTaxHead.State, 900, Supply), Money.FromRupees(4_256.70m)));
    }

    /// <summary>An IGST leg already carries the full integrated rate and is never doubled, odd or even.</summary>
    [Fact]
    public void An_integrated_leg_is_read_verbatim()
    {
        Assert.Equal(25, GstReportSupport.IntegratedRateOf(
            new GstLineTax(GstTaxHead.Integrated, 25, Supply), Money.FromRupees(118.24m)));
        Assert.Equal(1800, GstReportSupport.IntegratedRateOf(
            new GstLineTax(GstTaxHead.Integrated, 1800, Supply), Money.FromRupees(8_513.41m)));
    }

    // ================================================================ 2 — the two fallbacks, which are the safety

    /// <summary>
    /// <b>🔴 THE TIE-BREAK, and it was measurably unpinned.</b> On a small enough base BOTH candidates produce the same
    /// posted head — ₹20.00 at 1800 bp and at 1801 bp both give a total of ₹3.60 and a CGST of ₹1.80 — so the recovery
    /// is genuinely ambiguous. It must resolve to the EVEN candidate, because that is the historical answer and every
    /// reader's byte-identity (ER-13) rests on it; resolving to the odd one would print "18.01%" on an ordinary 18%
    /// supply. Measured while writing this: inverting the preference in <c>IntegratedRateOf</c> left every project that
    /// reads it green — Apex.Ledger.Tests 1437, Apex.Ledger.Io.Tests 384, Apex.Desktop.Tests 1997 — so nothing else in
    /// the suite constrains it. This test is the only thing that does.
    /// <para><b>Bite:</b> <c>return Reproduces(doubled + 1) ? doubled + 1 : doubled;</c> ⇒ 1801.</para>
    /// </summary>
    [Fact]
    public void When_both_candidates_explain_the_money_the_even_one_wins()
    {
        Assert.Equal(1800, GstReportSupport.IntegratedRateOf(
            new GstLineTax(GstTaxHead.Central, 900, Money.FromRupees(20m)), Money.FromRupees(1.80m)));
        // The same rule at the bottom of the scale: a zero-tax leg explains nothing, so it stays at the doubled half.
        Assert.Equal(0, GstReportSupport.IntegratedRateOf(
            new GstLineTax(GstTaxHead.Central, 0, Money.FromRupees(1m)), Money.Zero));
    }

    /// <summary>
    /// A leg whose posted amount matches NEITHER candidate — crafted or imported data, or a leg adjusted independently
    /// of its rate — falls back to the doubled half, exactly as before. The recovery may never invent a rate out of an
    /// amount it cannot explain.
    /// </summary>
    [Fact]
    public void A_leg_whose_money_explains_nothing_keeps_the_doubled_half()
    {
        Assert.Equal(1800, GstReportSupport.IntegratedRateOf(
            new GstLineTax(GstTaxHead.Central, 900, Supply), Money.FromRupees(1m)));
    }

    // ================================================================ 3 — end to end through the shared group read

    /// <summary>
    /// The two groups reach <see cref="GstReportSupport.ReadPostedRateGroups"/> — the read the printed breakup, the
    /// service-invoice footing conjuncts and (in its twin form) GSTR-1, the e-invoice and the e-Way Part-A all make —
    /// as TWO groups, each on its own base, not one merged group keyed 24 whose taxable is the max of the two.
    /// <para><b>Bite:</b> double the stamped half in <c>IntegratedRateOf</c> and this collapses to a single group with
    /// <c>Taxable == 47,296.73</c> and <c>Cgst == 61.02</c> — ₹1,579.47 of supply silently dropped from the breakup.</para>
    /// </summary>
    [Fact]
    public void Two_groups_one_basis_point_apart_read_back_as_two_groups()
    {
        var neighbour = Money.FromRupees(1_579.47m);
        var v = new Voucher(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2024, 4, 21), new[]
        {
            new EntryLine(Guid.NewGuid(), Money.FromRupees(48_998.23m), DrCr.Debit),
            new EntryLine(Guid.NewGuid(), Money.FromRupees(48_876.20m), DrCr.Credit),
            new EntryLine(Guid.NewGuid(), Money.FromRupees(59.12m), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.Central, 12, Supply)),
            new EntryLine(Guid.NewGuid(), Money.FromRupees(59.12m), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.State, 12, Supply)),
            new EntryLine(Guid.NewGuid(), Money.FromRupees(1.90m), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.Central, 12, neighbour)),
            new EntryLine(Guid.NewGuid(), Money.FromRupees(1.89m), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.State, 12, neighbour)),
        });

        var groups = GstReportSupport.ReadPostedRateGroups(v);
        Assert.Equal(2, groups.Count);
        Assert.Equal(24, groups[0].Rate);
        Assert.Equal(1_579.47m, groups[0].Taxable);
        Assert.Equal(1.90m, groups[0].Cgst);
        Assert.Equal(1.89m, groups[0].Sgst);
        Assert.Equal(25, groups[1].Rate);
        Assert.Equal(47_296.73m, groups[1].Taxable);
        Assert.Equal(59.12m, groups[1].Cgst);
        Assert.Equal(59.12m, groups[1].Sgst);

        // The two bases are distinct supplies, so the invoice taxable value is their SUM — merging them lost one.
        Assert.Equal(48_876.20m, GstReportSupport.InvoiceTaxableValue(v).Amount);
        Assert.Equal(122.03m, groups.Sum(g => g.Cgst + g.Sgst));   // 118.24 @0.25% + 3.79 @0.24%
    }
}
