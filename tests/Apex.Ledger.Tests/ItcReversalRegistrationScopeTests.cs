using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>ROOT CAUSE: an ITC reversal did not know which GST registration it belonged to, so it was posted under
/// one GSTIN and filed under every GSTIN.</b>
///
/// <para><b>What was wrong, measured in two places that compound.</b> (1) <c>GstReversalService</c> posted its
/// stat-adjustment Journal with no <see cref="Voucher.GstRegistrationId"/>, which
/// <see cref="GstReportSupport.RegistrationOf"/> normalises to the PRIMARY registration — so a branch's or an ISD's
/// reversal was recorded against the company's first GSTIN whatever registration it actually reduced the credit of.
/// (2) <c>Gstr3b.ReadReversals</c> was the ONE leg of <c>Gstr3b.Build</c> that took no <c>registrationId</c>, while
/// <c>ReadSide</c>, <c>ReadRcm</c>, <c>ReadCdn</c> and <c>ExemptOutwardValue</c> all took it — so it summed the
/// WHOLE BOOK's reversal-tagged lines into <b>every</b> registration's Table 4(B)/(D).</para>
///
/// <para><b>Why that is worse than a wrong report.</b> Table 4(B)(1) is a figure on a return that is FILED. One
/// ₹9,000.00 §17(5) reversal of the ISD registration's blocked input service appeared on the ISD's GSTR-3B, on the
/// operating registration's GSTR-3B and on the Tamil Nadu branch's GSTR-3B — the same reversal claimed three times
/// across three filings, and on two of them against a registration that had reversed nothing at all. A reversal
/// reduces exactly ONE electronic credit ledger; it cannot appear on more than one return.</para>
///
/// <para><b>The hand-computed arithmetic every test below asserts against.</b> The ISD registration buys a ₹50,000
/// input service intra-Karnataka at 18% ⇒ CGST 4,500.00 + SGST 4,500.00 = <b>₹9,000.00</b> of ITC. The service is
/// §17(5)-blocked, so the whole ₹9,000.00 is the gate's blocked candidate and the whole ₹9,000.00 is what posts.
/// The operating registration separately buys the same ₹50,000 service ⇒ its own ₹9,000.00. Those two figures are
/// what the scoping must keep apart.</para>
///
/// <para><b>Sources (R7), retrieved and read BY CONTENT for this slice.</b> CGST §39(1) —
/// "<i>Every registered person … shall, for every calendar month or part thereof, furnish a return, electronically,
/// of inward and outward supplies of goods or services or both, <b>input tax credit availed</b>, tax payable, tax
/// paid and such other particulars</i>" — at
/// <c>taxinformation.cbic.gov.in/content/html/tax_repository/gst/acts/2017_CGST_act/active/chapter9/section39_v1.00.html</c>,
/// which is what makes the return (and therefore its Table 4) a <b>per-registered-person</b> document; §39(4) gives
/// the ISD its own separate monthly return. CGST §16(1) — "<i>Every registered person shall, subject to such
/// conditions and restrictions as …</i>" — at
/// <c>…/2017_CGST_act/active/chapter5/section16_v1.00.html</c>, so the entitlement itself is per registered person.
/// No new vendor claim is made here; the per-registration architecture is the one the vendor documentation attests
/// (see <see cref="GstRegistration"/>).</para>
/// </summary>
public class ItcReversalRegistrationScopeTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly From = new(2025, 5, 1);
    private static readonly DateOnly To = new(2025, 5, 31);
    private static readonly DateOnly PurchaseDate = new(2025, 5, 12);
    private static readonly DateOnly ReturnDate = new(2025, 5, 20);
    private const string Period = "2025-05";

    private const string Karnataka = "29";
    private const string TamilNadu = "33";

    private static string GstinFor(string stateCode, string pan)
    {
        var body = stateCode + pan + "1Z";
        return body + Gstin.ComputeCheckDigit(body + "0");
    }

    private static readonly string GstinOperating = GstinFor(Karnataka, "AAPFU0939F");
    private static readonly string GstinIsd = GstinFor(Karnataka, "AAACC1206D");
    private static readonly string GstinBranch = GstinFor(TamilNadu, "AABCC1206D");
    private static readonly string GstinSupplier = GstinFor(Karnataka, "AAACS1206D");

    private sealed class Fixture
    {
        public required Company Company { get; init; }
        public required GstRegistration Isd { get; init; }
        public required GstRegistration Branch { get; init; }
        public required LedgerService Ledgers { get; init; }
        public required GstService Gst { get; init; }
        public required Domain.Ledger Services { get; init; }
        public required Domain.Ledger Creditor { get; init; }
        public required Guid PurchaseTypeId { get; init; }
        public required Guid DebitNoteTypeId { get; init; }
        public required Voucher IsdPurchase { get; init; }
        public required GstReversalService Reversal { get; init; }
    }

    /// <summary>
    /// A Karnataka operating registration (the company's own, <see cref="GstRegistration.PrimaryId"/>), a Karnataka
    /// ISD beside it and a Tamil Nadu branch — three registrations, so every scope in play is a real one — with the
    /// ISD's ₹50,000 @ 18% input service posted in May-2025.
    /// </summary>
    private static Fixture Build(bool blockedService = true)
    {
        var c = CompanyFactory.CreateSeeded("Reversal Scope Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = Karnataka,
            Gstin = GstinOperating,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var isd = new GstRegistration(
            Guid.NewGuid(), "Head Office (ISD)", Karnataka, GstinIsd,
            GstRegistrationType.InputServiceDistributor, FyStart);
        c.Gst!.AddRegistration(isd);

        var branch = new GstRegistration(
            Guid.NewGuid(), "Tamil Nadu Registration", TamilNadu, GstinBranch,
            GstRegistrationType.Regular, FyStart);
        c.Gst!.AddRegistration(branch);
        c.Gst!.EnsureValid();

        var ledgers = new LedgerService(c);
        var creditor = Add(c, "Service Supplier", "Sundry Creditors", false);
        creditor.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinSupplier, StateCode = Karnataka,
        };

        var services = Add(c, "Software Licence & Maintenance", "Indirect Expenses", true);
        if (blockedService)
            services.SalesPurchaseGst = new StockItemGstDetails
            {
                HsnSac = "9973", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
                ItcEligibility = ItcEligibility.BlockedSection17_5,
                BlockedCreditCategory = BlockedCreditCategory.MotorVehicles,
            };

        var purchaseTypeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
        var debitNoteTypeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.DebitNote).Id;

        var purchase = PostServiceLeg(ledgers, gst, purchaseTypeId, services, creditor, 50_000m, isd.Id, PurchaseDate);

        return new Fixture
        {
            Company = c, Isd = isd, Branch = branch, Ledgers = ledgers, Gst = gst,
            Services = services, Creditor = creditor, PurchaseTypeId = purchaseTypeId,
            DebitNoteTypeId = debitNoteTypeId, IsdPurchase = purchase,
            Reversal = new GstReversalService(c),
        };
    }

    private static Voucher PostServiceLeg(
        LedgerService ledgers, GstService gst, Guid typeId, Domain.Ledger services, Domain.Ledger creditor,
        decimal value, Guid? registrationId, DateOnly date)
    {
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(value), 1800) }, false, GstTaxDirection.Input);
        var gross = value + tax.TaxLines.Sum(l => l.Amount.Amount);
        var lines = new List<EntryLine>
        {
            new(services.Id, Money.FromRupees(value), DrCr.Debit),
            new(creditor.Id, Money.FromRupees(gross), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        var v = new Voucher(Guid.NewGuid(), typeId, date, lines, partyId: creditor.Id)
        {
            GstRegistrationId = registrationId,
        };
        ledgers.Post(v);
        return v;
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var group = c.Groups.First(g => g.Name == groupName);
        var l = new Domain.Ledger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>The empty 2B snapshot (no portal line ⇒ every booked purchase is <c>InBooksOnly</c>).</summary>
    private static Gstr2bSnapshot EmptySnapshot(string gstin) => new(
        Guid.NewGuid(), GstStatementType.Gstr2b, Period, gstin, new DateOnly(2025, 6, 14),
        "HASH", DateTimeOffset.UnixEpoch, 0, 0, 0, 0, Array.Empty<Gstr2bLine>());

    /// <summary>The ISD registration's §17(5)-blocked candidate — the whole ₹9,000.00 of the blocked service.</summary>
    private static ItcReversalCandidate BlockedCandidateOf(Fixture f, Guid registrationId)
    {
        var gate = ItcGateView.Build(f.Company, EmptySnapshot(GstinIsd), From, To, registrationId);
        return Assert.Single(gate.ReversalCandidates, x => x.Reason == ItcReversalReason.Section17_5Blocked);
    }

    // ==============================================================================================================
    //  1. THE FILED FIGURE — one reversal, one return
    // ==============================================================================================================

    /// <summary>
    /// 🔴 <b>THE MONEY TEST. A registration's ITC reversal must appear in ITS OWN GSTR-3B Table 4(B)(1) and in NO
    /// OTHER registration's.</b>
    ///
    /// <para>Hand-computed before measuring: the ISD's ₹50,000 input service at 18% intra-Karnataka carries
    /// CGST 4,500.00 + SGST 4,500.00 = ₹9,000.00 of ITC; the service is §17(5)-blocked, so the gate's blocked
    /// candidate is that whole ₹9,000.00 and that whole ₹9,000.00 is what <c>PostFromCandidate</c> posts.</para>
    ///
    /// <para><b>Before the fix</b> the ISD's 4(B)(1) read 4,500.00 / 4,500.00 — correct — AND the operating
    /// registration's read 4,500.00 / 4,500.00 and the Tamil Nadu branch's read 4,500.00 / 4,500.00, both of which
    /// had reversed nothing. The same ₹9,000.00 reversal was filed on three returns: ₹18,000.00 of credit reversal
    /// claimed that never happened.</para>
    /// </summary>
    [Fact]
    public void A_reversal_is_filed_in_its_OWN_registrations_gstr3b_table_4b_and_in_no_other()
    {
        var f = Build();
        var candidate = BlockedCandidateOf(f, f.Isd.Id);
        Assert.Equal(9_000m, candidate.SuggestedReversal.Amount);   // hand-computed: 4,500.00 + 4,500.00

        var posted = f.Reversal.PostFromCandidate(candidate, Period, To, registrationId: f.Isd.Id);
        Assert.NotNull(posted);
        Assert.Equal(450_000L, posted!.CgstPaisa);
        Assert.Equal(450_000L, posted.SgstPaisa);

        // The registration that reversed the credit — its own return carries it.
        var isd3b = Gstr3b.Build(f.Company, From, To, f.Isd.Id);
        Assert.Equal(4_500m, isd3b.ItcReversed4B1Cgst.Amount);
        Assert.Equal(4_500m, isd3b.ItcReversed4B1Sgst.Amount);

        // 🔴 And NO other registration's does. Both of these read 4,500.00 / 4,500.00 before the fix.
        var operating3b = Gstr3b.Build(f.Company, From, To, GstRegistration.PrimaryId);
        Assert.Equal(0m, operating3b.ItcReversed4B1Cgst.Amount);
        Assert.Equal(0m, operating3b.ItcReversed4B1Sgst.Amount);

        var branch3b = Gstr3b.Build(f.Company, From, To, f.Branch.Id);
        Assert.Equal(0m, branch3b.ItcReversed4B1Cgst.Amount);
        Assert.Equal(0m, branch3b.ItcReversed4B1Sgst.Amount);
    }

    /// <summary>
    /// 🔴 <b>THE POSTED ENTRY, not a report figure.</b> The stat-adjustment Journal the reversal posts must itself
    /// name the registration — that stamp is the whole attribution, and <c>null</c> (what it was) reads as the
    /// PRIMARY. Asserted on the voucher pulled back out of the books by the row's own
    /// <see cref="ItcReversal.ReversalVoucherId"/>, together with the Credit legs that reduce the credit pool.
    /// </summary>
    [Fact]
    public void The_posted_reversal_voucher_names_the_registration_whose_credit_it_reduces()
    {
        var f = Build();
        var posted = f.Reversal.PostFromCandidate(
            BlockedCandidateOf(f, f.Isd.Id), Period, To, registrationId: f.Isd.Id);

        var voucher = f.Company.FindVoucher(posted!.ReversalVoucherId);
        Assert.NotNull(voucher);

        // Before the fix this was null, i.e. the PRIMARY registration — a reversal of the ISD's credit recorded
        // against the operating GSTIN.
        Assert.Equal(f.Isd.Id, voucher!.GstRegistrationId);
        Assert.Equal(f.Isd.Id, GstReportSupport.RegistrationOf(voucher));

        // And it really is the reversal entry: ₹9,000.00 credited away from the input-tax heads.
        var creditedTax = voucher.Lines
            .Where(l => l.Gst is { Adjustment: not null } && l.Side == DrCr.Credit)
            .Sum(l => l.Amount.Amount);
        Assert.Equal(9_000m, creditedTax);
    }

    /// <summary>
    /// 🔴 <b>The registration is read from the SOURCE PURCHASE, not from what the caller says.</b> A reversal that
    /// names a source document cannot be mis-attributed by a wrong pick in a combo box: the engine reads the
    /// registration off that document. Here the caller deliberately names the BRANCH while handing over the ISD's
    /// purchase — the posting must follow the purchase.
    /// </summary>
    [Fact]
    public void A_reversal_follows_its_source_purchases_registration_even_when_the_caller_names_another()
    {
        var f = Build();
        var posted = f.Reversal.PostRule37(f.IsdPurchase.Id, Period, To);

        var voucher = f.Company.FindVoucher(posted.ReversalVoucherId)!;
        Assert.Equal(f.Isd.Id, voucher.GstRegistrationId);

        // Named the branch explicitly; the source purchase still decides.
        var candidate = BlockedCandidateOf(f, f.Isd.Id);
        var fromCandidate = f.Reversal.PostFromCandidate(
            candidate, Period, To, registrationId: f.Branch.Id);
        Assert.NotNull(fromCandidate);
        Assert.Equal(
            f.Isd.Id,
            f.Company.FindVoucher(fromCandidate!.ReversalVoucherId)!.GstRegistrationId);
    }

    // ==============================================================================================================
    //  2. THE IDEMPOTENCY KEY — two registrations, one period
    // ==============================================================================================================

    /// <summary>
    /// 🔴 <b>A SECOND REGISTRATION'S REVERSAL WAS SILENTLY SWALLOWED, NOT MERELY MIS-FILED.</b> Rule 42 apportions a
    /// POOL and so names no source document; the idempotency key was <c>(rule, period, source)</c> with source
    /// <c>null</c> for both. So once ANY registration had posted its May-2025 Rule-42 apportionment, every other
    /// registration's was handed back the first one's row and <b>nothing was written at all</b> — the second
    /// registration's reversal never existed, and its return under-reported the reversal it owed.
    ///
    /// <para>Hand-computed: C2 = ₹1,000.00 CGST with zero exempt turnover ⇒ D1 = (0 ÷ F) × C2 = 0.00 and
    /// D2 = 5% × 1,000.00 = <b>₹50.00</b>, per registration. Two registrations ⇒ two rows of ₹50.00, and ₹50.00 —
    /// never ₹100.00 and never nothing — on each one's own 4(B)(1).</para>
    /// </summary>
    [Fact]
    public void Two_registrations_each_post_their_OWN_rule_42_apportionment_for_the_same_period()
    {
        var f = Build();
        var basis = new GstReversalService.Rule42Basis(
            new GstReversalService.ReversalAmount(100_000L, 0L, 0L, 0L), ExemptTurnoverPaisa: 0L,
            TotalTurnoverPaisa: 1_000_000L);

        var isdRow = f.Reversal.PostRule42(Period, basis, To, registrationId: f.Isd.Id);
        var branchRow = f.Reversal.PostRule42(Period, basis, To, registrationId: f.Branch.Id);

        Assert.NotNull(isdRow);
        Assert.NotNull(branchRow);

        // Before the fix these were THE SAME ROW — the branch's apportionment was never posted.
        Assert.NotEqual(isdRow!.Id, branchRow!.Id);
        Assert.Equal(5_000L, isdRow.CgstPaisa);     // ₹50.00, hand-computed
        Assert.Equal(5_000L, branchRow.CgstPaisa);
        Assert.Equal(2, f.Company.ItcReversals.Count(r => r.Rule == ItcReversalRule.Rule42));

        // Each return carries its own ₹50.00 — not ₹100.00, and not zero.
        Assert.Equal(50m, Gstr3b.Build(f.Company, From, To, f.Isd.Id).ItcReversed4B1Cgst.Amount);
        Assert.Equal(50m, Gstr3b.Build(f.Company, From, To, f.Branch.Id).ItcReversed4B1Cgst.Amount);
        Assert.Equal(0m, Gstr3b.Build(f.Company, From, To, GstRegistration.PrimaryId).ItcReversed4B1Cgst.Amount);

        // A genuine re-run of the SAME registration's apportionment is still idempotent — the key gained a field,
        // it did not lose one.
        Assert.Equal(isdRow.Id, f.Reversal.PostRule42(Period, basis, To, registrationId: f.Isd.Id)!.Id);
    }

    // ==============================================================================================================
    //  3. THE ECRS CAP — a reclaim may not draw on another registration's reversals
    // ==============================================================================================================

    /// <summary>
    /// 🔴 <b>THE RECLAIM CAP WAS COMPUTED OVER THE WHOLE BOOK, so one registration could re-avail credit a DIFFERENT
    /// registration had reversed.</b> The ECRS is a portal statement per GSTIN and the portal hard-validates a Table
    /// 4(D)(1) reclaim against it, so the cap has to be that registration's own balance.
    ///
    /// <para>Hand-computed: the ISD's ₹50,000 service and the operating registration's ₹50,000 service each carry
    /// ₹9,000.00 of forward ITC (CGST 4,500.00 + SGST 4,500.00). A Rule-37 reversal of each gives a per-head
    /// whole-book ECRS of CGST 9,000.00 but a per-registration ECRS of CGST <b>4,500.00</b>. Reclaiming the ISD's
    /// row for CGST 9,000.00 therefore passed the old whole-book cap exactly — it drew the operating registration's
    /// reversal into the ISD's re-availment — and is refused now.</para>
    /// </summary>
    [Fact]
    public void A_reclaim_is_capped_against_its_OWN_registrations_ecrs_balance_not_the_whole_book()
    {
        var f = Build(blockedService: false);
        var operatingPurchase = PostServiceLeg(
            f.Ledgers, f.Gst, f.PurchaseTypeId, f.Services, f.Creditor, 50_000m,
            GstRegistration.PrimaryId, PurchaseDate);

        var isdReversal = f.Reversal.PostRule37(f.IsdPurchase.Id, Period, To);
        f.Reversal.PostRule37(operatingPurchase.Id, Period, To);

        // The two balances the fix separates: each registration's own 4,500.00 per head, not a shared 9,000.00.
        Assert.Equal(450_000L, f.Reversal.OutstandingReversalBalance(f.Isd.Id).CgstPaisa);
        Assert.Equal(450_000L, f.Reversal.OutstandingReversalBalance(GstRegistration.PrimaryId).CgstPaisa);
        Assert.Equal(900_000L, f.Reversal.OutstandingReversalBalance().CgstPaisa);

        // Reclaiming the ISD's row for the WHOLE BOOK's balance passed before the fix.
        var overReclaim = new GstReversalService.ReversalAmount(900_000L, 900_000L, 0L, 0L);
        var ex = Assert.Throws<InvalidOperationException>(
            () => f.Reversal.Reclaim(isdReversal.Id, Period, To, overReclaim));
        Assert.Contains("ECRS", ex.Message, StringComparison.Ordinal);

        // Its OWN ₹9,000.00 still reclaims, and the reclaim is recorded against the same registration.
        var reclaim = f.Reversal.Reclaim(isdReversal.Id, Period, To);
        Assert.Equal(450_000L, reclaim.CgstPaisa);
        Assert.Equal(f.Isd.Id, f.Company.FindVoucher(reclaim.ReversalVoucherId)!.GstRegistrationId);

        // 4(D)(1) is the ISD's alone.
        Assert.Equal(4_500m, Gstr3b.Build(f.Company, From, To, f.Isd.Id).ItcReclaimed4D1Cgst.Amount);
        Assert.Equal(0m, Gstr3b.Build(f.Company, From, To, GstRegistration.PrimaryId).ItcReclaimed4D1Cgst.Amount);
    }

    // ==============================================================================================================
    //  4. ER-13 — a single-registration book must be byte-identical
    // ==============================================================================================================

    /// <summary>
    /// <b>ER-13, asserted rather than assumed.</b> Every shipped book has exactly one registration, stores
    /// <c>null</c> in <c>vouchers.gst_registration_id</c>, and must keep storing it — a stamp of
    /// <see cref="GstRegistration.PrimaryId"/> (<c>Guid.Empty</c>) would be a silent data change on every book and
    /// a changed export for no gain, since <see cref="GstReportSupport.RegistrationOf"/> reads a null as the primary
    /// anyway. The reversal still files, unscoped and scoped alike.
    /// </summary>
    [Fact]
    public void A_single_registration_books_reversal_voucher_still_stores_no_registration_id()
    {
        var c = CompanyFactory.CreateSeeded("Single Reg Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = Karnataka,
            Gstin = GstinOperating,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        Assert.False(c.Gst!.IsMultiRegistration);

        var ledgers = new LedgerService(c);
        var creditor = Add(c, "Service Supplier", "Sundry Creditors", false);
        creditor.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinSupplier, StateCode = Karnataka,
        };
        var services = Add(c, "Software Licence & Maintenance", "Indirect Expenses", true);
        var purchase = PostServiceLeg(
            ledgers, gst, c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id,
            services, creditor, 50_000m, registrationId: null, PurchaseDate);

        var reversal = new GstReversalService(c);
        var row = reversal.PostRule37(purchase.Id, Period, To);

        Assert.Null(c.FindVoucher(row.ReversalVoucherId)!.GstRegistrationId);
        Assert.Equal(4_500m, Gstr3b.Build(c, From, To).ItcReversed4B2Cgst.Amount);
        Assert.Equal(4_500m, Gstr3b.Build(c, From, To, GstRegistration.PrimaryId).ItcReversed4B2Cgst.Amount);
    }

    // ==============================================================================================================
    //  5. THE GATE'S PER-POOL SHAPE — a sub-row may not be negative while its sister holds credit
    // ==============================================================================================================

    /// <summary>
    /// 🔴 <b>THE IDENTITY WAS RESTORED AND THE FACE WAS STILL IMPOSSIBLE.</b> Exact §34-link attribution subtracts a
    /// return's eligible share from the pool its OWN original fell into — the right pool — but the share is
    /// classified from the RETURN document and can exceed what that pool holds. The identity
    /// <c>BooksEligible = Claimable + NotInPortal</c> stayed true throughout, which is exactly why no identity test
    /// could see it.
    ///
    /// <para><b>Hand-computed before measuring.</b> Invoice A is the ISD's ₹50,000 service MATCHED in 2B ⇒ Claimable
    /// CGST 4,500.00 + SGST 4,500.00 = 9,000.00. Invoice B is a further ₹10,000 service with no 2B line ⇒
    /// NotInPortal 900.00 + 900.00 = 1,800.00. BooksEligible = 10,800.00. A §34-LINKED ₹60,000 return of A carries
    /// 5,400.00 + 5,400.00 = 10,800.00 and is attributed, correctly, to Claimable: 9,000.00 − 10,800.00 =
    /// <b>−1,800.00</b>, beside NotInPortal 1,800.00, under a BooksEligible of <b>0.00</b>. The screen rendered
    /// "of which §16(2)(aa)-claimable −1,800.00" and "of which not in 2B 1,800.00" beneath a total of 0.00 — a
    /// negative sub-row AND a sub-row larger than its own total.</para>
    ///
    /// <para>The deficit is now SPILLED onto the sister pool, never clamped: 0.00 / 0.00 under 0.00. Σ is invariant,
    /// so the identity is preserved exactly — a clamp would have invented 1,800.00 of credit.</para>
    /// </summary>
    [Fact]
    public void Exact_attribution_never_leaves_one_pool_negative_while_its_sister_still_holds_credit()
    {
        var f = Build(blockedService: false);
        PostServiceLeg(
            f.Ledgers, f.Gst, f.PurchaseTypeId, f.Services, f.Creditor, 10_000m, f.Isd.Id, PurchaseDate);

        var returnNote = PostServiceLeg(
            f.Ledgers, f.Gst, f.DebitNoteTypeId, f.Services, f.Creditor, 60_000m, f.Isd.Id, ReturnDate);
        f.Company.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), returnNote.Id, CdnType.Debit, f.IsdPurchase.Id,
            f.Company.FormatVoucherNumber(f.IsdPurchase), PurchaseDate, "01"));

        // 2B reflects ONLY invoice A, the one the return is linked to ⇒ its credit is the CLAIMABLE pool.
        var matching = new Gstr2bLine(
            Guid.NewGuid(), GstinSupplier, null, Gstr2bDocType.B2b,
            f.Company.FormatVoucherNumber(f.IsdPurchase),
            Gstr2bReconciler.NormaliseDocNo(f.Company.FormatVoucherNumber(f.IsdPurchase)),
            PurchaseDate, Karnataka,
            5_000_000L, 0L, 450_000L, 450_000L, 0L, true, null, false);
        var snapshot = new Gstr2bSnapshot(
            Guid.NewGuid(), GstStatementType.Gstr2b, Period, GstinIsd, new DateOnly(2025, 6, 14),
            "HASH", DateTimeOffset.UnixEpoch, 0, 0, 0, 0, new[] { matching });

        var gate = ItcGateView.Build(f.Company, snapshot, From, To, f.Isd.Id);

        Assert.Equal(0m, gate.BooksEligibleTotal.Amount);     // 10,800.00 − 10,800.00, unchanged by the spill
        Assert.Equal(0m, gate.ClaimableTotal.Amount);         // was −1,800.00
        Assert.Equal(0m, gate.NotInPortalTotal.Amount);       // was 1,800.00, beneath a total of 0.00

        // The identity is preserved EXACTLY — the spill moves the split, it never creates or destroys a paisa.
        Assert.Equal(
            gate.BooksEligibleTotal.Amount,
            gate.ClaimableTotal.Amount + gate.NotInPortalTotal.Amount);

        // No sub-row is negative while its sister holds credit, per head as well as in total.
        Assert.Equal(0m, gate.Claimable.Cgst.Amount);
        Assert.Equal(0m, gate.Claimable.Sgst.Amount);
        Assert.Equal(0m, gate.NotInPortal.Cgst.Amount);
        Assert.Equal(0m, gate.NotInPortal.Sgst.Amount);

        // And no sub-row exceeds its own total.
        Assert.True(gate.NotInPortalTotal.Amount <= gate.BooksEligibleTotal.Amount);
        Assert.True(gate.ClaimableTotal.Amount <= gate.BooksEligibleTotal.Amount);
    }
}
