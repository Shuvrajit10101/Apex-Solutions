using System;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>W0-1 follow-up review — the shared "does this voucher carry forward tax?" predicates.</b>
///
/// <list type="number">
/// <item><b>Finding #7 (LOW).</b> <c>HasPostedForwardCessLines</c>'s doc comment claimed it was "the exact predicate
/// <c>PostedCessTotal(voucher) != 0</c> expresses". That equivalence is FALSE: the method answers on the EXISTENCE of a
/// forward cess line, <c>PostedCessTotal</c> on the SUM of their amounts. The distinction is PINNED here rather than
/// merely described.
/// <para><b>W0-10 review (finding #7) — the CONSEQUENCE named here has moved, and the pin has not.</b> This used to
/// say that substituting one for the other would flip <c>VoucherPrintProjector.HasPostedForwardCess</c> from "use the
/// POSTED cess" to "resolve cess live from the master", reintroducing F4. <b>W0-10 deleted that member</b> along with
/// every live cess resolve on the print path. The surviving consumer is <c>GstReportSupport.CarriesForwardTax</c>,
/// which <c>IsBillOfSupply</c>'s first gate reads — so for a voucher whose forward cess legs net to zero the swap now
/// re-classifies the DOCUMENT KIND instead, titling a cess-bearing movement BILL OF SUPPLY and filing NIC
/// <c>BIL</c>.</para></item>
/// <item><b>Finding #1 (HIGH).</b> "Carries forward tax" had to stop meaning "carries GST metadata": the As-Voucher
/// entry path stamps none, so a hand-keyed credit to Output CGST/SGST was invisible to the §10(4) posting guard.
/// <c>CarriesForwardTax</c> also reads the LEDGER classification, and its exclusions (Input direction, RCM Output) are
/// pinned so it can never widen into someone else's liability.</item>
/// </list>
///
/// <para>Sources: CGST Act §10(4), §31(3)(c), §32(2) —
/// <c>https://cbic-gst.gov.in/pdf/CGST-Act-Updated-30092020.pdf</c>.</para>
/// <para>Figures are deliberately odd-valued (8,513.41 / 4,256.71 / 47,296.73) — round numbers assert nothing.</para>
/// </summary>
public sealed class GstForwardTaxPredicateTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    private static DomainLedger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Company NewRegularCompany(string name)
    {
        var c = CompanyFactory.CreateSeeded(name, FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Quarterly,
        });
        AddLedger(c, "Sales", "Sales Accounts", openingIsDebit: false);
        var customer = AddLedger(c, "Local Customer", "Sundry Debtors", openingIsDebit: true);
        customer.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
        };
        return c;
    }

    private static Voucher SalesVoucher(Company c, params EntryLine[] lines) =>
        new(Guid.NewGuid(),
            c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive).Id,
            FyStart.AddDays(9), lines,
            partyId: c.FindLedgerByName("Local Customer")!.Id);

    /// <summary>
    /// <b>Finding #7 — existence is not a non-zero sum.</b> Two forward Cess legs of +8,513.41 and −8,513.41 make
    /// <c>HasPostedForwardCessLines</c> TRUE while <c>PostedCessTotal</c> is exactly <see cref="Money.Zero"/>. The two
    /// are therefore NOT interchangeable, and the day someone rewrites the caller as
    /// <c>PostedCessTotal(v) != Money.Zero</c> this test says so.
    /// </summary>
    [Fact]
    public void A_cess_line_exists_even_when_the_posted_cess_sums_to_zero()
    {
        var c = NewRegularCompany("Netting Cess Co");
        var cess = new GstService(c).FindTaxLedger(GstTaxHead.Cess, GstTaxDirection.Output)
                   ?? AddLedger(c, "Output Cess", "Duties & Taxes", openingIsDebit: false);
        var taxable = Money.FromRupees(47_296.73m);

        var v = SalesVoucher(c,
            new EntryLine(cess.Id, Money.FromRupees(8_513.41m), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.Cess, 1800, taxable)),
            new EntryLine(cess.Id, Money.FromRupees(-8_513.41m), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.Cess, 1800, taxable)));

        Assert.True(GstReportSupport.HasPostedForwardCessLines(v));       // a forward cess line EXISTS …
        Assert.Equal(Money.Zero, GstReportSupport.PostedCessTotal(v));    // … and the amounts sum to zero
        Assert.False(GstReportSupport.PostedCessTotal(v) != Money.Zero);  // the substitution the doc invited: WRONG
    }

    /// <summary>
    /// <b>Finding #1 — the As-Voucher shape.</b> A plain credit to the company's own <b>Output</b> CGST/SGST ledgers,
    /// carrying NO <see cref="GstLineTax"/> (the entry screen stamps none), is collected tax. The metadata predicates
    /// answer false; the ledger-classification predicate answers true.
    /// </summary>
    [Fact]
    public void An_untagged_credit_to_an_output_tax_ledger_still_carries_forward_tax()
    {
        var c = NewRegularCompany("Untagged Forward Co");
        var gst = new GstService(c);
        var cgst = gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output)!;
        var sgst = gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Output)!;

        var v = SalesVoucher(c,
            new EntryLine(c.FindLedgerByName("Local Customer")!.Id, Money.FromRupees(55_810.14m), DrCr.Debit),
            new EntryLine(c.FindLedgerByName("Sales")!.Id, Money.FromRupees(47_296.73m), DrCr.Credit),
            new EntryLine(cgst.Id, Money.FromRupees(4_256.71m), DrCr.Credit),
            new EntryLine(sgst.Id, Money.FromRupees(4_256.70m), DrCr.Credit));

        Assert.All(v.Lines, l => Assert.Null(l.Gst));
        Assert.False(GstReportSupport.HasForwardTaxLines(v));         // metadata sees nothing …
        Assert.False(GstReportSupport.HasPostedForwardCessLines(v));
        Assert.True(GstReportSupport.CarriesForwardTax(c, v));        // … the general ledger does
    }

    /// <summary>The exclusions, pinned: an <b>Input</b> ITC ledger is not tax collected FROM a recipient, and an
    /// <b>RCM Output</b> ledger is the §49(4) liability the RECIPIENT pays — neither may widen the predicate.</summary>
    [Fact]
    public void Input_itc_and_rcm_output_ledgers_are_not_collected_forward_tax()
    {
        var c = NewRegularCompany("Exclusions Co");
        var gst = new GstService(c);
        var inputCgst = gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Input)!;
        var rcmOutputCgst = gst.EnsureRcmOutputLedger(GstTaxHead.Central);

        var input = SalesVoucher(c,
            new EntryLine(inputCgst.Id, Money.FromRupees(4_256.71m), DrCr.Debit),
            new EntryLine(c.FindLedgerByName("Sales")!.Id, Money.FromRupees(4_256.71m), DrCr.Credit));
        Assert.False(GstReportSupport.CarriesForwardTax(c, input));

        var rcm = SalesVoucher(c,
            new EntryLine(rcmOutputCgst.Id, Money.FromRupees(4_256.71m), DrCr.Credit),
            new EntryLine(c.FindLedgerByName("Local Customer")!.Id, Money.FromRupees(4_256.71m), DrCr.Debit));
        Assert.False(GstReportSupport.CarriesForwardTax(c, rcm));
    }

    /// <summary>ER-13: an ordinary untaxed sale — every leg on a plain sales/party ledger — carries no forward tax, so
    /// nothing about the ordinary composition document changes.</summary>
    [Fact]
    public void An_ordinary_untaxed_sale_carries_no_forward_tax()
    {
        var c = NewRegularCompany("Plain Sale Co");
        var v = SalesVoucher(c,
            new EntryLine(c.FindLedgerByName("Local Customer")!.Id, Money.FromRupees(47_296.73m), DrCr.Debit),
            new EntryLine(c.FindLedgerByName("Sales")!.Id, Money.FromRupees(47_296.73m), DrCr.Credit));
        Assert.False(GstReportSupport.CarriesForwardTax(c, v));
    }
}
