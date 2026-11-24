using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// T2-181 (wave 13, track ic, review fix) — <see cref="NatureOfPaymentRules.VouchersMovedBy"/> must count exactly the
/// live vouchers whose next alteration the re-carve would refuse BECAUSE OF the master alteration. The re-carve
/// (<c>VoucherEntryViewModel.ApplyReCarve</c>) hands the engine the voucher's own posted rate, assessable and TDS (the
/// §194C rate grandfathering and the §194-I outcome grandfathering), so the count has to hand in the same three facts
/// or it disagrees with the refusal in BOTH directions. The oracle here is that same engine call, made the way
/// <c>ApplyReCarve</c> makes it.
/// </summary>
public class NatureOfPaymentMovedByGrandfatheringTests
{
    private static readonly DateOnly Fy = new(2025, 4, 1);

    private static Domain.Ledger AddLedger(Company c, string name, string group, bool debit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(group)!.Id, Money.Zero, debit);
        c.AddLedger(l);
        return l;
    }

    private static (Voucher Voucher, TdsLineTax Tax) Post(
        Company c, NatureOfPayment nature, Domain.Ledger party, Domain.Ledger expense, decimal gross, DateOnly on)
    {
        var g = Money.FromRupees(gross);
        var carve = new TdsService(c).BuildCarveOut(g, g, nature, party, on);
        var lines = new List<EntryLine> { new(expense.Id, g, DrCr.Debit), carve.PartyLine };
        if (carve.TdsPayableLine is { } p) lines.Add(p);
        var v = new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id, on, lines));
        return (v, v.Lines.Select(l => l.Tds).OfType<TdsLineTax>().Single());
    }

    /// <summary>The re-carve's verdict on an unchanged-base alteration of <paramref name="v"/> under
    /// <paramref name="nature"/>: refused when the recomputed rate or TDS differs from the posted one.</summary>
    private static bool ReCarveRefuses(Company c, NatureOfPayment nature, Domain.Ledger party, Voucher v, TdsLineTax t)
    {
        var w = new TdsService(c).ComputeWithholding(t.AssessableValue, nature, party, v.Date, asPostedBefore: v.Id,
            postedRateBasisPoints: t.RateBasisPoints, postedAssessableValue: t.AssessableValue,
            postedTdsAmount: t.TdsAmount);
        return w.RateBasisPoints != t.RateBasisPoints || w.TdsAmount != t.TdsAmount;
    }

    /// <summary>
    /// 🔴 <b>THE HIDE DIRECTION.</b> A §194C voucher posted at 1% whose deductee was later re-typed to a company is
    /// grandfathered: its re-carve still resolves 1% and it is alterable. Altering §194C's with-PAN rate to 1.5% breaks
    /// the grandfathering (the posted 1% no longer equals the master's individual rate), so the re-carve now resolves
    /// the company's 2% and refuses the voucher. The warning must count it.
    /// <para>Mutation: dropping the three posted-carrier arguments in <c>VouchersMovedBy</c> → the count is 0 (red).</para>
    /// </summary>
    [Fact]
    public void A_grandfathered_194C_voucher_the_rate_alteration_makes_unalterable_is_counted()
    {
        var c = CompanyFactory.CreateSeeded("Grandfathered 194C Co", Fy);
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = "MUMA12345B" });
        var c194 = c.FindNatureOfPaymentByCode("194C")!;
        var expense = AddLedger(c, "Contract Expense", "Indirect Expenses", true);
        var party = AddLedger(c, "Contractor", "Sundry Creditors", false);
        party.TdsApplicable = true; party.TdsNatureOfPaymentId = c194.Id;
        party.DeducteeType = DeducteeType.Individual; party.PartyPan = "AAPFU0939F";
        var (v, t) = Post(c, c194, party, expense, 50_000m, new DateOnly(2025, 6, 10));
        Assert.Equal(100, t.RateBasisPoints);
        party.DeducteeType = DeducteeType.Company;          // re-typed after posting: the grandfathered case
        Assert.False(ReCarveRefuses(c, c194, party, v, t));  // alterable before the master alteration

        var raised = c194.WithTerms(c194.SectionCode, c194.Name, 150, c194.RateWithoutPanBp, c194.FvuSectionCode,
            c194.SingleTransactionThreshold, c194.CumulativeThreshold);
        Assert.True(ReCarveRefuses(c, raised, party, v, t)); // refused after it

        Assert.Equal(new[] { v.Id }, NatureOfPaymentRules.VouchersMovedBy(c, c194, raised).Select(x => x.Id));
    }

    /// <summary>
    /// 🔴 <b>THE OVER-STATE DIRECTION.</b> A §194-I(a) rent of ₹40,000 (under the ₹50,000 month) withheld nothing.
    /// Giving the nature a ₹10,000 single-transaction limb would charge a fresh ₹40,000 bill, but the posted voucher's
    /// re-carve is pinned to its posted outcome (base unchanged), so it stays alterable. The warning — which tells the
    /// operator that altering each counted voucher "is now refused" — must not count it.
    /// <para>Mutation: dropping the three posted-carrier arguments in <c>VouchersMovedBy</c> → the count is 1 (red).</para>
    /// </summary>
    [Fact]
    public void A_194I_voucher_whose_posted_outcome_is_pinned_is_not_counted()
    {
        var c = CompanyFactory.CreateSeeded("Pinned 194I Co", Fy);
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = "MUMA12345B" });
        var rentNature = c.FindNatureOfPaymentByCode("194I(a)")!;
        Assert.True(rentNature.ThresholdWindowIsPerMonth);
        var expense = AddLedger(c, "Rent", "Indirect Expenses", true);
        var landlord = AddLedger(c, "Landlord", "Sundry Creditors", false);
        landlord.TdsApplicable = true; landlord.TdsNatureOfPaymentId = rentNature.Id;
        landlord.DeducteeType = DeducteeType.Individual; landlord.PartyPan = "ABCPE1234F";
        var (v, t) = Post(c, rentNature, landlord, expense, 40_000m, new DateOnly(2025, 7, 10));
        Assert.Equal(Money.Zero, t.TdsAmount);

        var limb = rentNature.WithTerms(rentNature.SectionCode, rentNature.Name, rentNature.RateWithPanBp,
            rentNature.RateWithoutPanBp, rentNature.FvuSectionCode, Money.FromRupees(10_000m),
            rentNature.CumulativeThreshold);
        Assert.False(ReCarveRefuses(c, limb, landlord, v, t));

        Assert.Empty(NatureOfPaymentRules.VouchersMovedBy(c, rentNature, limb));
    }

    /// <summary>
    /// 🔴 <b>THE RESTORE DIRECTION</b> (wave 13 ic, independent verifier). A §194J(b) voucher posted at 10% is made
    /// unalterable by a 10% → 2% alteration (counted, correctly). Restoring 2% → 10% puts the master back on the
    /// voucher's posted figures: the re-carve reproduces them and the voucher is alterable again. Old-terms and
    /// new-terms still differ, so a count that compares only those two reports the voucher as "now refused" — the
    /// opposite of the truth. The count must compare the new-terms outcome with the POSTED figures, as the refusal does.
    /// <para>Mutation: drop the posted-figures test from <c>VouchersMovedBy</c> (old-vs-new alone) → the restore's
    /// count is 1 (red).</para>
    /// </summary>
    [Fact]
    public void Restoring_the_rate_a_voucher_was_posted_at_does_not_count_it()
    {
        var c = CompanyFactory.CreateSeeded("Restored 194J Co", Fy);
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = "MUMA12345B" });
        var fees = c.FindNatureOfPaymentByCode("194J(b)")!;
        var expense = AddLedger(c, "Fees", "Indirect Expenses", true);
        var consultant = AddLedger(c, "Consultant", "Sundry Creditors", false);
        consultant.TdsApplicable = true; consultant.TdsNatureOfPaymentId = fees.Id;
        consultant.DeducteeType = DeducteeType.Firm; consultant.PartyPan = "AAPFU0939F";
        var (v, t) = Post(c, fees, consultant, expense, 88_888.88m, new DateOnly(2025, 10, 1));
        Assert.Equal(1000, t.RateBasisPoints);
        Assert.Equal(8_889m, t.TdsAmount.Amount);

        var lowered = fees.WithTerms(fees.SectionCode, fees.Name, 200, fees.RateWithoutPanBp, fees.FvuSectionCode,
            fees.SingleTransactionThreshold, fees.CumulativeThreshold);
        Assert.True(ReCarveRefuses(c, lowered, consultant, v, t));
        Assert.Equal(new[] { v.Id }, NatureOfPaymentRules.VouchersMovedBy(c, fees, lowered).Select(x => x.Id));
        c.Tds!.ReplaceNatureOfPayment(lowered);

        var restored = lowered.WithTerms(lowered.SectionCode, lowered.Name, 1000, lowered.RateWithoutPanBp,
            lowered.FvuSectionCode, lowered.SingleTransactionThreshold, lowered.CumulativeThreshold);
        Assert.False(ReCarveRefuses(c, restored, consultant, v, t)); // alterable again
        Assert.Empty(NatureOfPaymentRules.VouchersMovedBy(c, lowered, restored));
    }
}
