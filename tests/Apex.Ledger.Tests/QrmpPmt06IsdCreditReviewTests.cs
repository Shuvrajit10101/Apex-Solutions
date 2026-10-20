using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>THE QRMP PMT-06 SELF-ASSESSMENT COUNTS TABLE 4(A)(4) ISD CREDIT — review finding R3-B1.</b>
///
/// <para><b>What was wrong.</b> The ISD-receipt slice added <c>Itc4A4Isd*</c> to the monthly GSTR-3B and carried it
/// into Table 4(C), the on-screen 3B, GSTR-9 Table 6 and the filed offline JSON — and <b>not</b> into
/// <see cref="GstQrmp"/>'s PMT-06 self-assessment, which is the one place the figure becomes a <b>cash deposit the
/// filer actually makes</b>. On the fixture below, ₹18,000.00 of forward output tax against ₹14,000.00 of ISD
/// credit availed in the same month's 3B suggested a deposit of <b>₹18,000.00</b> instead of <b>₹4,000.00</b>.</para>
///
/// <para><b>Why the credit counts.</b> An ISD receipt DEBITS the Input {head} ledgers, so
/// <see cref="ElectronicLedgersView"/> — the pool <c>RunSetOffViewModel</c> actually posts from — already counts it.
/// The advisory disagreed with the authoritative poster beside it. Grounding: CBIC Circular No. 170/02/2022-GST,
/// Annexure, row <i>"(C) Net ITC Available (A)-(B)"</i>, formula <c>C=A1+A2+A3+A4+A5-B1-B2</c> — A4 is this credit,
/// and it lands in the electronic credit ledger like any other availed ITC.</para>
///
/// <para>These assert on the realised <see cref="GstPmt06Suggestion"/> the QRMP projection hands the screen, over a
/// posted book — never on an internal the code just set.</para>
/// </summary>
public sealed class QrmpPmt06IsdCreditReviewTests
{
    private const string HomeGstin = "27AAPFU0939F1ZV";
    private const string CustomerGstin = "27AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly SaleDate = new(2025, 4, 5);
    private static readonly DateOnly IsdDocDate = new(2025, 4, 8);
    private static readonly DateOnly IsdPostDate = new(2025, 4, 10);

    private static Domain.Ledger Add(Company c, string name, string group, bool debit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(group)!.Id, Money.Zero, debit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>A QUARTERLY (QRMP) Regular filer with a ₹1,00,000 local sale @18% in M1, and optionally one ISD
    /// receipt of CGST ₹7,000.00 + SGST ₹7,000.00 in the same month.</summary>
    private static Company Build(bool withIsdCredit)
    {
        var c = CompanyFactory.CreateSeeded("Qrmp Isd Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = HomeGstin,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Quarterly,
        });

        var sales = c.FindLedgerByName("Sales") ?? Add(c, "Sales", "Sales Accounts", false);
        var customer = Add(c, "Qrmp Customer", "Sundry Debtors", true);
        customer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = CustomerGstin, StateCode = "27" };

        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(100_000m), 1800) }, false, GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(customer.Id, Money.FromRupees(118_000m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(100_000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), salesType, SaleDate, lines, partyId: customer.Id));

        if (withIsdCredit)
        {
            var headOffice = Add(c, "Head Office Account", "Indirect Expenses", true);
            new GstIsdCreditService(c).Post(
                "ISD/2025-26/QRMP", IsdDocDate,
                new GstIsdCreditService.IsdCreditAmount(700_000L, 700_000L, 0L, 0L),
                headOffice.Id, IsdPostDate);
        }

        return c;
    }

    private static GstPmt06Suggestion Month1(Company c)
    {
        var q = GstQrmp.Build(c, FyStart, FyStart.AddYears(1).AddDays(-1));
        Assert.True(q.Applicable);
        return q.Quarters[0].Month1Pmt06;
    }

    /// <summary>
    /// 🔴 <b>THE MONEY TEST.</b> Forward output tax ₹18,000.00 less ₹14,000.00 of Table 4(A)(4) ISD credit leaves
    /// ₹4,000.00 of cash to deposit. Dropping the <c>TotalItc4A4Isd</c> term from the subtrahend restores the old
    /// ₹18,000.00 and fails this assertion by name.
    /// </summary>
    [Fact]
    public void Pmt06_self_assessment_nets_the_table_4a4_isd_credit_off_the_forward_liability()
    {
        var c = Build(withIsdCredit: true);

        // The book the suggestion is built from, stated rather than assumed.
        var b = Gstr3b.Build(c, new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30));
        Assert.Equal(18_000m, b.TotalOutwardTax.Amount);
        Assert.Equal(14_000m, b.TotalItc4A4Isd.Amount);
        Assert.Equal(0m, b.TotalItc.Amount);          // 4(A)(5) is empty — no purchase on this book
        Assert.Equal(0m, b.TotalRcmItc.Amount);

        Assert.Equal(4_000m, Month1(c).SelfAssessment.Amount);
    }

    /// <summary>
    /// <b>THE CREDIT IS NOT COUNTED TWICE.</b> The suggestion moves by EXACTLY the ISD credit and by nothing else:
    /// the same book without the receipt suggests ₹18,000.00, with it ₹4,000.00 — a difference of ₹14,000.00.
    /// </summary>
    [Fact]
    public void The_suggestion_moves_by_exactly_the_isd_credit_and_no_more()
    {
        var withOut = Month1(Build(withIsdCredit: false)).SelfAssessment.Amount;
        var with = Month1(Build(withIsdCredit: true)).SelfAssessment.Amount;

        Assert.Equal(18_000m, withOut);
        Assert.Equal(14_000m, withOut - with);
    }

    /// <summary>
    /// <b>ER-13.</b> A book that never receives ISD credit is unchanged — the term is additive and defaults to zero,
    /// so the overwhelmingly common single-source book reads exactly as it did before.
    /// </summary>
    [Fact]
    public void A_book_with_no_isd_credit_is_unchanged()
    {
        Assert.Equal(18_000m, Month1(Build(withIsdCredit: false)).SelfAssessment.Amount);
    }

    /// <summary>
    /// <b>THE ADVISORY AGREES WITH THE POSTER.</b> The credit this suggestion now nets off is the same credit the
    /// Run Set-Off screen draws on — <see cref="ElectronicLedgersView"/>'s Input-ledger pool, which the ISD receipt
    /// debits. Two screens that disagreed about the available credit is what made the wrong deposit plausible.
    /// </summary>
    [Fact]
    public void The_isd_credit_is_in_the_electronic_credit_ledger_pool_the_setoff_screen_posts_from()
    {
        var pools = ElectronicLedgersView.Build(
            Build(withIsdCredit: true), new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30));

        Assert.Equal(7_000m, pools.CreditCgst.Amount);
        Assert.Equal(7_000m, pools.CreditSgst.Amount);
    }
}
