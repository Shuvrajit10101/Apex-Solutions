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
    ///
    /// <para><b>🔴 W0-9 TAIL review (findings #1/#3) — WHAT THIS FIXTURE ACTUALLY IS, ASSERTED RATHER THAN DESCRIBED.</b>
    /// <c>GstReportSupport.HasPostedForwardCessLines</c>'s doc used to say this test posts its netting pair to "the
    /// company's own Output Cess ledger", so <c>PostsToAnOrdinaryOutputTaxLedger</c> holds <c>CarriesForwardTax</c> true
    /// here. That was FALSE about this very fixture. <c>GstService.EnableGst</c> seeds only Central/State/Integrated for
    /// each direction — the Cess pair is created lazily by <c>EnsureCessLedgers</c>, which
    /// <see cref="NewRegularCompany"/> never reaches — so <c>FindTaxLedger(Cess, Output)</c> returns <c>null</c> and the
    /// <c>??</c> fallback below builds an <b>UNCLASSIFIED</b> "Output Cess" ledger. The three assertions that open the
    /// act block pin exactly that, so any future seeding change breaks the shipped doc's premise LOUDLY instead of
    /// leaving a warning whose cited pin quietly stopped meaning what it says.</para>
    ///
    /// <para><b>And this test still cannot demonstrate the document-kind flip</b> — for a reason that has nothing to do
    /// with the ledger. Its voucher carries no stock lines and no v49 accounting-invoice flag, so
    /// <c>GstReportSupport.IsTaxInvoice</c> is false and <c>IsBillOfSupply</c> returns false at limb 2's
    /// <c>if (!IsTaxInvoice(…)) return false;</c> gate whatever <c>CarriesForwardTax</c> answers. The shape that DOES
    /// bite is <see cref="A_netting_cess_pair_off_the_tax_ledgers_still_decides_the_document_kind"/>.</para>
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

        // W0-9 tail findings #1/#3 — EnableGst seeds no Cess pair, so this ledger is the UNCLASSIFIED fallback and the
        // ledger-side disjunct is BLIND to this voucher. The shipped doc said the opposite about this exact fixture.
        Assert.Null(new GstService(c).FindTaxLedger(GstTaxHead.Cess, GstTaxDirection.Output));
        Assert.Null(cess.GstClassification);
        Assert.False(GstReportSupport.PostsToAnOrdinaryOutputTaxLedger(c, v));

        // …and the flip is out of reach here anyway: no stock lines, no v49 flag ⇒ not an invoice document at all, so
        // IsBillOfSupply bails at its limb-2 `!IsTaxInvoice` gate whatever the money gate says.
        Assert.False(GstReportSupport.IsTaxInvoice(c, v));
        Assert.False(GstReportSupport.IsBillOfSupply(c, v));

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

    /// <summary>
    /// <b>🔴 W0-9 review finding #3 — the finding-#7 warning, pinned on a shape that can actually demonstrate it.</b>
    /// <c>HasPostedForwardCessLines</c>'s doc says that substituting <c>PostedCessTotal(…) != Money.Zero</c> for it
    /// would re-classify the DOCUMENT KIND for a voucher whose forward cess legs net to zero.
    /// <see cref="A_cess_line_exists_even_when_the_posted_cess_sums_to_zero"/> cannot show that — <b>not</b>, as this
    /// comment and the shipped doc both used to claim, because that test posts its pair to the company's own classified
    /// Output Cess ledger (W0-9 tail findings #1/#3: <c>EnableGst</c> seeds no Cess pair at all, so its ledger is an
    /// UNCLASSIFIED fallback and <c>PostsToAnOrdinaryOutputTaxLedger</c> is FALSE there, now asserted in that test).
    /// The real reason is that its voucher is neither an item invoice nor a v49 accounting invoice, so
    /// <c>IsTaxInvoice</c> is false and <c>IsBillOfSupply</c> returns false at limb 2's
    /// <c>if (!IsTaxInvoice(…)) return false;</c> gate whatever <c>CarriesForwardTax</c> answers. A warning whose only
    /// cited pin cannot bite is how a maintainer talks himself past it.
    ///
    /// <para><b>The shape that does bite</b> puts the same netting pair on a ledger the company does NOT classify as a
    /// GST tax ledger, on a voucher that IS an invoice document — reachable on imported or hand-keyed data, since
    /// <c>CanonicalXml</c> makes <c>&lt;gst&gt;</c> optional per entryLine and never requires the ledger it names to
    /// carry a classification. All three disjuncts are then decided by this one predicate, and the voucher is a
    /// <b>Regular</b> dealer's wholly-exempt outward supply (see the fourth paragraph for why not a §10 one), so the
    /// flip is visible on the statutory title and on the NIC <c>docType</c>.</para>
    ///
    /// <para><b>Bite (measured, then restored):</b> rewrite <c>CarriesForwardTax</c>'s middle disjunct as
    /// <c>PostedCessTotal(voucher) != Money.Zero</c> and this voucher is titled BILL OF SUPPLY and files <c>BIL</c>,
    /// while its books record two forward cess legs.</para>
    ///
    /// <para><b>Why a REGULAR dealer's wholly-exempt supply and not a §10 one.</b> On the §10 limb the FILING would not
    /// move at all — <c>IsBillOfSupplyForFiling</c> is <c>IsCompositionBillOfSupply || IsBillOfSupply</c> under the R12
    /// ruling, so it already answers <c>BIL</c> for a composition dealer whatever the money gate says, and only the
    /// printed title could flip. On the exempt limb BOTH move, which is exactly the pairing the doc comment claims.</para>
    ///
    /// <para>Odd to the paisa (₹8,513.41) and deliberately NOT round — a netting pair is the whole point, so the pair
    /// must be one a rounding shortcut could not produce.</para>
    /// </summary>
    [Fact]
    public void A_netting_cess_pair_off_the_tax_ledgers_still_decides_the_document_kind()
    {
        var c = NewRegularCompany("Netting Cess Off-Ledger Co");
        // W0-9 tail finding #6 — the dealer this test runs on, PINNED. The doc above once called this "a §10 dealer's
        // outward supply" while its fourth paragraph explained why it deliberately is NOT one; asserting the fixture
        // means the two can never disagree again.
        Assert.Equal(GstRegistrationType.Regular, c.Gst!.RegistrationType);

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var exempt = inv.CreateStockItem("Fresh Milk", grp.Id, nos.Id);
        exempt.Gst = new StockItemGstDetails { HsnSac = "040110", Taxability = GstTaxability.Exempt };

        // A ledger the company does NOT classify — no GstClassification, so the ledger-side disjunct cannot see it.
        var suspense = AddLedger(c, "Cess Suspense", "Duties & Taxes", openingIsDebit: false);
        Assert.Null(suspense.GstClassification);

        var customerId = c.FindLedgerByName("Local Customer")!.Id;
        var taxable = Money.FromRupees(47_296.73m);
        var v = new Voucher(Guid.NewGuid(),
            c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive).Id,
            FyStart.AddDays(9), new[]
            {
                new EntryLine(customerId, Money.FromRupees(47_296.73m), DrCr.Debit),
                new EntryLine(c.FindLedgerByName("Sales")!.Id, Money.FromRupees(47_296.73m), DrCr.Credit),
                new EntryLine(suspense.Id, Money.FromRupees(8_513.41m), DrCr.Credit,
                    gst: new GstLineTax(GstTaxHead.Cess, 1800, taxable)),
                new EntryLine(suspense.Id, Money.FromRupees(-8_513.41m), DrCr.Credit,
                    gst: new GstLineTax(GstTaxHead.Cess, 1800, taxable)),
            },
            partyId: customerId, inventoryLines: new[]
            {
                new VoucherInventoryLine(exempt.Id, c.MainLocation!.Id, 1m, Money.FromRupees(47_296.73m)),
            });

        // The other two disjuncts are BLIND to this voucher — so this predicate alone decides the document kind.
        Assert.False(GstReportSupport.HasForwardTaxLines(v));
        Assert.False(GstReportSupport.PostsToAnOrdinaryOutputTaxLedger(c, v));
        Assert.Equal(Money.Zero, GstReportSupport.PostedCessTotal(v));      // the sum the substitution would read
        Assert.True(GstReportSupport.HasPostedForwardCessLines(v));         // the existence it must read instead

        // …and that is what keeps a cess-bearing movement off the BILL OF SUPPLY title and off NIC `BIL`.
        Assert.True(GstReportSupport.CarriesForwardTax(c, v));
        Assert.True(GstReportSupport.IsTaxInvoice(c, v));                   // it IS an invoice document …
        Assert.False(GstReportSupport.IsBillOfSupply(c, v));                // … and the gate is the only thing
        Assert.False(GstReportSupport.IsBillOfSupplyForFiling(c, v));       // …so the EWB-01 declares INV, not BIL
    }
}
