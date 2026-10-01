using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>ROOT CAUSE: the sign a return document contributes to its own return side</b>
/// (<see cref="GstReportSupport.SignOf"/>).
///
/// <para><b>What was wrong, measured.</b> <see cref="GstReportSupport.DirectionOf"/> puts a Debit-Note base type on
/// the <b>inward</b> side, which is right — a purchase return IS an inward-side document. Nothing then asked which
/// WAY it pushes the figure. Three call sites hand-rolled <c>BaseType == CreditNote ? -1 : 1</c>, which is correct on
/// an Output sweep and a silent no-op on an Input sweep (the base types there are Purchase and Debit-Note, and
/// neither is a credit note). So every inward sweep ADDED a purchase return's tax to ITC.</para>
///
/// <para><b>The hand-computed arithmetic every test below asserts against.</b> The ISD buys a ₹50,000 input service
/// intra-Karnataka at 18% ⇒ CGST 4,500.00 + SGST 4,500.00 = ₹9,000.00 of ITC. It then returns ₹20,000 of that
/// service on a Debit Note ⇒ CGST 1,800.00 + SGST 1,800.00 = ₹3,600.00. The correct NET input tax is 18% of the
/// ₹30,000 retained = <b>₹5,400.00</b> (CGST 2,700.00 + SGST 2,700.00). Before the fix every inward report said
/// <b>₹12,600.00</b> (9,000 + 3,600) — 233% of the right figure, i.e. <b>133% overstated</b> — and GSTR-6
/// DISTRIBUTED that figure across real GSTINs with UndistributedCredit showing 0.00, so the return looked
/// perfectly footed while carrying more than twice the credit that existed.</para>
///
/// <para><b>Sources (R7), retrieved and read by content for this slice.</b> CGST Rule 39(1)(b) —
/// "<i>the amount of the credit distributed shall not exceed the amount of credit available for distribution</i>" —
/// at <c>taxinformation.cbic.gov.in/content/html/tax_repository/gst/rules/cgst_rules/active/chapter5/rule39_v1.00.html</c>.
/// A purchase return moves that cap DOWN.</para>
/// </summary>
public class PurchaseReturnDirectionTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly PrevFySale = new(2024, 6, 10);
    private static readonly DateOnly From = new(2025, 5, 1);
    private static readonly DateOnly To = new(2025, 5, 31);
    private static readonly DateOnly PurchaseDate = new(2025, 5, 12);
    private static readonly DateOnly ReturnDate = new(2025, 5, 20);

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
        public required Guid DebitNoteTypeId { get; init; }
        public required Guid SalesTypeId { get; init; }
        public required Guid CreditNoteTypeId { get; init; }
        public required Domain.Ledger Debtor { get; init; }
        public required Domain.Ledger Sales { get; init; }
        public required Voucher Purchase { get; init; }
    }

    /// <summary>
    /// A Karnataka operating registration, a Karnataka ISD beside it (§24(viii) compels the ISD registration
    /// "<i>whether or not separately registered</i>"), a Tamil Nadu branch, preceding-FY turnover 6,00,000 / 4,00,000
    /// so the pro rata is 60/40, and the ISD's ₹50,000 @ 18% input service posted in May-2025.
    /// </summary>
    private static Fixture Build()
    {
        var c = CompanyFactory.CreateSeeded("ISD Co", FyStart);
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
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtor = Add(c, "Debtor", "Sundry Debtors", true);
        var creditor = Add(c, "Service Supplier", "Sundry Creditors", false);
        creditor.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinSupplier, StateCode = Karnataka,
        };

        var services = Add(c, "Software Licence & Maintenance", "Indirect Expenses", true);

        var salesTypeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var purchaseTypeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
        var debitNoteTypeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.DebitNote).Id;
        var creditNoteTypeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.CreditNote).Id;

        PostSale(ledgers, salesTypeId, debtor, sales, 600_000m, null);
        PostSale(ledgers, salesTypeId, debtor, sales, 400_000m, branch.Id);

        var purchase = PostServiceLeg(
            ledgers, gst, purchaseTypeId, services, creditor, 50_000m, isd.Id, PurchaseDate);

        return new Fixture
        {
            Company = c, Isd = isd, Branch = branch, Ledgers = ledgers, Gst = gst,
            Services = services, Creditor = creditor, DebitNoteTypeId = debitNoteTypeId,
            SalesTypeId = salesTypeId, CreditNoteTypeId = creditNoteTypeId,
            Debtor = debtor, Sales = sales, Purchase = purchase,
        };
    }

    /// <summary>Posts an inward service leg (Purchase or Debit-Note base) of <paramref name="value"/> at 18% intra.</summary>
    private static Voucher PostServiceLeg(
        LedgerService ledgers, GstService gst, Guid typeId, Domain.Ledger services, Domain.Ledger creditor,
        decimal value, Guid registrationId, DateOnly date)
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

    /// <summary>The ₹20,000 purchase return on the seeded Debit Note voucher type ⇒ CGST 1,800 + SGST 1,800.</summary>
    private static Voucher PostPurchaseReturn(Fixture f) => PostServiceLeg(
        f.Ledgers, f.Gst, f.DebitNoteTypeId, f.Services, f.Creditor, 20_000m, f.Isd.Id, ReturnDate);

    /// <summary>
    /// 🔴 The SAME ₹20,000 purchase return, posted the way the <b>real voucher-entry screen</b> posts one:
    /// <c>VoucherEntryViewModel</c> passes <c>reverseSides: IsReturnNote</c> into
    /// <see cref="GstService.ComputeInvoiceTax"/>, so every tax leg of a return note lands on the OPPOSITE side —
    /// the input-tax leg is a <b>Credit</b>, not a Debit, and the party leg carries the Debit.
    ///
    /// <para><b>Why this second shape has to be tested and is not duplication.</b> The ordinary fixture above posts
    /// the return invoice-shaped, which is NOT what the application produces. If the sign had been derived from
    /// <c>DrCr</c> instead of from the base type, the invoice-shaped fixture would pass and the real screen's
    /// voucher would still be wrong — or the reverse. The sweeps read <see cref="GstLineTax"/> metadata and the
    /// line's <c>Amount</c> MAGNITUDE and never consult the side, so the netting must come out identical for both
    /// shapes. That is the property asserted, over the real UI's shape.</para>
    /// </summary>
    private static Voucher PostPurchaseReturnTheWayTheScreenDoes(Fixture f)
    {
        const decimal value = 20_000m;
        var tax = f.Gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(value), 1800) }, false, GstTaxDirection.Input,
            reverseSides: true);
        var gross = value + tax.TaxLines.Sum(l => l.Amount.Amount);

        // Dr Creditor (gross) = Cr Services (value) + Cr Input tax (the reversed tax legs).
        var lines = new List<EntryLine>
        {
            new(f.Creditor.Id, Money.FromRupees(gross), DrCr.Debit),
            new(f.Services.Id, Money.FromRupees(value), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);

        var v = new Voucher(Guid.NewGuid(), f.DebitNoteTypeId, ReturnDate, lines, partyId: f.Creditor.Id)
        {
            GstRegistrationId = f.Isd.Id,
        };
        f.Ledgers.Post(v);
        return v;
    }

    private static void PostSale(LedgerService ledgers, Guid salesTypeId, Domain.Ledger debtor,
        Domain.Ledger sales, decimal amount, Guid? registrationId)
    {
        var v = new Voucher(Guid.NewGuid(), salesTypeId, PrevFySale, new List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(amount), DrCr.Debit),
            new(sales.Id, Money.FromRupees(amount), DrCr.Credit),
        }, partyId: debtor.Id);
        if (registrationId is { } r) v.GstRegistrationId = r;
        ledgers.Post(v);
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    // ==============================================================================================================
    //  1. GSTR-6 — the measured money on a FILED return
    // ==============================================================================================================

    /// <summary>
    /// 🔴 <b>THE MEASURED DEFECT.</b> Received must be the NET input tax: 9,000.00 bought − 3,600.00 returned =
    /// 5,400.00. Before the fix this asserted 12,600.00 — 133% overstated.
    /// </summary>
    [Fact]
    public void A_purchase_return_debit_note_REDUCES_the_gstr6_credit_received_for_distribution()
    {
        var f = Build();
        PostPurchaseReturn(f);

        var r = Gstr6.Build(f.Company, From, To, f.Isd.Id);

        Assert.Equal(2_700m, r.ReceivedCgst.Amount);
        Assert.Equal(2_700m, r.ReceivedSgst.Amount);
        Assert.Equal(0m, r.ReceivedIgst.Amount);
        Assert.Equal(5_400m, r.TotalReceived.Amount);
    }

    /// <summary>
    /// 🔴 The overstatement did not stop at the received line — it was APPORTIONED ACROSS REAL GSTINs. 60% of the
    /// true 5,400.00 is Karnataka's 1,620.00 + 1,620.00 (its own State keeps the heads, Rule 39(1)(j)(i)); 40% is
    /// Tamil Nadu's 1,080.00 + 1,080.00 aggregated into IGST 2,160.00 (Rule 39(1)(j)(ii)). Before the fix the two
    /// rows carried 3,780.00 + 3,780.00 and IGST 5,040.00 — the 12,600.00 figure, split.
    /// </summary>
    [Fact]
    public void A_purchase_return_debit_note_REDUCES_what_each_recipient_gstin_is_told_to_claim()
    {
        var f = Build();
        PostPurchaseReturn(f);

        var r = Gstr6.Build(f.Company, From, To, f.Isd.Id);

        var karnataka = Assert.Single(r.Distribution, d => d.StateCode == Karnataka);
        var tamilNadu = Assert.Single(r.Distribution, d => d.StateCode == TamilNadu);

        Assert.Equal(1_620m, karnataka.Cgst.Amount);
        Assert.Equal(1_620m, karnataka.Sgst.Amount);
        Assert.Equal(0m, karnataka.Igst.Amount);

        Assert.Equal(0m, tamilNadu.Cgst.Amount);
        Assert.Equal(0m, tamilNadu.Sgst.Amount);
        Assert.Equal(2_160m, tamilNadu.Igst.Amount);

        // Rule 39(1)(b): distributed may not exceed available — and here it must EQUAL it, netted.
        Assert.Equal(5_400m, r.TotalDistributed.Amount);
        Assert.Equal(0m, r.UndistributedCredit.Amount);
    }

    /// <summary>
    /// The footing promise must hold with a return in the window: distributed + undistributed = received. This is the
    /// check the report's own Rule 39(1)(b) row exists to make, and the defect slipped past it because BOTH sides
    /// were inflated by the same 3,600.00.
    /// </summary>
    [Fact]
    public void The_gstr6_still_foots_when_a_purchase_return_nets_the_credit_down()
    {
        var f = Build();
        PostPurchaseReturn(f);

        var r = Gstr6.Build(f.Company, From, To, f.Isd.Id);

        Assert.Equal(
            r.TotalReceived.Amount,
            r.TotalDistributed.Amount + r.UndistributedCredit.Amount);
    }

    /// <summary>A book with no return at all is byte-identical (ER-13): the original 9,000.00 still distributes 60/40.</summary>
    [Fact]
    public void A_book_with_no_purchase_return_is_unchanged_by_the_sign()
    {
        var f = Build();

        var r = Gstr6.Build(f.Company, From, To, f.Isd.Id);

        Assert.Equal(9_000m, r.TotalReceived.Amount);
        Assert.Equal(9_000m, r.TotalDistributed.Amount);
        var karnataka = Assert.Single(r.Distribution, d => d.StateCode == Karnataka);
        Assert.Equal(2_700m, karnataka.Cgst.Amount);
    }

    /// <summary>
    /// A return LARGER than the credit received cannot be silently floored into a plausible-looking figure: the
    /// pools clamp at zero (<see cref="IsdDistribution"/> refuses a negative head — an ISD credit note under
    /// Rule 39(1)(l)/(n) is a different document), so the excess shows on the face of the return as a NEGATIVE
    /// undistributed figure with a diagnostic naming it, rather than being absorbed.
    /// </summary>
    [Fact]
    public void A_return_exceeding_the_credit_received_is_reported_not_absorbed()
    {
        var f = Build();
        // Return ₹60,000 of a ₹50,000 service ⇒ 10,800.00 of return tax against 9,000.00 received.
        PostServiceLeg(f.Ledgers, f.Gst, f.DebitNoteTypeId, f.Services, f.Creditor, 60_000m, f.Isd.Id, ReturnDate);

        var r = Gstr6.Build(f.Company, From, To, f.Isd.Id);

        Assert.Equal(-1_800m, r.TotalReceived.Amount);
        Assert.Equal(0m, r.TotalDistributed.Amount);
        Assert.Contains(r.Diagnostics, d => d.Contains("return", StringComparison.OrdinalIgnoreCase));
    }

    // ==============================================================================================================
    //  2. The ITC gate — T1-64, the sibling live on main
    // ==============================================================================================================

    /// <summary>
    /// 🔴 <b>T1-64, now measured.</b> The ITC gate accumulated a purchase-return Debit Note POSITIVELY, so the gate
    /// advised claiming 12,600.00 of books-eligible ITC where 5,400.00 existed — the same 133% overstatement, on the
    /// screen an operator uses to decide what to claim in GSTR-3B §4.
    /// </summary>
    [Fact]
    public void A_purchase_return_debit_note_REDUCES_books_eligible_itc_in_the_gate()
    {
        var f = Build();
        PostPurchaseReturn(f);

        var snapshot = new Gstr2bSnapshot(
            Guid.NewGuid(), GstStatementType.Gstr2b, "2025-05", GstinOperating, new DateOnly(2025, 6, 14),
            "HASH", DateTimeOffset.UnixEpoch, 0, 0, 0, 0, Array.Empty<Gstr2bLine>());

        var gate = ItcGateView.Build(f.Company, snapshot, From, To, f.Isd.Id);

        Assert.Equal(2_700m, gate.BooksEligible.Cgst.Amount);
        Assert.Equal(2_700m, gate.BooksEligible.Sgst.Amount);
        Assert.Equal(5_400m, gate.BooksEligibleTotal.Amount);
    }

    /// <summary>
    /// 🔴 <b>A SEPARATE DEFECT, FOUND BY THE MEASUREMENT ABOVE: the ITC gate dropped the registration scope twice,
    /// and on a multi-registration book that made it UNREACHABLE.</b>
    ///
    /// <para><c>ItcGateView.Build</c> takes a <c>registrationId</c> and uses it for its own inward sweep, but passed
    /// it to NEITHER <c>Gstr2bReconciler.Reconcile</c> nor <c>Gstr3b.Build</c>. For a single-registration book that
    /// was harmless — the unscoped set is the same set. For a book with more than one registration the unscoped
    /// sweep hits <c>GstReportSupport.EnsureRegistrationScoped</c> and throws "<i>a GST return must name the
    /// registration it is filed for</i>", so the gate could not be opened at all — and an ISD company has at least
    /// two registrations by construction, so every ISD company was affected. The second drop would also have
    /// compared ONE registration's books against EVERY registration's 3B claim, had it ever got that far.</para>
    /// </summary>
    [Fact]
    public void The_itc_gate_opens_for_a_multi_registration_company_and_scopes_every_leg_to_the_registration()
    {
        var f = Build();
        Assert.True(f.Company.Gst!.AllRegistrations.Count > 1, "The fixture must hold more than one registration.");

        var snapshot = new Gstr2bSnapshot(
            Guid.NewGuid(), GstStatementType.Gstr2b, "2025-05", GstinOperating, new DateOnly(2025, 6, 14),
            "HASH", DateTimeOffset.UnixEpoch, 0, 0, 0, 0, Array.Empty<Gstr2bLine>());

        // Before the fix this threw InvalidOperationException from EnsureRegistrationScoped.
        var isdGate = ItcGateView.Build(f.Company, snapshot, From, To, f.Isd.Id);

        // Scoped to the ISD: its ₹9,000 input service, and nothing from the operating or branch registrations.
        Assert.Equal(9_000m, isdGate.BooksEligibleTotal.Amount);

        // And the scope really is a scope — the Tamil Nadu branch bought nothing, so its gate is empty.
        var branchGate = ItcGateView.Build(f.Company, snapshot, From, To, f.Branch.Id);
        Assert.Equal(0m, branchGate.BooksEligibleTotal.Amount);

        // The "claimed in 3B" column must be the SAME registration's 3B, not the whole book's.
        Assert.Equal(0m, branchGate.Claimed3b.Total.Amount);
    }

    // ==============================================================================================================
    //  3. GSTR-3B §4 — the same root cause on the return that is actually filed monthly
    // ==============================================================================================================

    /// <summary>
    /// GSTR-3B Table 4(A)(5) "all other ITC" is read off the same inward sweep, so it carried the same inflation.
    /// Net ITC after the return is 2,700.00 + 2,700.00.
    /// </summary>
    [Fact]
    public void A_purchase_return_debit_note_REDUCES_gstr3b_table_4_itc()
    {
        var f = Build();
        PostPurchaseReturn(f);

        var r = Gstr3b.Build(f.Company, From, To, f.Isd.Id);

        Assert.Equal(2_700m, r.ItcCgst.Amount);
        Assert.Equal(2_700m, r.ItcSgst.Amount);
        Assert.Equal(0m, r.ItcIgst.Amount);
    }

    // ==============================================================================================================
    //  4. The §34 debit note on an OUTWARD supply must be EXCLUDED from the inward side, not negated
    // ==============================================================================================================

    /// <summary>
    /// 🔴 One base type, two documents. A §34(3) note revising UPWARD a supply made BY us has a Debit-Note base type
    /// — so <see cref="GstReportSupport.DirectionOf"/> maps it to Input — but it is an OUTWARD document, projected
    /// signed into the output buckets by its own table. On an inward sweep it must contribute <b>0</b>: negating it
    /// would reduce ITC by a figure that was never ITC. <see cref="GstReportSupport.SignOf"/> returns 0 for it, and
    /// -1 only for a note whose original is a Purchase (or which has no §34 link at all).
    /// </summary>
    [Fact]
    public void A_section34_debit_note_on_an_outward_supply_contributes_zero_to_the_inward_side()
    {
        var f = Build();
        var retn = PostPurchaseReturn(f);

        // The purchase return, posted with no §34 link: the §34 discriminator is absent, DirectionOf has already
        // placed it on the inward side, so it keeps that side's return meaning ⇒ a reduction of ITC.
        Assert.Equal(-1, GstReportSupport.SignOf(f.Company, retn, VoucherBaseType.DebitNote));

        // An upward §34 note on our own SALES invoice: same base type, but it is not an inward document at all.
        var sale = new Voucher(Guid.NewGuid(), f.SalesTypeId, PurchaseDate, new List<EntryLine>
        {
            new(f.Debtor.Id, Money.FromRupees(10_000m), DrCr.Debit),
            new(f.Sales.Id, Money.FromRupees(10_000m), DrCr.Credit),
        }, partyId: f.Debtor.Id);
        f.Ledgers.Post(sale);

        var upward = new Voucher(Guid.NewGuid(), f.DebitNoteTypeId, ReturnDate, new List<EntryLine>
        {
            new(f.Debtor.Id, Money.FromRupees(1_000m), DrCr.Debit),
            new(f.Sales.Id, Money.FromRupees(1_000m), DrCr.Credit),
        }, partyId: f.Debtor.Id);
        f.Ledgers.Post(upward);
        f.Company.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), upward.Id, CdnType.Debit, sale.Id, "INV-1", PurchaseDate, "Price revision"));

        Assert.Equal(0, GstReportSupport.SignOf(f.Company, upward, VoucherBaseType.DebitNote));
    }

    /// <summary>
    /// 🔴 <b>THE REAL SCREEN'S VOUCHER SHAPE.</b> The return posted the way <c>VoucherEntryViewModel</c> posts one
    /// (<c>reverseSides: true</c> ⇒ the input-tax legs are CREDITS) must net to the SAME ₹5,400.00 — on the engine,
    /// on the ITC gate and on GSTR-3B alike. This is the test that makes the fix's independence from <c>DrCr</c> a
    /// pinned property rather than an assumption: a sign derived from the posting side instead of the base type
    /// would pass the invoice-shaped fixture and fail here.
    /// </summary>
    [Fact]
    public void The_return_nets_identically_when_posted_with_the_reversed_sides_the_real_screen_uses()
    {
        var f = Build();
        var retn = PostPurchaseReturnTheWayTheScreenDoes(f);

        // The tax legs really are on the opposite side — otherwise this test is a duplicate of the one above.
        var taxLegs = retn.Lines.Where(l => l.Gst is not null).ToList();
        Assert.NotEmpty(taxLegs);
        Assert.All(taxLegs, l => Assert.Equal(DrCr.Credit, l.Side));

        // The sign still comes from the base type, not the side.
        Assert.Equal(-1, GstReportSupport.SignOf(f.Company, retn, VoucherBaseType.DebitNote));

        var r = Gstr6.Build(f.Company, From, To, f.Isd.Id);
        Assert.Equal(5_400m, r.TotalReceived.Amount);
        Assert.Equal(5_400m, r.TotalDistributed.Amount);
        Assert.Equal(0m, r.UndistributedCredit.Amount);

        var g3b = Gstr3b.Build(f.Company, From, To, f.Isd.Id);
        Assert.Equal(2_700m, g3b.ItcCgst.Amount);
        Assert.Equal(2_700m, g3b.ItcSgst.Amount);

        var snapshot = new Gstr2bSnapshot(
            Guid.NewGuid(), GstStatementType.Gstr2b, "2025-05", GstinOperating, new DateOnly(2025, 6, 14),
            "HASH", DateTimeOffset.UnixEpoch, 0, 0, 0, 0, Array.Empty<Gstr2bLine>());
        Assert.Equal(5_400m, ItcGateView.Build(f.Company, snapshot, From, To, f.Isd.Id).BooksEligibleTotal.Amount);
    }

    // ==============================================================================================================
    //  5. The 2B reconciler — the last unswept consumer
    // ==============================================================================================================

    /// <summary>
    /// 🔴 <b>A PURCHASE RETURN IS NOT A SUPPLIER INVOICE AND MUST NOT ENTER THE 2B BOOKS REGISTER.</b> The register
    /// is matched one-for-one against GSTR-2B invoice lines. With the return in it, our own debit note was offered
    /// to the greedy matcher as an inward invoice: it could MATCH and CONSUME the real 2B line (stranding the
    /// genuine invoice as <c>InBooksOnly</c>), or fall through as <c>InBooksOnly</c> itself and be surfaced to the
    /// operator as a §16(2)(aa) reversal candidate — advice to reverse credit the return had already removed.
    ///
    /// <para>The statutory reason it is EXCLUDED rather than signed: when a supply goes back, the SUPPLIER issues
    /// the credit note under CGST Act §34(1), and it is that supplier document which appears in our GSTR-2B. Our
    /// debit note is an internal record the portal never shows us, so it has no 2B counterpart to pair with.</para>
    ///
    /// <para>There was NO Debit-Note case anywhere in <c>Gstr2bReconcilerTests</c> or <c>ImsAndItcGateTests</c>,
    /// which is why this consumer stayed invisible while the accumulating reports were being fixed.</para>
    /// </summary>
    [Fact]
    public void A_purchase_return_is_not_offered_to_the_2b_matcher_as_an_inward_invoice()
    {
        var f = Build();
        PostPurchaseReturn(f);

        var snapshot = new Gstr2bSnapshot(
            Guid.NewGuid(), GstStatementType.Gstr2b, "2025-05", GstinOperating, new DateOnly(2025, 6, 14),
            "HASH", DateTimeOffset.UnixEpoch, 0, 0, 0, 0, Array.Empty<Gstr2bLine>());

        var recon = Gstr2bReconciler.Reconcile(
            f.Company, snapshot, From, To, ReconTolerance.Exact, f.Isd.Id);

        // The month holds exactly TWO inward vouchers — the ₹50,000 purchase and its ₹20,000 return — and exactly
        // ONE of them is an invoice. The 2B snapshot is empty, so the one invoice lands in InBooksOnly. Before the
        // fix the register carried BOTH and this count was 2.
        var only = Assert.Single(recon.InBooksOnly);
        Assert.Equal(f.Purchase.Id, only.VoucherId);

        // And the one entry is the INVOICE's own figures — ₹50,000 taxable, ₹9,000 tax — never the return's.
        Assert.Equal(5_000_000L, only.TaxableValuePaisa);
        Assert.Equal(900_000L, only.TotalTaxPaisa);

        // 🔴 And the return is therefore NOT advised for reversal. ReversalCandidates is what S7 posts from, so a
        // return listed here would reverse credit the netting had already removed — a double reduction in the books.
        Assert.Single(recon.ReversalCandidates);
        Assert.DoesNotContain(recon.ReversalCandidates, e => e.TaxableValuePaisa == 2_000_000L);
    }

    // ==============================================================================================================
    //  6. The MIRROR wrong-side defect: a credit note recording a PURCHASE return, on an OUTWARD sweep
    // ==============================================================================================================

    /// <summary>
    /// 🔴 <b>THE SECOND WRONG-SIDE DEFECT, AND IT MOVES CREDIT BETWEEN REAL GSTINs.</b> The §34 rule is symmetric,
    /// and only half of it existed. A Debit-Note base type lands on the INWARD side, so a §34(3) upward revision of
    /// our own sale had to be excluded from it — that half was handled. A Credit-Note base type lands on the
    /// OUTWARD side, so a note recording the SUPPLIER's credit note on a purchase (original = Purchase, the shape
    /// <c>ClassifyNoteDocument</c> titles "PURCHASE RETURN RECORD") must be excluded from the outward side — and it
    /// was not. All four outward sites hand-rolled <c>BaseType == CreditNote ? -1 : 1</c>, which returned −1 and so
    /// REDUCED an outward figure by an inward document.
    ///
    /// <para><b>The money, computed by hand.</b> The fixture's recipients have preceding-FY turnover ₹6,00,000
    /// (Karnataka) and ₹4,00,000 (Tamil Nadu) ⇒ Rule 39(1)(f) splits 60/40. Post a ₹1,00,000 credit note linked to
    /// a PURCHASE original under the Karnataka registration and the defect shrinks Karnataka's t1 to ₹5,00,000, so
    /// T becomes ₹9,00,000 and the split becomes 5/9 : 4/9. On the ₹9,000 of credit received that is Karnataka
    /// 9,000 × 5/9 = <b>₹5,000.00</b> (CGST 2,500 + SGST 2,500) and Tamil Nadu 9,000 × 4/9 = <b>₹4,000.00</b> of
    /// IGST — against the correct 60/40 of Karnataka <b>₹5,400.00</b> (2,700 + 2,700) and Tamil Nadu
    /// <b>₹3,600.00</b>. <b>₹400.00 of input tax credit is distributed to the wrong GSTIN</b>, and the return still
    /// foots perfectly to ₹9,000 — so no footing check could ever catch it.</para>
    ///
    /// <para>The same −1 also understates a composition dealer's taxable turnover (and so its tax payable) in
    /// <c>CompositionTaxService</c>, shrinks the Kerala flood-cess base, and shrinks GSTR-1 Table 4B's RCM outward
    /// value. Pre-existing on main, not introduced by this slice: centralising the rule in
    /// <see cref="GstReportSupport.SignOf"/> is what exposed it. No test covered the shape — every credit-note link
    /// in the suite points at a SALES original.</para>
    /// </summary>
    [Fact]
    public void A_credit_note_recording_a_purchase_return_does_not_shrink_the_outward_turnover_ratio()
    {
        var f = Build();

        // A ₹1,00,000 credit note under the Karnataka (primary) registration, linked to a PURCHASE original — the
        // supplier's credit note on our purchase, which we merely record. Dated in the relevant period (the
        // preceding financial year), which is what feeds the Rule 39(1)(f) turnover ratio.
        var note = new Voucher(Guid.NewGuid(), f.CreditNoteTypeId, PrevFySale, new List<EntryLine>
        {
            new(f.Sales.Id, Money.FromRupees(100_000m), DrCr.Debit),
            new(f.Debtor.Id, Money.FromRupees(100_000m), DrCr.Credit),
        }, partyId: f.Debtor.Id);
        f.Ledgers.Post(note);
        f.Company.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), note.Id, CdnType.Credit, f.Purchase.Id, "PINV-1", PurchaseDate, "01 purchase return"));

        // It is an INWARD document: it contributes nothing to an outward figure.
        Assert.Equal(0, GstReportSupport.SignOf(f.Company, note, VoucherBaseType.CreditNote));

        // And the 60/40 ratio therefore survives it. Before the fix: 2,500 / 2,500 and IGST 4,000.
        var r = Gstr6.Build(f.Company, From, To, f.Isd.Id);

        var karnataka = Assert.Single(r.Distribution, d => d.StateCode == Karnataka);
        Assert.Equal(2_700m, karnataka.Cgst.Amount);
        Assert.Equal(2_700m, karnataka.Sgst.Amount);

        var tamilNadu = Assert.Single(r.Distribution, d => d.StateCode == TamilNadu);
        Assert.Equal(3_600m, tamilNadu.Igst.Amount);

        // 🔴 The footing held perfectly THROUGHOUT the defect — ₹9,000 received, ₹9,000 distributed, nothing
        // undistributed, every paisa accounted for and ₹400.00 of it at the wrong GSTIN. This assertion is here to
        // document that a footing check is no guard against a mis-apportionment.
        Assert.Equal(9_000m, r.TotalReceived.Amount);
        Assert.Equal(9_000m, r.TotalDistributed.Amount);
        Assert.Equal(0m, r.UndistributedCredit.Amount);
    }

    /// <summary>
    /// A credit note linked to a SALES original keeps the outward side's return meaning (−1) — the positive control
    /// that proves the exclusion above discriminates on the ORIGINAL's side rather than just switching every linked
    /// credit note off.
    /// </summary>
    [Fact]
    public void A_credit_note_against_a_sales_original_still_reduces_the_outward_side()
    {
        var f = Build();

        var sale = new Voucher(Guid.NewGuid(), f.SalesTypeId, PrevFySale, new List<EntryLine>
        {
            new(f.Debtor.Id, Money.FromRupees(10_000m), DrCr.Debit),
            new(f.Sales.Id, Money.FromRupees(10_000m), DrCr.Credit),
        }, partyId: f.Debtor.Id);
        f.Ledgers.Post(sale);

        var note = new Voucher(Guid.NewGuid(), f.CreditNoteTypeId, PrevFySale, new List<EntryLine>
        {
            new(f.Sales.Id, Money.FromRupees(4_000m), DrCr.Debit),
            new(f.Debtor.Id, Money.FromRupees(4_000m), DrCr.Credit),
        }, partyId: f.Debtor.Id);
        f.Ledgers.Post(note);
        f.Company.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), note.Id, CdnType.Credit, sale.Id, "INV-9", PrevFySale, "01 sales return"));

        Assert.Equal(-1, GstReportSupport.SignOf(f.Company, note, VoucherBaseType.CreditNote));
    }

    /// <summary>
    /// The sign is answered in ONE place for BOTH sides — that is the whole point of the fix. A plain Purchase adds,
    /// a Sales adds, and each side's single return document reduces.
    /// </summary>
    [Fact]
    public void The_sign_is_plus_one_for_an_invoice_and_minus_one_for_its_sides_return_document()
    {
        var f = Build();

        Assert.Equal(1, GstReportSupport.SignOf(f.Company, f.Purchase, VoucherBaseType.Purchase));
        Assert.Equal(1, GstReportSupport.SignOf(f.Company, f.Purchase, VoucherBaseType.Sales));
        Assert.Equal(-1, GstReportSupport.SignOf(f.Company, f.Purchase, VoucherBaseType.CreditNote));
        Assert.Equal(-1, GstReportSupport.SignOf(f.Company, f.Purchase, VoucherBaseType.DebitNote));
        // A base type that never carries GST contributes nothing to either side.
        Assert.Equal(0, GstReportSupport.SignOf(f.Company, f.Purchase, VoucherBaseType.Journal));
        Assert.Equal(0, GstReportSupport.SignOf(f.Company, f.Purchase, VoucherBaseType.Payment));
    }
}
