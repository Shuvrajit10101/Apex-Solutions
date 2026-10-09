using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>TWO HIGH LIVE-MONEY DEFECTS ON THE OUTWARD SIDE, BOTH PROVED ON A MULTI-REGISTRATION BOOK.</b>
/// <c>T2-119</c> (ii) — <c>Gstr1.BuildTable9B</c> applied <b>no registration filter</b> — and <c>T2-122</c> — the
/// <b>outward</b> arm of <c>TaxAnalysis.ReadSide</c> was <b>unsigned</b>. They are tested in one file because they
/// share one fixture and one statutory rule, and because the second is only safe to fix now that the first's
/// precondition (a Table 9B record for an unlinked §34 note) exists on <c>main</c>.
///
/// <para><b>🔴 T2-119 (ii) — WHAT WAS WRONG, AND IT WAS INCONSISTENT INSIDE ONE TABLE.</b> Every other leg of
/// <c>Gstr1.Build</c> reaches its vouchers through <c>GstReportSupport.PostedDirectionalVouchers</c>, which applies
/// the <c>registrationId</c> filter. <c>BuildTable9B</c> did not: it walked <c>company.CreditDebitNoteLinks</c>
/// wholesale with only <c>FindVoucher</c>, a lower date bound and <c>CountsAsOf</c>. So within Table 9B the
/// <b>unlinked</b> rows (built by the scoped sweep) were correctly scoped while the <b>linked</b> rows were not —
/// the asymmetry asserted by
/// <see cref="Table_9B_is_scoped_consistently_for_BOTH_the_linked_and_the_unlinked_row"/>. <c>Gstr3b.ReadCdn</c>
/// already walked the same collection <i>with</i> the filter, so the correct shape was sitting one file away.</para>
///
/// <para><b>🔴 THE RUPEE FIGURE THAT WAS WRONG.</b> On the fixture below the <b>Karnataka</b> registration files
/// <c>TotalIgst</c> <b>−₹1,800.00</b> and a Table 9B carrying <b>two</b> rows — the second being the Tamil Nadu
/// registration's own §34 credit note, reference <c>TN/S/7</c>. Karnataka made no inter-State supply at all, so it
/// declared a <b>reduction it never made</b>, on a document issued under another GSTIN, while Tamil Nadu declared
/// the same note in its own return: <b>one credit note filed twice, against two different GSTINs.</b> Correct is
/// <c>TotalIgst</c> <b>₹0.00</b> and <b>one</b> 9B row. This is the same shape as <c>T1-64</c>/<c>T1-72</c>, where
/// <c>Gstr3b.ReadReversals</c> was the one unscoped leg of its own <c>Build</c>.</para>
///
/// <para><b>🔴 T2-122 — WHAT WAS WRONG.</b> <c>TaxAnalysis.ReadSide</c> signed the <b>inward</b> arm through
/// <c>GstReportSupport.SignOf</c> and hard-coded <c>1</c> on the outward arm, which its own comment recorded as
/// deliberate: the outward mirror "must move together with GSTR-1 so this analysis keeps agreeing with the return
/// it is reconciled against". GSTR-1 moved in wave 44 (the unlinked §34 note now takes its own Table 9B row), so
/// that precondition is met and the arm is signed here. <b>The figure:</b> a ₹50,000 sale at 18% with ₹20,000
/// returned reported outward <b>CGST 6,300.00 + SGST 6,300.00</b> against a true <b>2,700.00 + 2,700.00</b> — the
/// return's tax ADDED instead of subtracted — and the 900-basis-point rate row showed taxable <b>70,000</b> against
/// a true <b>30,000</b>.</para>
///
/// <para><b>🔴 SOURCES (R7), EVERY ONE OPENED BY CONTENT.</b>
/// <list type="bullet">
///   <item><b>Scoping is the vendor's own behaviour.</b> TallyPrime's GSTR-1 report: "<i>Press F3 (Company/Tax
///     Registration) and select the registration for which you want to view the report.</i>"
///     (<c>help.tallysolutions.com/gstr-1-report-in-tallyprime/</c>). And for the filed artefact: "<i>If you have
///     multiple registrations, select the required GST Registration.</i>"
///     (<c>help.tallysolutions.com/upload-gstr-1/</c>). A return is per registration, so another registration's
///     document may not appear in it.</item>
///   <item><b>⚠️ AND THE ONE SENTENCE THAT CUTS THE OTHER WAY, RECORDED RATHER THAN OMITTED.</b> The same vendor
///     page also says "<i>If you had created multiple registrations, the report displays the combined GST details
///     and activities across registrations.</i>" That describes the vendor's UNSCOPED landing view. This product
///     does not offer one — <c>GstReportSupport.EnsureRegistrationScoped</c> REFUSES an unscoped multi-registration
///     build, which is a divergence already shipped at v61 and deliberately not touched here. What both vendor
///     sentences agree on, and all this file asserts, is that once a registration IS named the report is THAT
///     registration's.</item>
///   <item><b>The sign is the statute's.</b> CGST Act §34(2), opened by content at
///     <c>taxinformation.cbic.gov.in/content/html/tax_repository/gst/acts/2017_CGST_act/active/chapter7/section34_v1.00.html</c>:
///     the issuer "<i>shall declare the details of such credit note in the return … and the tax liability shall be
///     adjusted in such manner as may be prescribed</i>". Adjusted DOWN — so a credit note subtracts from the
///     outward side, which is what the outward arm now does.</item>
/// </list></para>
/// </summary>
public sealed class GstOutwardRegistrationScopeAndSignTests
{
    private const string Karnataka = "29";
    private const string TamilNadu = "33";

    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly From = new(2025, 4, 1);
    private static readonly DateOnly To = new(2025, 4, 30);
    private static readonly DateOnly SaleDate = new(2025, 4, 9);
    private static readonly DateOnly ReturnDate = new(2025, 4, 20);
    private static readonly DateOnly BranchNoteDate = new(2025, 4, 22);

    private static string GstinFor(string stateCode, string pan)
    {
        var body = stateCode + pan + "1Z";
        return body + Gstin.ComputeCheckDigit(body + "0");
    }

    private static readonly string GstinPrimary = GstinFor(Karnataka, "AAPFU0939F");
    private static readonly string GstinIsd = GstinFor(Karnataka, "AAACC1206D");
    private static readonly string GstinBranch = GstinFor(TamilNadu, "AABCC1206D");
    private static readonly string GstinDebtorKa = GstinFor(Karnataka, "AAACS1206D");
    private static readonly string GstinDebtorTn = GstinFor(TamilNadu, "AADCS1206D");

    /// <summary>The branch's §34 note reference — the string that proves WHOSE document a 9B row is.</summary>
    private const string BranchNoteReference = "TN/S/7";

    internal sealed class Fixture
    {
        public required Company Company { get; init; }
        public required GstRegistration Branch { get; init; }
    }

    // ==============================================================================================================
    //  T2-119 (ii) — Table 9B registration scope
    // ==============================================================================================================

    /// <summary>
    /// 🔴 <b>THE MEASURED DEFECT, ON THE FIGURE THAT GETS FILED.</b> Karnataka made no inter-State supply, so its
    /// IGST must be ₹0.00. Before the fix <c>BuildTable9B</c> folded the Tamil Nadu registration's credit note into
    /// Karnataka's header through the <c>ref</c> totals and this read <b>−1,800.00</b>.
    /// </summary>
    [Fact]
    public void A_second_registrations_section_34_note_does_not_move_this_registrations_header()
    {
        var f = Build();

        var r = Gstr1.Build(f.Company, From, To, GstRegistration.PrimaryId);

        Assert.Equal(0m, r.TotalIgst.Amount);          // was -1,800.00
        // The Karnataka sale (4,500 per head) net of the Karnataka unlinked return (1,800 per head). Unchanged by
        // the fix — asserted so a scope filter that over-reached and dropped THIS registration's own rows is caught.
        Assert.Equal(2_700m, r.TotalCgst.Amount);
        Assert.Equal(2_700m, r.TotalSgst.Amount);
    }

    /// <summary>
    /// 🔴 <b>THE ASYMMETRY INSIDE ONE TABLE, WHICH IS WHAT MADE THIS GET MISSED.</b> Karnataka's Table 9B must hold
    /// exactly ONE row — its own unlinked note, whose original-invoice reference is <c>null</c> by construction.
    /// Before the fix it held TWO, the second naming <c>TN/S/7</c>: the unlinked row was scoped (it comes off the
    /// scoped sweep) while the linked row was not.
    /// </summary>
    [Fact]
    public void Table_9B_is_scoped_consistently_for_BOTH_the_linked_and_the_unlinked_row()
    {
        var f = Build();

        var r = Gstr1.Build(f.Company, From, To, GstRegistration.PrimaryId);

        var row = Assert.Single(r.Table9B);                     // was 2 rows
        Assert.Null(row.OriginalInvoiceNumber);                  // the unlinked Karnataka note
        Assert.Equal(-1_800m, row.Cgst.Amount);
        Assert.Equal(-1_800m, row.Sgst.Amount);
        Assert.Equal(0m, row.Igst.Amount);

        // 🔴 The whole point: the other registration's reference is nowhere in this return.
        Assert.DoesNotContain(r.Table9B, n => n.OriginalInvoiceNumber == BranchNoteReference);
    }

    /// <summary>
    /// 🔴 <b>THE NOTE IS NOT LOST — IT IS FILED EXACTLY ONCE, BY ITS OWNER.</b> The defect was a DUPLICATE, so the
    /// fix is only correct if Tamil Nadu's own return is byte-for-byte what it already was. Asserted deliberately:
    /// a filter that scoped the note out of both returns would trade a double declaration for a missing one, which
    /// breaches §34(2)'s first limb.
    /// </summary>
    [Fact]
    public void The_owning_registration_still_files_its_own_section_34_note()
    {
        var f = Build();

        var r = Gstr1.Build(f.Company, From, To, f.Branch.Id);

        var row = Assert.Single(r.Table9B);
        Assert.Equal(BranchNoteReference, row.OriginalInvoiceNumber);
        Assert.Equal(-1_800m, row.Igst.Amount);
        Assert.Equal(-1_800m, r.TotalIgst.Amount);
        // Tamil Nadu made no intra-State supply on this book.
        Assert.Equal(0m, r.TotalCgst.Amount);
        Assert.Equal(0m, r.TotalSgst.Amount);
    }

    /// <summary>
    /// A <b>single</b>-registration book is byte-identical (ER-13): the whole filter is a no-op when
    /// <c>registrationId</c> is <c>null</c> and the company holds one registration, which is every pre-v61 book.
    /// </summary>
    [Fact]
    public void A_single_registration_book_is_unchanged_by_the_scope_filter()
    {
        var c = BuildSingleRegistrationBook();

        var r = Gstr1.Build(c, From, To);

        var row = Assert.Single(r.Table9B);
        Assert.Equal(BranchNoteReference, row.OriginalInvoiceNumber);
        Assert.Equal(-1_800m, row.Igst.Amount);
        Assert.Equal(-1_800m, r.TotalIgst.Amount);
    }

    // ==============================================================================================================
    //  T2-122 — the outward arm of TaxAnalysis
    // ==============================================================================================================

    /// <summary>
    /// 🔴 <b>THE MEASURED DEFECT.</b> Outward tax must be the sale net of the return: 4,500.00 − 1,800.00 =
    /// <b>2,700.00</b> per head. Before the fix this read <b>6,300.00</b> per head — the return ADDED — i.e.
    /// ₹12,600.00 of outward liability analysed against ₹5,400.00 actually posted.
    /// </summary>
    [Fact]
    public void An_unlinked_sales_return_REDUCES_the_outward_side_of_the_tax_analysis()
    {
        var f = Build();

        var r = TaxAnalysis.Build(f.Company, From, To, GstRegistration.PrimaryId);

        Assert.Equal(2_700m, r.Outward.TotalCgst.Amount);   // was 6,300.00
        Assert.Equal(2_700m, r.Outward.TotalSgst.Amount);   // was 6,300.00
        Assert.Equal(0m, r.Outward.TotalIgst.Amount);
        Assert.Equal(5_400m, r.Outward.TotalTax.Amount);    // was 12,600.00
    }

    /// <summary>
    /// 🔴 <b>THE RATE ROW MOVED TOO, AND IT IS A SEPARATE FILED FIGURE.</b> The 900-basis-point CGST row's taxable
    /// value must be 50,000 − 20,000 = <b>30,000</b> with tax <b>2,700</b>. Before the fix: 70,000 / 6,300.
    /// </summary>
    [Fact]
    public void The_outward_rate_row_nets_the_sales_return_down()
    {
        var f = Build();

        var r = TaxAnalysis.Build(f.Company, From, To, GstRegistration.PrimaryId);

        var cgst = Assert.Single(r.Outward.RateRows, x => x.Head == GstTaxHead.Central);
        Assert.Equal(900, cgst.RateBasisPoints);
        Assert.Equal(30_000m, cgst.TaxableValue.Amount);    // was 70,000
        Assert.Equal(2_700m, cgst.Tax.Amount);              // was 6,300
    }

    /// <summary>
    /// 🔴 <b>THE REASON THE OUTWARD ARM ROUTES THROUGH <c>SignOf</c> RATHER THAN <c>IsReturnNote</c>, AND IT IS A
    /// SECOND WRONG-SIDE DEFECT THIS FILE IS THE FIRST TO COVER.</b> A credit note recording the SUPPLIER's credit
    /// note on a PURCHASE carries the <c>CreditNote</c> base type, which <c>DirectionOf</c> maps OUTWARD — but it is
    /// an <b>inward</b> document. <c>IsReturnNote</c> would return <c>-1</c> and REDUCE the outward side by a figure
    /// that was never outward; <c>SignOf</c> returns <c>0</c> — "not on this side at all" — because the linked
    /// ORIGINAL is a Purchase. <c>TaxAnalysis</c> has no credit/debit-note exclusion of its own, so unlike
    /// <c>Gstr3b.ReadSide</c> (where the <c>CdnLinkFor</c> guard stands first and makes this arm unreachable) this
    /// is the one outward sweep where <c>SignOf</c>'s linked-note branch is LIVE. Outward must stay at the sale net
    /// of the sales return — the purchase-side note must not touch it in either direction.
    /// </summary>
    [Fact]
    public void A_purchase_linked_credit_note_does_not_touch_the_outward_side_in_either_direction()
    {
        var f = Build(withPurchaseLinkedCreditNote: true);

        var r = TaxAnalysis.Build(f.Company, From, To, GstRegistration.PrimaryId);

        // Neither 2,700 − 900 = 1,800 (what a bare IsReturnNote would give) nor 2,700 + 900 = 3,600 (unsigned).
        Assert.Equal(2_700m, r.Outward.TotalCgst.Amount);
        Assert.Equal(2_700m, r.Outward.TotalSgst.Amount);
    }

    /// <summary>
    /// 🔴 <b>THE ANALYSIS NOW AGREES WITH THE RETURN IT IS RECONCILED AGAINST</b> — which is the precondition its own
    /// source comment set, and the reason the outward half was refused for two waves. Asserted across the two
    /// projections rather than inside one, because "they share a code path so they must agree" is exactly the sort
    /// of claim this project has had to retract: they do NOT share one — <c>Gstr1</c> signs with
    /// <c>VoucherEffects.IsReturnNote</c> plus a separate <c>BuildTable9B</c> fold, <c>TaxAnalysis</c> with
    /// <c>SignOf</c> over one sweep.
    /// </summary>
    [Fact]
    public void The_outward_tax_analysis_foots_to_the_GSTR1_header_for_the_same_registration()
    {
        var f = Build();

        var analysis = TaxAnalysis.Build(f.Company, From, To, GstRegistration.PrimaryId);
        var gstr1 = Gstr1.Build(f.Company, From, To, GstRegistration.PrimaryId);

        Assert.Equal(gstr1.TotalCgst.Amount, analysis.Outward.TotalCgst.Amount);
        Assert.Equal(gstr1.TotalSgst.Amount, analysis.Outward.TotalSgst.Amount);
        Assert.Equal(gstr1.TotalIgst.Amount, analysis.Outward.TotalIgst.Amount);
    }

    /// <summary>
    /// 🔴 <b>AND IT FOOTS TO THE BOOKS, NOT ONLY TO THE OTHER REPORT.</b> The outward CGST analysed must equal the
    /// Output CGST ledger's own NET credit movement in the window — credits less debits — because the engine posts a
    /// return note with <c>reverseSides: true</c> and therefore DEBITS Output CGST. Two wrong reports can agree with
    /// each other; neither can agree with the ledger.
    /// </summary>
    [Fact]
    public void The_outward_analysis_equals_the_output_tax_ledgers_own_net_movement()
    {
        var f = Build();

        var r = TaxAnalysis.Build(f.Company, From, To, GstRegistration.PrimaryId);

        Assert.Equal(NetCreditOfOutputHead(f.Company, GstTaxHead.Central), r.Outward.TotalCgst.Amount);
        Assert.Equal(NetCreditOfOutputHead(f.Company, GstTaxHead.State), r.Outward.TotalSgst.Amount);
    }

    /// <summary>
    /// 🔴 <b>THE INWARD ARM IS UNTOUCHED BY THIS SLICE — AND THE FIGURE IT PINS IS ITSELF STILL SHORT, WHICH IS
    /// SAID HERE RATHER THAN HIDDEN BEHIND A GREEN TEST.</b> Inward reads <b>900.00</b> per head: the ₹10,000
    /// purchase at 18% alone. The supplier's ₹5,000 credit note (450 per head) does <b>not</b> reduce it, because
    /// its <c>CreditNote</c> base type puts it on the OUTWARD sweep, where <c>SignOf</c> correctly returns
    /// <c>0</c> — "not on this side at all" — and no inward sweep ever sees it. So ITC of 900.00 is analysed where
    /// <b>450.00</b> is the economic truth.
    ///
    /// <para>🔴 <b>I FIRST WROTE THIS TEST ASSERTING 450 AND IT FAILED, AND THAT MATTERS MORE THAN THE FIX.</b> 450
    /// is the right economic answer and the WRONG expectation of the shipped design: a test pinning it would have
    /// rejected a correct change and demanded a re-routing of the document across <c>DirectionOf</c>, which would
    /// move GSTR-3B Table 4 figures on every book in the suite. The gap is <b>document classification</b> — a
    /// purchase-linked credit note belongs on the inward side — which is the question <c>SignOf</c>'s own
    /// documentation defers and <c>T2-113</c>/<c>T2-121</c> circle. <b>It is NOT closed here and must not be read
    /// as closed.</b> This assertion exists for one purpose: to prove that signing the outward arm did not disturb
    /// the inward half a sibling branch gated and mutation-pinned in wave 44.</para>
    /// </summary>
    [Fact]
    public void The_inward_arm_is_untouched_by_this_slice_and_its_own_under_reduction_stays_OPEN()
    {
        var f = Build(withPurchaseLinkedCreditNote: true);

        var r = TaxAnalysis.Build(f.Company, From, To, GstRegistration.PrimaryId);

        Assert.Equal(900m, r.Inward.TotalCgst.Amount);
        Assert.Equal(900m, r.Inward.TotalSgst.Amount);
    }

    /// <summary>
    /// 🔴 <b>T2-122 ON THE SURFACE A USER CAN ACTUALLY REACH TODAY.</b> Every assertion above names a registration
    /// explicitly, which is the GST filing screen's path. The <b>Tax Analysis</b> screen has no registration picker
    /// — <c>ReportsViewModel.BuildTaxAnalysis</c> passes none — so the figure a user sees is the
    /// <b>single-registration</b> one, and that is the one that was wrong on screen. Same arithmetic, no
    /// <c>registrationId</c> anywhere: 4,500.00 − 1,800.00 = <b>2,700.00</b> per head, where it read
    /// <b>6,300.00</b>.
    /// </summary>
    [Fact]
    public void The_outward_arm_is_signed_on_the_unscoped_single_registration_path_too()
    {
        var c = BuildSingleRegistrationBook(withSaleAndReturn: true);

        var r = TaxAnalysis.Build(c, From, To);

        Assert.Equal(2_700m, r.Outward.TotalCgst.Amount);   // was 6,300.00
        Assert.Equal(2_700m, r.Outward.TotalSgst.Amount);   // was 6,300.00
    }

    // ================================================================ fixture

    /// <summary>The Output tax ledger's NET credit movement for one head in the window: the BOOKS' own figure,
    /// read off the posted sides rather than from any projection.</summary>
    private static decimal NetCreditOfOutputHead(Company company, GstTaxHead head)
    {
        var ledgerId = new GstService(company).FindTaxLedger(head, GstTaxDirection.Output)!.Id;
        return company.Vouchers
            .Where(v => v.Date >= From && v.Date <= To)
            .SelectMany(v => v.Lines)
            .Where(l => l.LedgerId == ledgerId)
            .Sum(l => l.Side == DrCr.Credit ? l.Amount.Amount : -l.Amount.Amount);
    }

    /// <summary>
    /// A Karnataka primary Regular registration, a Karnataka ISD beside it and a <b>Tamil Nadu</b> Regular branch —
    /// the three-registration shape <c>PurchaseReturnDirectionTests</c> established. Postings:
    /// <list type="bullet">
    ///   <item><b>Karnataka (primary):</b> a ₹50,000 intra-State sale at 18% ⇒ CGST 4,500 + SGST 4,500.</item>
    ///   <item><b>Karnataka (primary):</b> an <b>UNLINKED</b> sales-return credit note of ₹20,000 at 18% ⇒ 1,800 per
    ///     head, posted the way the entry screen posts one (<c>reverseSides: true</c>). Unlinked is the ORDINARY
    ///     shape — the §34 annotation is opt-in — and it is what reaches both defects.</item>
    ///   <item><b>Tamil Nadu (branch):</b> a <b>LINKED</b> §34 credit note of ₹10,000 inter-State at 18% ⇒ IGST
    ///     1,800, reference <c>TN/S/7</c>. This is the document that leaked into Karnataka's return.</item>
    /// </list>
    /// </summary>
    internal static Fixture Build(bool withPurchaseLinkedCreditNote = false)
    {
        var c = CompanyFactory.CreateSeeded("Multi GSTIN Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = Karnataka,
            Gstin = GstinPrimary,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        c.Gst!.AddRegistration(new GstRegistration(
            Guid.NewGuid(), "Head Office (ISD)", Karnataka, GstinIsd,
            GstRegistrationType.InputServiceDistributor, FyStart));

        var branch = new GstRegistration(
            Guid.NewGuid(), "Tamil Nadu Registration", TamilNadu, GstinBranch,
            GstRegistrationType.Regular, FyStart);
        c.Gst!.AddRegistration(branch);
        c.Gst!.EnsureValid();

        var ledgers = new LedgerService(c);
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtorKa = Add(c, "Karnataka Debtor", "Sundry Debtors", true);
        debtorKa.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinDebtorKa, StateCode = Karnataka,
        };
        var debtorTn = Add(c, "Tamil Nadu Debtor", "Sundry Debtors", true);
        debtorTn.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinDebtorTn, StateCode = TamilNadu,
        };

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var creditNoteType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.CreditNote).Id;

        // ---- Karnataka: the sale.
        PostOutward(ledgers, gst, salesType, sales, debtorKa, 50_000m, interState: false,
            reverseSides: false, SaleDate, registrationId: null);

        // ---- Karnataka: the UNLINKED sales return. No GstCreditDebitNoteLink ⇒ the ordinary entry-screen shape.
        PostOutward(ledgers, gst, creditNoteType, sales, debtorKa, 20_000m, interState: false,
            reverseSides: true, ReturnDate, registrationId: null);

        // ---- Tamil Nadu: the LINKED §34 credit note. Inter-State (home 29 → party 33) ⇒ IGST.
        var branchNoteId = PostOutward(ledgers, gst, creditNoteType, sales, debtorTn, 10_000m, interState: true,
            reverseSides: true, BranchNoteDate, registrationId: branch.Id);
        c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), branchNoteId, CdnType.Credit, null, BranchNoteReference, SaleDate,
            "01 sales return", is9BTarget: true));

        if (withPurchaseLinkedCreditNote)
            AddPurchaseLinkedCreditNote(c, ledgers, gst);

        return new Fixture { Company = c, Branch = branch };
    }

    /// <summary>
    /// The same book with ONE registration, to hold ER-13: the Tamil Nadu note becomes the single registration's own
    /// and must file exactly as it did before the filter existed.
    /// </summary>
    private static Company BuildSingleRegistrationBook(bool withSaleAndReturn = false)
    {
        var c = CompanyFactory.CreateSeeded("Single GSTIN Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = Karnataka,
            Gstin = GstinPrimary,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var ledgers = new LedgerService(c);
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtorTn = Add(c, "Tamil Nadu Debtor", "Sundry Debtors", true);
        debtorTn.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinDebtorTn, StateCode = TamilNadu,
        };

        var creditNoteType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.CreditNote).Id;
        var noteId = PostOutward(ledgers, gst, creditNoteType, sales, debtorTn, 10_000m, interState: true,
            reverseSides: true, BranchNoteDate, registrationId: null);
        c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), noteId, CdnType.Credit, null, BranchNoteReference, SaleDate,
            "01 sales return", is9BTarget: true));

        if (withSaleAndReturn)
        {
            // The same ₹50,000 intra-State sale and ₹20,000 UNLINKED return as the multi-registration fixture, so
            // the outward-sign arithmetic is identical on the path that carries no registrationId at all.
            var debtorKa = Add(c, "Karnataka Debtor", "Sundry Debtors", true);
            debtorKa.PartyGst = new PartyGstDetails
            {
                RegistrationType = GstRegistrationType.Regular, Gstin = GstinDebtorKa, StateCode = Karnataka,
            };
            var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
            PostOutward(ledgers, gst, salesType, sales, debtorKa, 50_000m, interState: false,
                reverseSides: false, SaleDate, registrationId: null);
            PostOutward(ledgers, gst, creditNoteType, sales, debtorKa, 20_000m, interState: false,
                reverseSides: true, ReturnDate, registrationId: null);
        }

        return c;
    }

    /// <summary>
    /// A ₹10,000 intra-State PURCHASE at 18%, plus the SUPPLIER's ₹5,000 credit note on it recorded on the
    /// <c>CreditNote</c> base type and LINKED to that purchase. <c>DirectionOf(CreditNote)</c> is OUTWARD, so this
    /// voucher reaches the outward sweep although it is an inward document — the shape no test in the suite covered,
    /// because every credit-note link in it points at a Sales original.
    /// </summary>
    private static void AddPurchaseLinkedCreditNote(Company c, LedgerService ledgers, GstService gst)
    {
        var purchases = Add(c, "Purchases", "Purchase Accounts", true);
        var creditor = Add(c, "Karnataka Supplier", "Sundry Creditors", false);
        creditor.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinDebtorKa, StateCode = Karnataka,
        };

        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
        var creditNoteType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.CreditNote).Id;

        var purchaseTax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(10_000m), 1800) },
            interState: false, GstTaxDirection.Input);
        var purchaseLines = new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(10_000m), DrCr.Debit),
            new(creditor.Id, Money.FromRupees(10_000m + purchaseTax.TotalTax.Amount), DrCr.Credit),
        };
        purchaseLines.AddRange(purchaseTax.TaxLines);
        var purchaseId = Guid.NewGuid();
        ledgers.Post(new Voucher(purchaseId, purchaseType, SaleDate, purchaseLines, partyId: creditor.Id));

        var noteTax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(5_000m), 1800) },
            interState: false, GstTaxDirection.Input, reverseSides: true);
        var noteLines = new List<EntryLine>
        {
            new(creditor.Id, Money.FromRupees(5_000m + noteTax.TotalTax.Amount), DrCr.Debit),
            new(purchases.Id, Money.FromRupees(5_000m), DrCr.Credit),
        };
        noteLines.AddRange(noteTax.TaxLines);
        var noteId = Guid.NewGuid();
        ledgers.Post(new Voucher(noteId, creditNoteType, ReturnDate, noteLines, partyId: creditor.Id));

        c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), noteId, CdnType.Credit, purchaseId, "P/1", SaleDate,
            "01 sales return", is9BTarget: true));
    }

    /// <summary>Posts one outward document of <paramref name="value"/> at 18%, returning its voucher id.
    /// <paramref name="reverseSides"/> mirrors <c>ComputeItemInvoiceGst</c>'s own <c>reverseSides: IsReturnNote</c>,
    /// so a return note genuinely DEBITS the Output head — the real screen's shape.</summary>
    private static Guid PostOutward(
        LedgerService ledgers, GstService gst, Guid typeId, Domain.Ledger sales, Domain.Ledger party,
        decimal value, bool interState, bool reverseSides, DateOnly date, Guid? registrationId)
    {
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(value), 1800) },
            interState, GstTaxDirection.Output, reverseSides: reverseSides);
        var gross = value + tax.TotalTax.Amount;

        var lines = reverseSides
            ? new List<EntryLine>
            {
                new(sales.Id, Money.FromRupees(value), DrCr.Debit),      // a return reduces income
                new(party.Id, Money.FromRupees(gross), DrCr.Credit),     // and reduces the debt
            }
            : new List<EntryLine>
            {
                new(party.Id, Money.FromRupees(gross), DrCr.Debit),
                new(sales.Id, Money.FromRupees(value), DrCr.Credit),
            };
        lines.AddRange(tax.TaxLines);

        var id = Guid.NewGuid();
        var v = new Voucher(id, typeId, date, lines, partyId: party.Id);
        if (registrationId is { } r) v.GstRegistrationId = r;
        ledgers.Post(v);
        return id;
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
