using System.Collections.Generic;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>Phase 10.11 S5c — a re-carve must make the threshold test the POSTING made, not a different one.</b>
///
/// <para>🔴 <b>The defect this closes, and neither the design nor the plan names it.</b>
/// <c>TdsService.ProjectPriorCumulative</c> is a pure projection over <c>Company.Vouchers</c>. At POSTING time the
/// voucher is not in that list yet, so the projection is exactly "everything before this transaction". At
/// RE-ACCEPT time — which is the whole of what an alteration does — the voucher IS in the list, carrying its own
/// <see cref="TdsLineTax"/>, so an unguarded re-carve reads the voucher's own assessable back as "prior" and adds
/// the amended current on top of it. Under §194J(b) (₹50,000 cumulative threshold, no single-transaction
/// threshold) that turns a correctly below-threshold ₹30,000 fee into 30,000 + 30,000 = 60,000 and ACQUIRES a
/// ₹3,000 withholding on an alteration that changed nothing but the narration.</para>
///
/// <para>🔴 <b>And the second half of it, which an earlier form of the fix left live.</b> That form removed only the
/// voucher's OWN id from the projection. But the loop selects by DATE, so a sibling posted LATER and dated on or
/// before the voucher still counted as "prior" although it was not in the book at posting — the reachable window
/// was "posted later, dated on or before", i.e. every same-day batch of entries and every back-dated correction.
/// The argument is therefore <c>asPostedBefore</c>: it takes the projection at the named voucher's POSTING MOMENT
/// (everything before it in list order, which <c>LedgerService.Replace</c> deliberately preserves), which
/// reproduces the posting-time set exactly with no schema change. It defaults to <c>null</c> so every pre-S5c
/// caller is byte-identical (ER-13).</para>
///
/// <para>Both halves are pinned below, and so is the property that stops the fix becoming a way to switch the
/// cumulative threshold off: a voucher posted BEFORE the one being re-carved still counts in full.</para>
/// </summary>
public class TdsCumulativeSelfExclusionTests
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";
    private static readonly DateOnly Fy = new(2025, 4, 1);
    private static readonly DateOnly D1 = new(2025, 5, 10);

    private static Company NewTdsCompany()
    {
        var c = CompanyFactory.CreateSeeded("Self-Cumulative Co", Fy);
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan });
        return c;
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Guid JournalTypeId(Company c) =>
        c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id;

    /// <summary>Posts a below-threshold §194J(b) assessment of <paramref name="amount"/> and returns its voucher id.</summary>
    private static (Company Company, TdsService Svc, Domain.Ledger Vendor, NatureOfPayment Nop, Guid VoucherId) Booked(
        decimal amount)
    {
        var c = NewTdsCompany();
        var fees = AddLedger(c, "Professional Fees", "Indirect Expenses", true);
        var vendor = AddLedger(c, "Acme Consultants", "Sundry Creditors", false);
        vendor.DeducteeType = DeducteeType.Firm;
        vendor.PartyPan = DeducteePan;
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var svc = new TdsService(c);

        var gross = new Money(amount);
        var carve = svc.BuildCarveOut(gross, gross, nop, vendor, D1);
        Assert.False(carve.Applies);           // below the ₹50,000 cumulative threshold
        Assert.Null(carve.TdsPayableLine);

        var id = PostAssessment(c, fees, carve, gross, D1);
        return (c, svc, vendor, nop, id);
    }

    /// <summary>Posts one <c>Dr Professional Fees / Cr party (+ Cr TDS Payable)</c> assessment at its GROSS and
    /// returns its voucher id. The payable leg rides along whenever the carve withheld, so the voucher balances.</summary>
    private static Guid PostAssessment(
        Company c, Domain.Ledger fees, TdsService.CarveOut carve, Money gross, DateOnly on)
    {
        var lines = new List<EntryLine> { new(fees.Id, gross, DrCr.Debit), carve.PartyLine };
        if (carve.TdsPayableLine is { } payable) lines.Add(payable);
        var id = Guid.NewGuid();
        new LedgerService(c).Post(new Voucher(id, JournalTypeId(c), on, lines));
        return id;
    }

    [Fact]
    public void The_projection_counts_a_posted_voucher_by_default_and_skips_it_at_its_own_posting_moment()
    {
        var (_, svc, vendor, nop, id) = Booked(30_000.30m);

        // The default (pre-S5c) behaviour is unchanged: the posted voucher IS prior.
        Assert.Equal(new Money(30_000.30m), svc.ProjectPriorCumulative(vendor.Id, nop.Id, D1));

        // Taken at its own posting moment it is not — which is what a re-carve of that very voucher needs.
        Assert.Equal(Money.Zero, svc.ProjectPriorCumulative(vendor.Id, nop.Id, D1, id));
    }

    /// <summary>
    /// 🔴 The consequence in figures. Re-carving the SAME ₹30,000.30 without the marker crosses the ₹50,000
    /// threshold on the voucher's own assessable and withholds ₹3,000; with it, the assessment stays below
    /// threshold and the party keeps the full gross.
    /// </summary>
    [Fact]
    public void Re_carving_without_the_posting_moment_acquires_a_withholding_the_posting_never_made()
    {
        var (_, svc, vendor, nop, id) = Booked(30_000.30m);
        var gross = new Money(30_000.30m);

        var unguarded = svc.BuildCarveOut(gross, gross, nop, vendor, D1);
        Assert.True(unguarded.Applies);                                  // 30,000.30 prior + 30,000.30 current
        Assert.Equal(new Money(3_000m), unguarded.TdsAmount);            // 10% of 30,000.30, nearest rupee
        Assert.Equal(new Money(27_000.30m), unguarded.NetPartyAmount);

        var guarded = svc.BuildCarveOut(gross, gross, nop, vendor, D1, asPostedBefore: id);
        Assert.False(guarded.Applies);
        Assert.Equal(Money.Zero, guarded.TdsAmount);
        Assert.Equal(gross, guarded.NetPartyAmount);
        Assert.Null(guarded.TdsPayableLine);
    }

    /// <summary>
    /// 🔴 <b>The half an id-only exclusion left live (finding L1-01).</b> The projection selects by DATE, so a
    /// sibling posted AFTER the voucher and dated on or before it would still be counted as "prior" — the shape of
    /// every same-day batch. At the voucher's POSTING MOMENT it is not counted, because it was not in the book.
    /// </summary>
    [Fact]
    public void A_sibling_posted_later_and_dated_the_same_day_is_not_prior_to_this_voucher()
    {
        var (c, svc, vendor, nop, first) = Booked(30_000.30m);
        var fees = c.FindLedgerByName("Professional Fees")!;

        // A SECOND same-dated assessment, posted AFTER the first. It correctly sees the first as prior and crosses.
        var secondGross = new Money(30_000.30m);
        var second = svc.BuildCarveOut(secondGross, secondGross, nop, vendor, D1);
        Assert.True(second.Applies);
        Assert.Equal(new Money(3_000m), second.TdsAmount);
        PostAssessment(c, fees, second, secondGross, D1);

        // Whole book: both assessments count.
        Assert.Equal(new Money(60_000.60m), svc.ProjectPriorCumulative(vendor.Id, nop.Id, D1));

        // 🔴 At the FIRST voucher's posting moment nothing is prior — the second was not in the book yet, even
        // though it is dated the same day. An id-only exclusion returned ₹30,000.30 here and a narration-only
        // alteration of the first voucher then ACQUIRED a ₹3,000 withholding.
        Assert.Equal(Money.Zero, svc.ProjectPriorCumulative(vendor.Id, nop.Id, D1, first));
        var reCarve = svc.BuildCarveOut(secondGross, secondGross, nop, vendor, D1, asPostedBefore: first);
        Assert.False(reCarve.Applies);
        Assert.Equal(Money.Zero, reCarve.TdsAmount);
        Assert.Equal(new Money(30_000.30m), reCarve.NetPartyAmount);
    }

    /// <summary>
    /// The marker is NOT a way to switch the cumulative threshold off: a voucher posted BEFORE the named one still
    /// counts in full, so a re-carve of the second assessment still crosses the threshold.
    /// </summary>
    [Fact]
    public void A_voucher_posted_earlier_still_counts_towards_the_named_vouchers_threshold()
    {
        var (c, svc, vendor, nop, first) = Booked(30_000.30m);
        var fees = c.FindLedgerByName("Professional Fees")!;

        var secondGross = new Money(15_000.15m);
        var second = svc.BuildCarveOut(secondGross, secondGross, nop, vendor, D1);
        Assert.False(second.Applies);                       // 30,000.30 + 15,000.15 = 45,000.45, still below
        var secondId = PostAssessment(c, fees, second, secondGross, D1);

        Assert.Equal(new Money(45_000.45m), svc.ProjectPriorCumulative(vendor.Id, nop.Id, D1));
        Assert.Equal(new Money(30_000.30m), svc.ProjectPriorCumulative(vendor.Id, nop.Id, D1, secondId));

        // And it is still a real threshold: amend the second assessment to ₹25,000.25 and the FY aggregate
        // 30,000.30 + 25,000.25 = 55,000.55 crosses ₹50,000, so the re-carve DOES withhold.
        var amended = new Money(25_000.25m);
        var reCarve = svc.BuildCarveOut(amended, amended, nop, vendor, D1, asPostedBefore: secondId);
        Assert.True(reCarve.Applies);
        Assert.Equal(new Money(2_500m), reCarve.TdsAmount);  // 10% of 25,000.25, nearest rupee
    }

    /// <summary>An id that is not in the book at all projects over the whole book — which is exactly what a voucher
    /// that has not been posted yet sees, so a fresh posting cannot be changed by passing one.</summary>
    [Fact]
    public void A_marker_that_is_not_in_the_book_projects_over_the_whole_book()
    {
        var (_, svc, vendor, nop, _) = Booked(30_000.30m);
        Assert.Equal(
            new Money(30_000.30m),
            svc.ProjectPriorCumulative(vendor.Id, nop.Id, D1, Guid.NewGuid()));
    }
}
