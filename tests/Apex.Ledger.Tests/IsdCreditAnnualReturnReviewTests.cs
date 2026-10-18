using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>REVIEW FIXTURE (adversarial, written from OUTSIDE the branch's own tests).</b>
/// An ISD credit receipt enters the monthly GSTR-3B Table 4(A)(4) and Table 4(C), but <c>Gstr9.Build</c> sums
/// only 4(A)(5) + 4(A)(2)/(3) into Table 6 "ITC availed". So the credit disappears from the annual return — and
/// because GSTR-9C's books side (Σ Input-ledger debit legs) DOES see the receipt, 9C reports a spurious
/// unreconciled ITC difference of exactly the ISD credit.
/// </summary>
public class IsdCreditAnnualReturnReviewTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly FyEnd = new(2026, 3, 31);
    private static readonly DateOnly D = new(2025, 5, 15);

    private const long Cgst = 150_000L;   // Rs.1,500.00
    private const long Sgst = 150_000L;   // Rs.1,500.00
    private const long Igst = 200_000L;   // Rs.2,000.00
    private const decimal GstHeadsRupees = 5_000m;   // cess ring-fenced (ER-2)

    private static (Company Company, Domain.Ledger HeadOffice) Build()
    {
        var c = CompanyFactory.CreateSeeded("ISD Annual Review Co", FyStart);
        var gst = new GstService(c);
        var body = "29" + "AAPFU0939F" + "1Z";
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "29",
            Gstin = body + Gstin.ComputeCheckDigit(body + "0"),
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var group = c.Groups.First(g => g.Name == "Indirect Expenses");
        var ho = new Domain.Ledger(Guid.NewGuid(), "Head Office Account", group.Id, Money.Zero, true);
        c.AddLedger(ho);
        return (c, ho);
    }

    /// <summary>The May 3B carries the credit; the annual return's Table 6 must carry it too.</summary>
    [Fact]
    public void Gstr9_table_6_itc_availed_includes_the_isd_credit_the_monthly_3b_availed()
    {
        var (c, ho) = Build();
        new GstIsdCreditService(c).Post(
            "ISD/2025-26/001", D, new GstIsdCreditService.IsdCreditAmount(Cgst, Sgst, Igst, 0), ho.Id, D);

        var may = Gstr3b.Build(c, new DateOnly(2025, 5, 1), new DateOnly(2025, 5, 31));
        Assert.Equal(GstHeadsRupees, may.TotalItc4A4Isd.Amount);   // the monthly return availed it

        var g9 = Gstr9.Build(c, FyStart, FyEnd);
        Assert.Equal(GstHeadsRupees, g9.Table6Cgst.Amount + g9.Table6Sgst.Amount + g9.Table6Igst.Amount);
    }

    /// <summary>
    /// GSTR-9C Table 12: the books side counts the Input-ledger debit the ISD receipt posts, the return side
    /// does not — so 12A − 12E is a fabricated unreconciled difference on a book whose only ITC IS the receipt.
    /// </summary>
    [Fact]
    public void Gstr9c_reports_no_unreconciled_itc_when_the_only_itc_is_an_isd_receipt()
    {
        var (c, ho) = Build();
        new GstIsdCreditService(c).Post(
            "ISD/2025-26/001", D, new GstIsdCreditService.IsdCreditAmount(Cgst, Sgst, Igst, 0), ho.Id, D);

        var g9c = Gstr9c.Build(c, FyStart, FyEnd);
        Assert.Equal(GstHeadsRupees, g9c.Table12ABooksItc.Amount);          // the books saw it
        Assert.Equal(GstHeadsRupees, g9c.Table12EReturnItc.Amount);         // the return must too
    }
}
