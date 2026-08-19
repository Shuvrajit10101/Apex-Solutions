using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>Phase 10.11 S5c — a voucher must not be counted against its OWN cumulative-FY threshold when it is
/// re-carved.</b>
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
/// <para>The fix is an <c>excludingVoucherId</c> argument, defaulted to <c>null</c> so every pre-existing caller is
/// byte-identical (ER-13). Both halves are pinned below: the exclusion works, and it is scoped to ONE voucher so it
/// cannot become a way to switch the cumulative threshold off.</para>
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

        var id = Guid.NewGuid();
        new LedgerService(c).Post(new Voucher(id, JournalTypeId(c), D1,
            new[] { new EntryLine(fees.Id, gross, DrCr.Debit), carve.PartyLine }));

        return (c, svc, vendor, nop, id);
    }

    [Fact]
    public void The_projection_counts_a_posted_voucher_by_default_and_skips_the_excluded_one()
    {
        var (_, svc, vendor, nop, id) = Booked(30_000.30m);

        // The default (pre-S5c) behaviour is unchanged: the posted voucher IS prior.
        Assert.Equal(new Money(30_000.30m), svc.ProjectPriorCumulative(vendor.Id, nop.Id, D1));

        // Excluding it takes it back out — which is what a re-carve of that very voucher needs.
        Assert.Equal(Money.Zero, svc.ProjectPriorCumulative(vendor.Id, nop.Id, D1, id));
    }

    /// <summary>
    /// 🔴 The consequence in figures. Re-carving the SAME ₹30,000.30 without the exclusion crosses the ₹50,000
    /// threshold on the voucher's own assessable and withholds ₹3,000; with it, the assessment stays below
    /// threshold and the party keeps the full gross.
    /// </summary>
    [Fact]
    public void Re_carving_without_the_exclusion_acquires_a_withholding_the_posting_never_made()
    {
        var (_, svc, vendor, nop, id) = Booked(30_000.30m);
        var gross = new Money(30_000.30m);

        var unguarded = svc.BuildCarveOut(gross, gross, nop, vendor, D1);
        Assert.True(unguarded.Applies);                                  // 30,000.30 prior + 30,000.30 current
        Assert.Equal(new Money(3_000m), unguarded.TdsAmount);            // 10% of 30,000.30, nearest rupee
        Assert.Equal(new Money(27_000.30m), unguarded.NetPartyAmount);

        var guarded = svc.BuildCarveOut(gross, gross, nop, vendor, D1, excludingVoucherId: id);
        Assert.False(guarded.Applies);
        Assert.Equal(Money.Zero, guarded.TdsAmount);
        Assert.Equal(gross, guarded.NetPartyAmount);
        Assert.Null(guarded.TdsPayableLine);
    }

    /// <summary>
    /// The exclusion is scoped to ONE voucher: a second assessment in the same FY still sees the first, so the
    /// cumulative threshold is not disabled by the fix.
    /// </summary>
    [Fact]
    public void The_exclusion_removes_only_the_named_voucher_from_the_projection()
    {
        var (c, svc, vendor, nop, first) = Booked(30_000.30m);
        var fees = c.FindLedgerByName("Professional Fees")!;

        // A SECOND below-threshold assessment for the same party×nature in the same FY.
        var second = svc.BuildCarveOut(new Money(15_000.15m), new Money(15_000.15m), nop, vendor, D1);
        Assert.False(second.Applies);
        var secondId = Guid.NewGuid();
        new LedgerService(c).Post(new Voucher(secondId, JournalTypeId(c), D1,
            new[] { new EntryLine(fees.Id, new Money(15_000.15m), DrCr.Debit), second.PartyLine }));

        Assert.Equal(new Money(45_000.45m), svc.ProjectPriorCumulative(vendor.Id, nop.Id, D1));
        Assert.Equal(new Money(15_000.15m), svc.ProjectPriorCumulative(vendor.Id, nop.Id, D1, first));
        Assert.Equal(new Money(30_000.30m), svc.ProjectPriorCumulative(vendor.Id, nop.Id, D1, secondId));
    }
}
