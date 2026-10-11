using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>T2-123 — THE THIRD REPORT-SIDE WALKER OF <c>company.CreditDebitNoteLinks</c> TO NEED A REGISTRATION
/// FILTER, PLUS ONE THE BRIEF DID NOT NAME.</b> <c>Gstr1Amendments.BuildTable9C</c> walked that collection
/// wholesale, after <c>Gstr3b.ReadCdn</c> (scoped at v61) and <c>Gstr1.BuildTable9B</c> (scoped as T2-119 ii). The
/// sweep this slice ran over every company-wide collection read by a GST report also found
/// <c>Gstr9.ReadTable7</c> — the one unscoped leg of its own <c>Build</c>, exactly the <c>T1-64</c>/<c>T1-72</c>
/// shape — and it is fixed and pinned here too.
///
/// <para>🔴 <b>WHAT WAS WRONG, AND THE TWO HALVES ARE DIFFERENT KINDS OF WRONG. MEASURED, NOT INFERRED.</b>
/// <list type="number">
///   <item><b>Table 9A was a REACHABILITY failure, not wrong money.</b> It folded through <c>Gstr1.Build</c> with
///     no registration, so <c>GstReportSupport.EnsureRegistrationScoped</c> threw: a probe on the fixture below
///     returned <c>InvalidOperationException</c> — "This company holds multiple GST registrations, so a GST return
///     must name the registration it is filed for." The amendments screen and the 3B correction advisory were
///     therefore <b>unavailable on any path</b> for a multi-registration book.</item>
///   <item><b>Table 9C was the wrong money, and 9A's refusal MASKED it.</b> Scoping 9A alone would have made the
///     projection reachable with 9C still folding every registration's §34 notes into it — turning an honest
///     refusal into a silently wrong filed figure, which is strictly worse. That is why both are closed together
///     and why this file asserts the reachability half as well as the figure.</item>
/// </list></para>
///
/// <para>🔴 <b>THE RUPEE FIGURE.</b> Karnataka and Tamil Nadu each issue ONE §34 credit note in May against their
/// OWN April invoice. Karnataka's Table 9C must hold <b>one</b> row and its 3B prior-period correction must be
/// <b>−₹1,800.00</b>. Unscoped it holds <b>two</b> — the second naming Tamil Nadu's <c>TN/S/22</c> — and the
/// correction reads <b>−₹5,400.00</b>: ₹3,600.00 of another GSTIN's credit note claimed as Karnataka's correction,
/// a <b>200% overstatement</b>, while Tamil Nadu declares the same note in its own return. <b>One note, filed
/// twice, against two GSTINs.</b> On the GSTR-9 side Karnataka's Table 7C reads <b>₹1,050.00</b> unscoped against a
/// true <b>₹450.00</b> — and that one is <b>live on screen today</b>, because
/// <c>GstOfflineReturnsViewModel.ProjectGstr9</c> already passes a <c>ScopedRegistrationId</c> from a screen with a
/// registration picker.</para>
///
/// <para>🔴 <b>SOURCES (R7), OPENED BY CONTENT.</b>
/// <list type="bullet">
///   <item>TallyPrime's GSTR-1 report presents "<i>Amended Credit or Debit Notes (Registered) – 9C</i>" and
///     "<i>Amended Credit or Debit Notes (Unregistered) – 9C</i>" as sections of the same report whose registration
///     is chosen with "<i>Press F3 (Company/Tax Registration) and select the registration for which you want to
///     view the report</i>" — <c>help.tallysolutions.com/gstr-1-report-in-tallyprime/</c>. So 9C is per
///     registration.</item>
///   <item>⚠️ <b>AND THE SENTENCE THAT CUTS THE OTHER WAY, RECORDED RATHER THAN OMITTED.</b> The same page also
///     says "<i>If you had created multiple registrations, the report displays the combined GST details and
///     activities across registrations.</i>" That is the vendor's UNSCOPED landing view. This product does not
///     offer one — <c>EnsureRegistrationScoped</c> refuses it — a divergence already shipped at v61 and
///     deliberately not touched here. What both sentences agree on, and all this file asserts, is that once a
///     registration IS named the report is THAT registration's.</item>
///   <item>CGST Act §34(2) binds the other half: the duty to declare a credit note falls on the person "<i>who
///     issues</i>" it, so the filter must scope the note OUT of this return without dropping it from its own —
///     which is why <see cref="The_owning_registration_still_declares_its_own_amendment"/> exists.</item>
/// </list></para>
/// </summary>
public sealed class GstAmendmentRegistrationScopeTests
{
    private const string Karnataka = "29";
    private const string TamilNadu = "33";

    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly FyEnd = new(2026, 3, 31);
    private static readonly DateOnly InvoiceDate = new(2025, 4, 9);
    private static readonly DateOnly NoteDate = new(2025, 5, 14);
    private static readonly DateOnly ReversalDate = new(2025, 5, 20);
    private static readonly DateOnly From = new(2025, 5, 1);
    private static readonly DateOnly To = new(2025, 5, 31);

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

    /// <summary>The §34 note references — the strings that prove WHOSE document a 9C row is.</summary>
    private const string KaInvoiceReference = "KA/S/11";
    private const string TnInvoiceReference = "TN/S/22";

    internal sealed class Fixture
    {
        public required Company Company { get; init; }
        public required GstRegistration Branch { get; init; }
    }

    // ==============================================================================================================
    //  T2-123 — GSTR-1 Table 9C registration scope
    // ==============================================================================================================

    /// <summary>
    /// 🔴 <b>THE MEASURED DEFECT, ON THE FIGURE THAT GETS FILED.</b> Karnataka issued exactly one §34 credit note
    /// against its own April invoice, so its Table 9C holds exactly ONE row. Unscoped it held TWO, the second
    /// naming <c>TN/S/22</c> — the Tamil Nadu registration's own note, declared in Karnataka's return.
    /// </summary>
    [Fact]
    public void Karnataka_table_9C_holds_only_its_own_section_34_amendment()
    {
        var f = Build();

        var a = Gstr1Amendments.Build(f.Company, From, To, GstRegistration.PrimaryId);

        var row = Assert.Single(a.Table9C);                       // was 2 rows
        Assert.Equal(KaInvoiceReference, row.OriginalInvoiceNumber);
        Assert.Equal("CDNRA", row.SectionCode);
        Assert.Equal(-900m, row.RevisedCgst.Amount);
        Assert.Equal(-900m, row.RevisedSgst.Amount);
        Assert.Equal(0m, row.RevisedIgst.Amount);                 // Karnataka made no inter-State supply at all
        Assert.Equal(-10_000m, row.RevisedTaxableValue.Amount);

        // 🔴 The whole point: the other registration's reference is nowhere in this return.
        Assert.DoesNotContain(a.Table9C, r => r.OriginalInvoiceNumber == TnInvoiceReference);
    }

    /// <summary>
    /// 🔴 <b>THE NOTE IS NOT LOST — IT IS DECLARED EXACTLY ONCE, BY ITS ISSUER (§34(2)).</b> The defect was a
    /// DUPLICATE, so the fix is only correct if Tamil Nadu's own return still carries its own note. Asserted
    /// deliberately: a filter that scoped the note out of BOTH returns would trade a double declaration for a
    /// missing one, which breaches §34(2)'s first limb — a strictly different and equally filed-wrong outcome.
    /// </summary>
    [Fact]
    public void The_owning_registration_still_declares_its_own_amendment()
    {
        var f = Build();

        var a = Gstr1Amendments.Build(f.Company, From, To, f.Branch.Id);

        var row = Assert.Single(a.Table9C);
        Assert.Equal(TnInvoiceReference, row.OriginalInvoiceNumber);
        Assert.Equal(-3_600m, row.RevisedIgst.Amount);
        Assert.Equal(0m, row.RevisedCgst.Amount);
        Assert.Equal(0m, row.RevisedSgst.Amount);
        Assert.DoesNotContain(a.Table9C, r => r.OriginalInvoiceNumber == KaInvoiceReference);
    }

    /// <summary>
    /// 🔴 <b>THE DERIVED MONEY FIGURE, WHICH IS THE ONE A USER ACTS ON.</b> The 3B correction advisory is Σ the
    /// amendment tax, so an unscoped 9C moves it directly: Karnataka read <b>−5,400.00</b> against a true
    /// <b>−1,800.00</b>. Table 9A is asserted EMPTY on both so the figure is unambiguously 9C's — the May window
    /// holds only credit notes, so no B2B document is re-stated.
    /// </summary>
    [Fact]
    public void The_3B_prior_period_correction_is_scoped_to_the_registration_filing_it()
    {
        var f = Build();

        var ka = Gstr3bCorrectionAdvisory.Build(f.Company, From, To, GstRegistration.PrimaryId);
        var tn = Gstr3bCorrectionAdvisory.Build(f.Company, From, To, f.Branch.Id);

        Assert.Empty(Gstr1Amendments.Build(f.Company, From, To, GstRegistration.PrimaryId).Table9A);
        Assert.Empty(Gstr1Amendments.Build(f.Company, From, To, f.Branch.Id).Table9A);

        Assert.Equal(-1_800m, ka.PriorPeriodCorrectionTax.Amount);       // was -5,400.00
        Assert.Equal(-10_000m, ka.PriorPeriodCorrectionTaxable.Amount);  // was -30,000.00
        Assert.Equal(1, ka.CorrectionCount);                             // was 2

        Assert.Equal(-3_600m, tn.PriorPeriodCorrectionTax.Amount);       // was -5,400.00
        Assert.Equal(-20_000m, tn.PriorPeriodCorrectionTaxable.Amount);
        Assert.Equal(1, tn.CorrectionCount);

        // And the two registrations' corrections sum to the whole book's — nothing dropped, nothing duplicated.
        Assert.Equal(-5_400m, ka.PriorPeriodCorrectionTax.Amount + tn.PriorPeriodCorrectionTax.Amount);
    }

    /// <summary>
    /// 🔴 <b>THE REACHABILITY HALF, WHICH IS A SEPARATE DEFECT AND IS ASSERTED SEPARATELY.</b> Before this slice
    /// the whole projection THREW on a multi-registration book because Table 9A called <c>Gstr1.Build</c> with no
    /// registration. The probe that measured it recorded <c>InvalidOperationException</c>. A screen that cannot be
    /// opened is not a correct screen, so the fix is incomplete without this.
    /// </summary>
    [Fact]
    public void The_amendments_projection_is_REACHABLE_on_a_multi_registration_book()
    {
        var f = Build();

        Assert.Null(Record.Exception(() => Gstr1Amendments.Build(f.Company, From, To, GstRegistration.PrimaryId)));
        Assert.Null(Record.Exception(() => Gstr1Amendments.Build(f.Company, From, To, f.Branch.Id)));
        Assert.Null(Record.Exception(() => Gstr3bCorrectionAdvisory.Build(f.Company, From, To, f.Branch.Id)));

        // ⚠️ And the v61 refusal for a caller that still names NO registration is UNCHANGED — this slice scopes
        // the amendment tables, it does not loosen the guard that makes a wrong filed document impossible.
        Assert.Throws<InvalidOperationException>(() => Gstr1Amendments.Build(f.Company, From, To));
    }

    /// <summary>
    /// A <b>single</b>-registration book is byte-identical (ER-13): with one registration every voucher normalises
    /// to <c>GstRegistration.PrimaryId</c>, so both notes are that registration's own and BOTH must still be
    /// declared. Asserted so a filter that over-reached and dropped a registration's own rows is caught.
    /// </summary>
    [Fact]
    public void A_single_registration_book_declares_both_notes_and_is_unchanged()
    {
        var c = BuildSingleRegistrationBook();

        var unscoped = Gstr1Amendments.Build(c, From, To);
        var named = Gstr1Amendments.Build(c, From, To, GstRegistration.PrimaryId);

        Assert.Equal(2, unscoped.Table9C.Count);
        Assert.Equal(2, named.Table9C.Count);
        Assert.Equal(-5_400m, Gstr3bCorrectionAdvisory.Build(c, From, To).PriorPeriodCorrectionTax.Amount);
        // Naming the primary explicitly and leaving it null must agree exactly (the Guid.Empty/null normalisation).
        Assert.Equal(
            unscoped.Table9C.Select(r => r.OriginalInvoiceNumber),
            named.Table9C.Select(r => r.OriginalInvoiceNumber));
    }

    // ==============================================================================================================
    //  T2-123 SWEEP FIND — GSTR-9 Table 7 registration scope
    // ==============================================================================================================

    /// <summary>
    /// 🔴 <b>THE SWEEP FIND, AND IT IS LIVE ON SCREEN TODAY RATHER THAN LATENT.</b> <c>Gstr9.ReadTable7</c> walked
    /// <c>company.Vouchers</c> directly while every other leg of <c>Gstr9.Build</c> took the
    /// <c>registrationId</c> it is handed — so Karnataka's Table 7C read <b>₹1,050.00</b> (its own ₹450 plus Tamil
    /// Nadu's ₹600) against a true <b>₹450.00</b>. <c>GstOfflineReturnsViewModel.ProjectGstr9</c> passes a
    /// <c>ScopedRegistrationId</c> from a screen that HAS a registration picker and labels the figure "Table 7 ITC
    /// reversed", so no further plumbing was needed to reach it.
    /// </summary>
    [Fact]
    public void Gstr9_table_7_is_scoped_to_the_registration_filing_the_annual_return()
    {
        var f = Build();

        var ka = Gstr9.Build(f.Company, FyStart, FyEnd, GstRegistration.PrimaryId);
        var tn = Gstr9.Build(f.Company, FyStart, FyEnd, f.Branch.Id);

        Assert.Equal(450m, ka.Table7Rule42.Amount);          // was 1,350.00
        Assert.Equal(450m, ka.Table7ItcReversed.Amount);     // was 1,350.00
        Assert.Equal(900m, tn.Table7Rule42.Amount);          // was 1,350.00
        Assert.Equal(900m, tn.Table7ItcReversed.Amount);

        // Nothing dropped: the two registrations' Table 7 figures sum to the whole book's.
        Assert.Equal(1_350m, ka.Table7ItcReversed.Amount + tn.Table7ItcReversed.Amount);
    }

    /// <summary>
    /// 🔴 <b>THE ARTEFACT NO LONGER CONTRADICTS ITSELF, AND THIS IS THE ASSERTION THAT MATTERS MOST.</b>
    /// <c>ReadTable7</c>'s own summary promises its sub-buckets "sum to Σ the year's
    /// <c>Gstr3b.TotalItcReversed</c>", and <c>Gstr9.Build</c> computes those monthly 3B legs SCOPED
    /// (<c>Gstr3b.ReadReversals</c>, scoped as T1-64/T1-72). Unscoped, Table 7 could not foot to the Table 4(B)
    /// figures sitting beside it in the same return. Asserted against the 3B projection itself, not against a
    /// constant, so a future change to either side has to keep them agreeing.
    /// </summary>
    [Fact]
    public void Gstr9_table_7_foots_to_the_scoped_GSTR3B_table_4B_for_the_same_registration()
    {
        var f = Build();

        foreach (var reg in new[] { GstRegistration.PrimaryId, f.Branch.Id })
        {
            var g9 = Gstr9.Build(f.Company, FyStart, FyEnd, reg);

            decimal monthly = 0m;
            for (var m = FyStart; m <= FyEnd; m = m.AddMonths(1))
                monthly += Gstr3b.Build(f.Company, m, m.AddMonths(1).AddDays(-1), reg).TotalItcReversed.Amount;

            Assert.Equal(monthly, g9.Table7ItcReversed.Amount);
        }
    }

    /// <summary>
    /// 🔴 <b>ASSERTED ON THE EMITTED BYTES, NOT ON AN IN-MEMORY PROJECTION.</b> The filed artefact is what the
    /// portal reads, and nothing downstream will question it. <c>GstReturnJson.Gstr9</c> previously took no
    /// registration at all — one of the reachability failures T2-119 (ii) filed — so on a multi-registration book it
    /// could not be emitted; it is threaded here and the payload's <c>tbl7c_rule42_paisa</c> and <c>gstin</c> must
    /// name ONE registration consistently. Emitting a branch's figures under the primary's GSTIN would be a worse
    /// artefact than the refusal it replaces, so the GSTIN is asserted alongside the money.
    /// </summary>
    [Fact]
    public void The_emitted_GSTR9_payload_carries_only_this_registrations_table_7_reversal()
    {
        var f = Build();

        var kaJson = Encoding.UTF8.GetString(GstReturnJson.Gstr9(f.Company, FyStart, FyEnd, GstRegistration.PrimaryId));
        var tnJson = Encoding.UTF8.GetString(GstReturnJson.Gstr9(f.Company, FyStart, FyEnd, f.Branch.Id));

        Assert.Contains("\"tbl7c_rule42_paisa\": 45000", kaJson);       // was 135000
        Assert.Contains("\"tbl7_itc_reversed_paisa\": 45000", kaJson);
        Assert.Contains($"\"gstin\": \"{GstinPrimary}\"", kaJson);
        Assert.DoesNotContain("135000", kaJson);

        Assert.Contains("\"tbl7c_rule42_paisa\": 90000", tnJson);       // was 135000
        Assert.Contains("\"tbl7_itc_reversed_paisa\": 90000", tnJson);
        // 🔴 The branch's payload must name the BRANCH's GSTIN, not the primary's.
        Assert.Contains($"\"gstin\": \"{GstinBranch}\"", tnJson);
        Assert.DoesNotContain(GstinPrimary, tnJson);
    }

    /// <summary>
    /// ER-13 on the emitted bytes: a single-registration book's GSTR-9 payload is <b>byte-identical</b> whether the
    /// writer is called with no registration or with the primary named. This is the regression that a careless
    /// normalisation (or a <c>FindRegistration(null)</c> that stopped resolving to the primary) would break.
    /// </summary>
    [Fact]
    public void A_single_registration_GSTR9_payload_is_byte_identical_with_and_without_the_registration()
    {
        var c = BuildSingleRegistrationBook();

        Assert.Equal(
            GstReturnJson.Gstr9(c, FyStart, FyEnd),
            GstReturnJson.Gstr9(c, FyStart, FyEnd, GstRegistration.PrimaryId));
    }

    // ================================================================ fixture

    /// <summary>
    /// A Karnataka primary Regular registration, a Karnataka ISD beside it and a <b>Tamil Nadu</b> Regular branch —
    /// the three-registration shape <c>PurchaseReturnDirectionTests</c> established and
    /// <c>GstOutwardRegistrationScopeAndSignTests</c> reuses. Postings:
    /// <list type="bullet">
    ///   <item><b>April 2025 (a PRIOR filed period):</b> one ₹100,000 original invoice per registration —
    ///     Karnataka intra-State (CGST/SGST 9,000 each), Tamil Nadu inter-State (IGST 18,000).</item>
    ///   <item><b>May 2025 (the CURRENT window):</b> one LINKED §34 credit note per registration against ITS OWN
    ///     April invoice — Karnataka ₹10,000 (900 per head), Tamil Nadu ₹20,000 (IGST 3,600). Because the original
    ///     invoice is in a prior period, each is a cross-period CDN amendment ⇒ exactly one Table 9C row.</item>
    ///   <item><b>May 2025:</b> one Rule-42 ITC reversal per registration — Karnataka CGST ₹450, Tamil Nadu IGST
    ///     ₹600 — for the GSTR-9 Table 7 half.</item>
    /// </list>
    /// </summary>
    internal static Fixture Build()
    {
        var c = CompanyFactory.CreateSeeded("Multi GSTIN Amend Co", FyStart);
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

        Populate(c, gst, branch.Id);
        return new Fixture { Company = c, Branch = branch };
    }

    /// <summary>The same book with ONE registration, to hold ER-13: both notes become that registration's own and
    /// must be declared exactly as they were before the filter existed.</summary>
    internal static Company BuildSingleRegistrationBook()
    {
        var c = CompanyFactory.CreateSeeded("Single GSTIN Amend Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = Karnataka,
            Gstin = GstinPrimary,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        Populate(c, gst, null);
        return c;
    }

    private static void Populate(Company c, GstService gst, Guid? branchId)
    {
        var ledgers = new LedgerService(c);
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var reversalExpense = Add(c, "ITC Reversed (Indirect Expense)", "Indirect Expenses", true);
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

        // ---- April (a PRIOR filed period): the two original invoices.
        var kaInvoice = PostOutward(ledgers, gst, salesType, sales, debtorKa, 100_000m, interState: false,
            reverseSides: false, InvoiceDate, registrationId: null);
        var tnInvoice = PostOutward(ledgers, gst, salesType, sales, debtorTn, 100_000m, interState: true,
            reverseSides: false, InvoiceDate, registrationId: branchId);

        // ---- May (the CURRENT window): one linked §34 credit note per registration, each against ITS OWN April
        //      invoice. Each is a cross-period CDN amendment ⇒ exactly one Table 9C row in ITS OWN return.
        var kaNote = PostOutward(ledgers, gst, creditNoteType, sales, debtorKa, 10_000m, interState: false,
            reverseSides: true, NoteDate, registrationId: null);
        c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), kaNote, CdnType.Credit, kaInvoice, KaInvoiceReference, InvoiceDate,
            "01 sales return", is9BTarget: true));

        var tnNote = PostOutward(ledgers, gst, creditNoteType, sales, debtorTn, 20_000m, interState: true,
            reverseSides: true, NoteDate, registrationId: branchId);
        c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), tnNote, CdnType.Credit, tnInvoice, TnInvoiceReference, InvoiceDate,
            "01 sales return", is9BTarget: true));

        // ---- May: one Rule-42 ITC reversal per registration (the GSTR-9 Table 7 half). Karnataka reverses CGST
        //      ₹450 on a ₹5,000 base (9%); Tamil Nadu reverses IGST ₹900 on a ₹5,000 base (18%).
        PostRule42Reversal(c, gst, ledgers, reversalExpense, GstTaxHead.Central, 900, 5_000m, 450m, null);
        PostRule42Reversal(c, gst, ledgers, reversalExpense, GstTaxHead.Integrated, 1800, 5_000m, 900m, branchId);
    }

    /// <summary>
    /// Posts one Rule-42 ITC reversal as a Journal: Dr the reversal expense, Cr the Input tax ledger, with the tax
    /// leg tagged <c>GstAdjustmentKind.ReversalRule42</c> — the tag the GSTR-9 Table 7 and GSTR-3B Table 4(B)
    /// projections read. Tagged directly rather than through <c>GstReversalService</c> so the voucher's
    /// registration is what this test controls and nothing else moves.
    /// </summary>
    private static void PostRule42Reversal(
        Company c, GstService gst, LedgerService ledgers, Domain.Ledger expense, GstTaxHead head,
        int rateBasisPoints, decimal taxable, decimal amount, Guid? registrationId)
    {
        var inputLedger = gst.FindTaxLedger(head, GstTaxDirection.Input)!;
        var journalType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id;

        var lines = new List<EntryLine>
        {
            new(expense.Id, Money.FromRupees(amount), DrCr.Debit),
            new(inputLedger.Id, Money.FromRupees(amount), DrCr.Credit,
                gst: new GstLineTax(head, rateBasisPoints, Money.FromRupees(taxable),
                    adjustment: GstAdjustmentKind.ReversalRule42)),
        };

        var v = new Voucher(Guid.NewGuid(), journalType, ReversalDate, lines);
        if (registrationId is { } r) v.GstRegistrationId = r;
        ledgers.Post(v);
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
