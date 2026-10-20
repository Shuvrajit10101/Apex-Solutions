using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>GSTR-4 and GSTR-9A DROPPED THE REGISTRATION SCOPE WHEN BUILDING THEIR QUARTERLY CMP-08 LEGS, AND THE
/// RESULT WAS NOT A WRONG FIGURE — IT WAS A HARD FAILURE THAT MADE BOTH RETURNS UNAVAILABLE.</b>
///
/// <para><b>How it was found.</b> A sweep of every inner scope-carrying call site in the ten registration-aware
/// GST report files: <b>20 sites, 18 forwarded <c>registrationId</c>, 2 dropped it</b> — <c>Gstr4.cs:66</c> and
/// <c>Gstr4.cs:172</c>, both <c>Cmp08.Build(company, …)</c> with the scope omitted, each sitting directly beside
/// a sibling line that <i>did</i> forward it to <c>CompositionTaxService.ComputeForPeriod</c>. The census had
/// already named these two as known survivors of the unscoped-collection shape; this is their measurement and
/// their closure.</para>
///
/// <para><b>Why it is a crash and not a silent mis-figure.</b> <c>Cmp08.Build</c> with a null scope reaches
/// <c>GstReportSupport.PostedDirectionalVouchers</c>, whose <c>EnsureRegistrationScoped</c> <b>throws</b> for any
/// company holding more than one registration — deliberately, so no caller can consume a partially-correct
/// sequence. So on a multi-registration composition book, <c>Gstr4.Build</c> and <c>Gstr9a.Build</c> threw
/// <b>even when the caller correctly named a registration</b>: the annual return could not be produced at all.
/// On a single-registration book the throw never fires, which is why nothing caught it.</para>
///
/// <para><b>Source.</b> CGST §39 makes a return a per-registered-person document, and §24(viii) the separateness
/// of registrations; a composition dealer's quarterly statement is CMP-08 and the annual return GSTR-4 (Rule 62).
/// No new vendor claim is made here — this is the existing per-registration architecture applied to the two call
/// sites that had not adopted it.</para>
/// </summary>
public class Gstr4QuarterScopeTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly FyEnd = new(2025, 3, 31);

    private const string Karnataka = "29";
    private const string TamilNadu = "33";

    private static string GstinFor(string stateCode, string pan)
    {
        var body = stateCode + pan + "1Z";
        return body + Gstin.ComputeCheckDigit(body + "0");
    }

    /// <summary>A COMPOSITION company in Karnataka with a second-State registration in Tamil Nadu, and one
    /// ₹1,00,000 outward supply recorded under EACH.</summary>
    private static (Company Company, GstRegistration Branch) Build()
    {
        var c = CompanyFactory.CreateSeeded("Composition Branch Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = Karnataka,
            Gstin = GstinFor(Karnataka, "AAPFU0939F"),
            RegistrationType = GstRegistrationType.Composition,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Quarterly,
            CompositionSubType = CompositionSubType.Trader,
        });

        var branch = new GstRegistration(
            Guid.NewGuid(), "Tamil Nadu Registration", TamilNadu, GstinFor(TamilNadu, "AABCC1206D"),
            GstRegistrationType.Composition, FyStart);
        c.Gst!.AddRegistration(branch);
        c.Gst!.EnsureValid();

        var ledgers = new LedgerService(c);
        var sales = c.Ledgers.FirstOrDefault(l => l.Name == "Sales");
        if (sales is null)
        {
            sales = new Domain.Ledger(
                Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, false);
            c.AddLedger(sales);
        }
        // A Trader's CMP-08 turnover BASE is the TAXABLE turnover (CompositionSubType's own doc), so the supply
        // has to be a taxable one or both registrations would read 0.00 and the comparison would be vacuous.
        sales.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "9985", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };
        var debtor = new Domain.Ledger(
            Guid.NewGuid(), "Customer", c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true);
        c.AddLedger(debtor);

        var salesTypeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;

        void Supply(Guid? registrationId, DateOnly date)
        {
            var v = new Voucher(Guid.NewGuid(), salesTypeId, date, new List<EntryLine>
            {
                new(debtor.Id, Money.FromRupees(100_000m), DrCr.Debit),
                new(sales.Id, Money.FromRupees(100_000m), DrCr.Credit),
            }, partyId: debtor.Id)
            { GstRegistrationId = registrationId };
            ledgers.Post(v);
        }

        Supply(null, new DateOnly(2024, 5, 10));           // the PRIMARY's supply
        Supply(branch.Id, new DateOnly(2024, 5, 11));      // the BRANCH's supply

        return (c, branch);
    }

    /// <summary>
    /// 🔴 <b>THE REGRESSION TEST.</b> Before the fix both of these threw
    /// <see cref="InvalidOperationException"/> ("This company holds multiple GST registrations, so a GST return
    /// must name the registration it is filed for") from inside <c>Cmp08.Build</c> — despite the caller naming
    /// the registration — so a multi-registration composition dealer could not produce GSTR-4 at all.
    /// </summary>
    [Fact]
    public void Gstr4_builds_for_a_named_registration_on_a_multi_registration_composition_book()
    {
        var (c, branch) = Build();

        var primary = Gstr4.Build(c, FyStart, FyEnd, GstRegistration.PrimaryId);
        var branchReturn = Gstr4.Build(c, FyStart, FyEnd, branch.Id);

        Assert.True(primary.Applicable);
        Assert.True(branchReturn.Applicable);
        Assert.Equal(4, primary.Quarters.Count);
        Assert.Equal(4, branchReturn.Quarters.Count);
    }

    /// <summary>
    /// 🔴 <b>THE MONEY TEST.</b> Each registration's quarterly CMP-08 legs must carry ITS OWN turnover, not the
    /// whole book's. Both supplies are ₹1,00,000 and both fall in Q1, so an unscoped leg would report
    /// ₹2,00,000 of turnover on each of the two returns — the same supply filed twice.
    /// </summary>
    [Fact]
    public void Each_registrations_quarterly_cmp08_carries_only_its_own_turnover()
    {
        var (c, branch) = Build();

        var primaryQ1 = Gstr4.Build(c, FyStart, FyEnd, GstRegistration.PrimaryId).Quarters[0];
        var branchQ1 = Gstr4.Build(c, FyStart, FyEnd, branch.Id).Quarters[0];

        Assert.Equal(100_000m, primaryQ1.TurnoverBase.Amount);
        Assert.Equal(100_000m, branchQ1.TurnoverBase.Amount);
    }

    /// <summary>GSTR-9A's own quarterly loop had the identical defect at <c>Gstr4.cs:172</c>.</summary>
    [Fact]
    public void Gstr9a_builds_for_a_named_registration_and_sums_only_its_own_quarters()
    {
        var (c, branch) = Build();

        var primary = Gstr9a.Build(c, FyStart, FyEnd, GstRegistration.PrimaryId);
        var branchReturn = Gstr9a.Build(c, FyStart, FyEnd, branch.Id);

        Assert.True(primary.Applicable);
        Assert.True(branchReturn.Applicable);

        // One ₹1,00,000 supply each — neither return may carry the other's.
        Assert.Equal(100_000m, primary.TotalTurnover.Amount);
        Assert.Equal(100_000m, branchReturn.TotalTurnover.Amount);
    }

    /// <summary>
    /// ER-13: a SINGLE-registration composition book never reached the throw and must be completely unaffected
    /// by the fix — it is the shape every existing composition test uses.
    /// </summary>
    [Fact]
    public void A_single_registration_composition_book_is_unaffected()
    {
        var c = CompanyFactory.CreateSeeded("Composition Solo Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = Karnataka,
            Gstin = GstinFor(Karnataka, "AAPFU0939F"),
            RegistrationType = GstRegistrationType.Composition,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Quarterly,
            CompositionSubType = CompositionSubType.Trader,
        });

        var g4 = Gstr4.Build(c, FyStart, FyEnd);
        Assert.True(g4.Applicable);
        Assert.Equal(4, g4.Quarters.Count);
        Assert.True(Gstr9a.Build(c, FyStart, FyEnd).Applicable);
    }
}
