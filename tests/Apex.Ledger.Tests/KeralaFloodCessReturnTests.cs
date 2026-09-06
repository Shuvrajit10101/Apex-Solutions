using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>Census row 6.26 — the Kerala Flood Cess RETURN projection, over a really-posted Kerala book.</b>
///
/// <para><see cref="KeralaFloodCessTests"/> pins the pure rate/limb/window engine against the Kerala GST
/// Department's own FAQ. This class pins the layer above it — <see cref="KeralaFloodCessReturnBuilder"/> — which
/// decides <b>which posted vouchers reach that engine</b>. That is where the money moves: the engine can be
/// perfectly correct and the return still wrong because the wrong supplies were swept into it.</para>
///
/// <para>Four of these tests guard a figure that a plausible simplification would silently break:</para>
/// <list type="number">
///   <item>🔴 <b>The 5% slab is reported as outside the schedules and bears nothing.</b> "1% on everything taxable"
///     is the natural wrong reading and it over-collects on the commonest slab in an Indian trading book.</item>
///   <item>🔴 <b>The window bounds the SWEEP, not just the rate.</b> A sale dated after the lapse must not reach the
///     return at all — a projection that swept it and multiplied by a zero rate would foot correctly today and
///     start over-collecting the moment anyone "tidied" the rate lookup.</item>
///   <item>🔴 <b>Turnover excluded as B2B is DISCLOSED, not dropped.</b> A filer must be able to see how much
///     turnover the single largest judgement in this projection removed.</item>
///   <item>🔴 <b>The slab is rounded ONCE.</b> Rounding per voucher and summing makes the answer depend on how the
///     same turnover happened to be split across invoices.</item>
/// </list>
/// </summary>
public sealed class KeralaFloodCessReturnTests
{
    // The levy's own first return period. Every fixture posts inside it unless a test says otherwise.
    private static readonly DateOnly FyStart = new(2019, 4, 1);
    private static readonly DateOnly PeriodFrom = new(2019, 8, 1);
    private static readonly DateOnly PeriodTo = new(2019, 8, 31);
    private static readonly DateOnly SaleDate = new(2019, 8, 10);

    private const string PanCompany = "AAPFU0939F";
    private const string PanBuyer = "AAQCS1234K";

    /// <summary>Builds a structurally valid GSTIN for a state code + PAN using the production check-digit rule, so a
    /// fixture never has to hardcode a checksum.</summary>
    private static string GstinFor(string stateCode, string pan)
    {
        var body = stateCode + pan + "1Z0";
        return body[..14] + Gstin.ComputeCheckDigit(body);
    }

    private static readonly string GstinKerala = GstinFor("32", PanCompany);
    private static readonly string GstinKeralaBuyer = GstinFor("32", PanBuyer);
    private static readonly string GstinTamilNadu = GstinFor("33", PanBuyer);

    // ─────────────────────────────────────────────────────────────────────── fixture

    private sealed class Book
    {
        public required Company Company { get; init; }
        public required GstService Gst { get; init; }
        public required LedgerService Ledgers { get; init; }
        public required Domain.Ledger Sales { get; init; }
        public required Guid SalesType { get; init; }
        public required Guid CreditNoteType { get; init; }
        public required Domain.Ledger Consumer { get; init; }
        public required Domain.Ledger KeralaDealer { get; init; }
        public required Domain.Ledger TamilNaduDealer { get; init; }
    }

    private static Book BuildKeralaBook(
        string homeStateCode = "32",
        GstRegistrationType registrationType = GstRegistrationType.Regular)
    {
        var gstin = homeStateCode == "32" ? GstinKerala : GstinFor(homeStateCode, PanCompany);
        var c = CompanyFactory.CreateSeeded("Kerala Flood Cess Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = homeStateCode,
            Gstin = gstin,
            RegistrationType = registrationType,
            // A Composition registration is invalid without a sub-type (GstConfig.EnsureValid), so the fixture
            // supplies one; the Kerala Flood Cess exemption in FAQ Q14 is on Composition itself, not on any sub-type.
            CompositionSubType = registrationType == GstRegistrationType.Composition
                ? Domain.CompositionSubType.Trader
                : null,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var sales = AddLedger(c, "Sales", "Sales Accounts", openingIsDebit: false);

        var consumer = AddLedger(c, "Walk-in Consumer", "Sundry Debtors", openingIsDebit: true);
        consumer.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Consumer,
            StateCode = homeStateCode,
        };

        var keralaDealer = AddLedger(c, "Kerala Dealer", "Sundry Debtors", openingIsDebit: true);
        keralaDealer.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular,
            Gstin = GstinKeralaBuyer,
            StateCode = "32",
        };

        var tnDealer = AddLedger(c, "Tamil Nadu Dealer", "Sundry Debtors", openingIsDebit: true);
        tnDealer.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular,
            Gstin = GstinTamilNadu,
            StateCode = "33",
        };

        return new Book
        {
            Company = c,
            Gst = gst,
            Ledgers = new LedgerService(c),
            Sales = sales,
            SalesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id,
            CreditNoteType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.CreditNote).Id,
            Consumer = consumer,
            KeralaDealer = keralaDealer,
            TamilNaduDealer = tnDealer,
        };
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>Posts an outward invoice of <paramref name="taxable"/> at <paramref name="rateBp"/> to
    /// <paramref name="party"/>, routed intra- or inter-State, and returns the posted voucher.</summary>
    private static Voucher PostSale(
        Book book, Domain.Ledger party, decimal taxable, int rateBp, DateOnly date, bool interState = false)
    {
        var tax = book.Gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(taxable), rateBp) },
            interState, GstTaxDirection.Output);

        var gross = Money.FromRupees(taxable) + tax.TotalTax;
        var lines = new List<EntryLine>
        {
            new(party.Id, gross, DrCr.Debit),
            new(book.Sales.Id, Money.FromRupees(taxable), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);

        return book.Ledgers.Post(new Voucher(
            Guid.NewGuid(), book.SalesType, date, lines, partyId: party.Id));
    }

    /// <summary>
    /// Posts a §34 <b>credit note</b> against <paramref name="original"/> through the real
    /// <see cref="CreditDebitNoteService"/>, so its tax legs carry genuine <c>GstLineTax</c> blocks on the reversed
    /// sides — which is what <see cref="GstReportSupport.PostedForwardRouting"/> reads.
    ///
    /// <para>🔴 <b>Written this way after a measured failure, and the failure is worth recording.</b> The first
    /// version of this helper hand-built the reversal with <c>new EntryLine(l.LedgerId, l.Amount, flipped)</c>. That
    /// compiles, posts, and balances — but the <c>EntryLine</c> constructor's <c>gst</c> parameter is optional, so
    /// the flipped legs carried <b>no GST block at all</b>. <c>PostedForwardRouting</c> then answered <c>null</c>
    /// ("this voucher posted no forward tax"), the projection skipped the note entirely, and the credit note
    /// silently failed to reduce the turnover. A hand-rolled reversal is not a credit note.</para>
    /// </summary>
    private static void PostCreditNote(Book book, Voucher original, Domain.Ledger party, decimal taxable, int rateBp,
        DateOnly date, bool interState = false)
    {
        var svc = new CreditDebitNoteService(book.Company);
        var noteId = Guid.NewGuid();
        var posting = svc.BuildCreditDebitNote(
            CdnType.Credit, new[] { new GstService.TaxableLine(Money.FromRupees(taxable), rateBp) },
            interState, noteId, original.Id, "INV-KFC-1", original.Date, date, reasonCode: "01 sales return");

        var total = new Money(taxable + posting.Computed.TotalTax.Amount);
        var lines = new List<EntryLine>
        {
            new(book.Sales.Id, Money.FromRupees(taxable), DrCr.Debit),
            new(party.Id, total, DrCr.Credit),
        };
        lines.AddRange(posting.TaxLines);

        book.Ledgers.Post(new Voucher(noteId, book.CreditNoteType, date, lines, partyId: party.Id));
    }

    private static KeralaFloodCessReturn BuildReturn(Book book) =>
        KeralaFloodCessReturnBuilder.Build(book.Company, PeriodFrom, PeriodTo);

    // ─────────────────────────────────────────────────────────── the core leviable case

    /// <summary>
    /// An ordinary intra-Kerala B2C sale of ₹1,00,000 at 18% inside the levy window is leviable at <b>1%</b>
    /// ([KFC-FAQ] Q5/Q19), on the GST-EXCLUSIVE value (Q10) — so the cess is ₹1,000.00, not ₹1,180.00's 1%.
    /// </summary>
    [Fact]
    public void An_intra_Kerala_B2C_sale_inside_the_window_is_leviable_at_one_percent_on_the_taxable_value()
    {
        var book = BuildKeralaBook();
        PostSale(book, book.Consumer, 1_00_000m, 1800, SaleDate);

        var ret = BuildReturn(book);

        var slab = Assert.Single(ret.Slabs);
        Assert.Equal(1800, slab.GstRateBasisPoints);
        Assert.Equal(KfcRateLimb.Standard, slab.Limb);
        Assert.Equal(100, slab.CessRateBasisPoints);
        Assert.Equal(1_00_000m, slab.Turnover.Amount);
        Assert.Equal(1_000.00m, slab.Cess.Amount);

        Assert.Equal(1_00_000m, ret.TotalTurnover.Amount);
        Assert.Equal(1_000.00m, ret.TotalCess.Amount);
        Assert.False(ret.IsEmpty);
        Assert.True(ret.PeriodIntersectsTheLevy);

        // The GST-inclusive base would have been ₹1,18,000 ⇒ ₹1,180. Named, so the figure above is not a coincidence.
        Assert.NotEqual(1_180.00m, ret.TotalCess.Amount);
    }

    /// <summary>The 0.25% limb: Schedule V of S.R.O. 360/2017 — the 3% GST slab, "gold, diamond etc." ([KFC-FAQ] Q5).</summary>
    [Fact]
    public void A_three_percent_gold_sale_is_leviable_at_the_quarter_percent_limb()
    {
        var book = BuildKeralaBook();
        PostSale(book, book.Consumer, 4_00_000m, 300, SaleDate);

        var slab = Assert.Single(BuildReturn(book).Slabs);
        Assert.Equal(KfcRateLimb.Reduced, slab.Limb);
        Assert.Equal(25, slab.CessRateBasisPoints);
        Assert.Equal(1_000.00m, slab.Cess.Amount);   // 0.25% of ₹4,00,000
    }

    // ─────────────────────────────────────────────────────────── the window bounds the SWEEP

    /// <summary>
    /// 🔴 <b>THE LAPSE LOCK AT PROJECTION LEVEL.</b> The identical sale, dated one day after the levy lapsed, must
    /// produce a return with <b>no slabs at all</b> and a period that does not intersect the levy — not a slab of
    /// ₹0. The distinction is the whole safety property: if the sweep ignored the window and relied on the rate
    /// being zero, the return would foot correctly today and start over-collecting the moment the rate lookup was
    /// "simplified".
    /// </summary>
    [Fact]
    public void The_same_sale_after_the_lapse_produces_a_return_with_no_slabs_and_no_levy_period()
    {
        var book = BuildKeralaBook();
        var after = new DateOnly(2021, 8, 10);
        PostSale(book, book.Consumer, 1_00_000m, 1800, after);

        var ret = KeralaFloodCessReturnBuilder.Build(
            book.Company, new DateOnly(2021, 8, 1), new DateOnly(2021, 8, 31));

        Assert.Empty(ret.Slabs);
        Assert.True(ret.IsEmpty);
        Assert.False(ret.PeriodIntersectsTheLevy);
        Assert.Null(ret.LevyFrom);
        Assert.Null(ret.LevyTo);
        Assert.Equal(0m, ret.TotalCess.Amount);
    }

    /// <summary>
    /// A period straddling the cessation date is <b>clipped</b> to the levy: a July-2021 sale is on the return, an
    /// August-2021 sale is not, even though both fall inside the requested range.
    /// </summary>
    [Fact]
    public void A_period_straddling_the_lapse_reports_only_the_days_the_levy_covered()
    {
        var book = BuildKeralaBook();
        PostSale(book, book.Consumer, 50_000m, 1800, new DateOnly(2021, 7, 20));   // inside
        PostSale(book, book.Consumer, 90_000m, 1800, new DateOnly(2021, 8, 20));   // outside

        var ret = KeralaFloodCessReturnBuilder.Build(
            book.Company, new DateOnly(2021, 7, 1), new DateOnly(2021, 8, 31));

        Assert.Equal(new DateOnly(2021, 7, 1), ret.LevyFrom);
        Assert.Equal(new DateOnly(2021, 7, 31), ret.LevyTo);   // clipped to the cessation date
        Assert.Equal(50_000m, ret.TotalTurnover.Amount);       // the August sale never entered
        Assert.Equal(500.00m, ret.TotalCess.Amount);
    }

    // ─────────────────────────────────────────────────────────── the over-collection lock

    /// <summary>
    /// 🔴 <b>THE OVER-COLLECTION LOCK, AND THE RUPEES IT GUARDS.</b> Schedule I of S.R.O. 360/2017 is 2.5% State tax
    /// = the <b>5% GST</b> slab, and [KFC-FAQ] Q5 names Schedules II, III and IV in the 1% limb and the Fifth in the
    /// 0.25% limb — Schedule I is in <b>neither</b>. So a ₹10,00,000 sale of 5% goods bears <b>no</b> cess, and the
    /// projection reports that turnover in its own disclosure line rather than dropping it.
    ///
    /// <para>The wrong reading ("1% on everything taxable") would collect ₹10,000.00 that was never levied. This
    /// test fails by exactly that amount.</para>
    /// </summary>
    [Fact]
    public void A_five_percent_sale_bears_no_cess_and_is_disclosed_as_outside_the_schedules()
    {
        var book = BuildKeralaBook();
        PostSale(book, book.Consumer, 10_00_000m, 500, SaleDate);

        var ret = BuildReturn(book);

        Assert.Empty(ret.Slabs);
        Assert.Equal(0m, ret.TotalCess.Amount);
        Assert.Equal(0m, ret.TotalTurnover.Amount);
        Assert.Equal(10_00_000m, ret.TurnoverOutsideTheSchedules.Amount);   // seen, considered, correctly nil
        Assert.NotEqual(10_000.00m, ret.TotalCess.Amount);
    }

    // ─────────────────────────────────────────────────────────── the B2B exclusion, disclosed

    /// <summary>
    /// 🔴 A sale to a <b>Kerala-registered</b> buyer is excluded ([KFC-FAQ] Q12/Q21) — and the excluded turnover is
    /// reported in its own figure, never silently dropped. A filer who cannot see how much turnover the exclusion
    /// removed cannot check it.
    /// </summary>
    [Fact]
    public void A_sale_to_a_Kerala_registered_buyer_is_excluded_AND_disclosed()
    {
        var book = BuildKeralaBook();
        PostSale(book, book.KeralaDealer, 7_00_000m, 1800, SaleDate);

        var ret = BuildReturn(book);

        Assert.Empty(ret.Slabs);
        Assert.Equal(0m, ret.TotalCess.Amount);
        Assert.Equal(7_00_000m, ret.ExemptedBusinessTurnover.Amount);
    }

    /// <summary>
    /// 🔴 <b>THE OTHER HALF OF Q21, WHICH "B2B IS EXEMPT" LOSES.</b> "Exemption is eligible only for registered
    /// taxable person having GST registration <b>in Kerala</b> GST." A buyer registered in Tamil Nadu buying
    /// intra-Kerala (the goods do not leave the State, so the supply posted CGST+SGST) is <b>leviable</b> — it is
    /// not an exclusion, and it is not inter-State either.
    /// </summary>
    [Fact]
    public void A_buyer_registered_outside_Kerala_does_not_get_the_business_exemption()
    {
        var book = BuildKeralaBook();
        PostSale(book, book.TamilNaduDealer, 2_00_000m, 1800, SaleDate, interState: false);

        var ret = BuildReturn(book);

        Assert.Equal(2_00_000m, ret.TotalTurnover.Amount);
        Assert.Equal(2_000.00m, ret.TotalCess.Amount);
        Assert.Equal(0m, ret.ExemptedBusinessTurnover.Amount);
    }

    // ───────────────────────────────── Q21: the GSTIN decides, not the party's State field

    /// <summary>
    /// 🔴 <b>WHERE THE TWO FACTS DISAGREE, THE GSTIN WINS — AND UNTIL THIS TEST NOTHING ENFORCED IT.</b>
    /// <see cref="KeralaFloodCessReturnBuilder.IsKeralaRegisteredBuyer"/> documents in bold that the buyer's
    /// Kerala-ness is read off the GSTIN's own leading two digits and NOT off
    /// <see cref="PartyGstDetails.StateCode"/>, because [KFC-FAQ] Q21 turns on <b>where the registration is</b>
    /// while the State field is the place-of-supply driver. The two normally agree, so every fixture in this class
    /// had them agreeing and the documented rule was untested: swapping the implementation to read
    /// <c>pg.StateCode</c> would have left the whole suite green.
    ///
    /// <para>This is the exemption direction: a <b>Kerala</b> GSTIN (32…) on a party whose State field says Tamil
    /// Nadu (33). Q21 is satisfied by the registration, so the supply is excluded and disclosed. Read
    /// <c>pg.StateCode</c> instead and this reddens — the sale becomes leviable and ₹3,000.00 of cess is invented
    /// on a buyer who is in fact exempt.</para>
    /// </summary>
    [Fact]
    public void A_Kerala_GSTIN_earns_the_exemption_even_when_the_party_State_field_says_otherwise()
    {
        var book = BuildKeralaBook();
        var mismatched = AddLedger(book.Company, "Kerala GSTIN, TN state field", "Sundry Debtors", openingIsDebit: true);
        mismatched.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular,
            Gstin = GstinKeralaBuyer,   // 32… — registered in Kerala
            StateCode = "33",           // …but the place-of-supply field says Tamil Nadu
        };

        PostSale(book, mismatched, 3_00_000m, 1800, SaleDate, interState: false);

        var ret = BuildReturn(book);

        Assert.True(KeralaFloodCessReturnBuilder.IsKeralaRegisteredBuyer(mismatched));
        Assert.Empty(ret.Slabs);
        Assert.Equal(0m, ret.TotalCess.Amount);
        Assert.Equal(3_00_000m, ret.ExemptedBusinessTurnover.Amount);
    }

    /// <summary>
    /// 🔴 <b>THE MIRROR, AND THE ONE THAT COSTS MONEY IF IT IS WRONG.</b> A <b>Tamil Nadu</b> GSTIN (33…) on a party
    /// whose State field says Kerala (32). The registration is not a Kerala one, so Q21's exemption is NOT available
    /// however the State field reads, and the intra-Kerala supply stays leviable at 1%. An implementation that
    /// trusted the State field would exempt this buyer and <b>under-collect</b> ₹4,000.00 — a filer's shortfall.
    /// </summary>
    [Fact]
    public void A_non_Kerala_GSTIN_is_leviable_even_when_the_party_State_field_says_Kerala()
    {
        var book = BuildKeralaBook();
        var mismatched = AddLedger(book.Company, "TN GSTIN, Kerala state field", "Sundry Debtors", openingIsDebit: true);
        mismatched.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular,
            Gstin = GstinTamilNadu,     // 33… — registered in Tamil Nadu
            StateCode = "32",           // …but the place-of-supply field says Kerala
        };

        PostSale(book, mismatched, 4_00_000m, 1800, SaleDate, interState: false);

        var ret = BuildReturn(book);

        Assert.False(KeralaFloodCessReturnBuilder.IsKeralaRegisteredBuyer(mismatched));
        Assert.Single(ret.Slabs);
        Assert.Equal(4_00_000m, ret.TotalTurnover.Amount);
        Assert.Equal(4_000.00m, ret.TotalCess.Amount);
        Assert.Equal(0m, ret.ExemptedBusinessTurnover.Amount);
    }

    /// <summary>
    /// 🔴 <b>PINS WHAT THE STATE FIELD CANNOT DO: RESCUE A PARTY THAT HOLDS NO GSTIN.</b> This method's own doc
    /// used to claim the State field served as "a fall-back for a party carrying a registration type but no GSTIN
    /// string" — <b>that branch cannot execute</b>. <see cref="PartyGstDetails.IsB2C"/> is true whenever the GSTIN
    /// is null or blank, so such a party is rejected by the B2C guard on the first line and never reaches the
    /// fall-back. The doc has been corrected; this test locks the behaviour it now describes.
    ///
    /// <para>And the behaviour is the right one: Q21 grants the exemption to a "registered taxable person having GST
    /// registration in Kerala GST". A buyer with a Kerala address but no registration is not that person — it is the
    /// unregistered buyer of Q19, and its supply is <b>leviable</b>. Falling back to the State field here would
    /// exempt every walk-in Kerala buyer and gut the levy.</para>
    /// </summary>
    [Fact]
    public void A_party_with_a_Kerala_State_field_but_no_GSTIN_gets_no_exemption()
    {
        var book = BuildKeralaBook();
        var noGstin = AddLedger(book.Company, "Kerala address, unregistered", "Sundry Debtors", openingIsDebit: true);
        noGstin.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Unregistered,
            Gstin = null,
            StateCode = "32",           // Kerala — but there is no registration to be Kerala-registered under
        };

        PostSale(book, noGstin, 1_00_000m, 1800, SaleDate, interState: false);

        var ret = BuildReturn(book);

        Assert.False(KeralaFloodCessReturnBuilder.IsKeralaRegisteredBuyer(noGstin));
        Assert.Equal(1_000.00m, ret.TotalCess.Amount);
        Assert.Equal(0m, ret.ExemptedBusinessTurnover.Amount);
    }

    /// <summary>[KFC-FAQ] Q11 — "Kerala Flood Cess is applicable only for intra-state supply." An IGST sale never
    /// enters the return, and it is not counted as an exclusion either (it was never in scope).</summary>
    [Fact]
    public void An_inter_state_sale_never_enters_the_return()
    {
        var book = BuildKeralaBook();
        PostSale(book, book.TamilNaduDealer, 3_00_000m, 1800, SaleDate, interState: true);

        var ret = BuildReturn(book);

        Assert.Empty(ret.Slabs);
        Assert.Equal(0m, ret.TotalCess.Amount);
        Assert.Equal(0m, ret.ExemptedBusinessTurnover.Amount);
        Assert.Equal(0m, ret.TurnoverOutsideTheSchedules.Amount);
    }

    // ─────────────────────────────────────────────────────────── supplier-side gates

    /// <summary>A supplier registered outside Kerala files no Kerala Flood Cess return ([KFC-FAQ] Q11/Q21) — even
    /// on an intra-State B2C sale in its own State.</summary>
    [Fact]
    public void A_company_registered_outside_Kerala_files_nothing()
    {
        var book = BuildKeralaBook(homeStateCode: "27");   // Maharashtra
        PostSale(book, book.Consumer, 5_00_000m, 1800, SaleDate);

        var ret = BuildReturn(book);

        Assert.Empty(ret.Slabs);
        Assert.True(ret.IsEmpty);
        Assert.Equal(0m, ret.TotalCess.Amount);
        Assert.False(KeralaFloodCessReturnBuilder.IsKeralaRegisteredSupplier(book.Company));
    }

    /// <summary>[KFC-FAQ] Q14 — "Composition tax payers are exempted from the levy of Kerala Flood Cess." A Kerala
    /// composition dealer files nothing however its book reads.
    ///
    /// <para>⚠️ <b>This test does NOT reach the builder's Composition guard, and says so rather than pretending
    /// otherwise.</b> A composition company issues a Bill of Supply: <see cref="GstService.ComputeInvoiceTax"/>
    /// suppresses every forward tax line for it, so the fixture's sale posts no GST leg,
    /// <c>PostedForwardRouting</c> answers <c>null</c>, and the voucher is skipped as exempt/nil long before the
    /// guard matters. Deleting the guard entirely leaves this test green (measured). It still earns its place as
    /// the end-to-end statement of Q14 — but the guard itself is pinned by
    /// <see cref="A_book_that_switched_to_composition_files_nothing_even_on_vouchers_that_posted_forward_tax"/>.</para>
    /// </summary>
    [Fact]
    public void A_Kerala_composition_dealer_files_nothing()
    {
        var book = BuildKeralaBook(registrationType: GstRegistrationType.Composition);
        PostSale(book, book.Consumer, 5_00_000m, 1800, SaleDate);

        var ret = BuildReturn(book);

        Assert.Empty(ret.Slabs);
        Assert.Equal(0m, ret.TotalCess.Amount);
        Assert.True(KeralaFloodCessReturnBuilder.IsKeralaRegisteredSupplier(book.Company));  // Kerala, but…
    }

    /// <summary>
    /// 🔴 <b>THE TEST THAT ACTUALLY EXERCISES THE Q14 COMPOSITION GUARD.</b> The guard is unreachable for a book that
    /// was composition all along (see the test above), so the only state that reaches it is a book holding vouchers
    /// which DID post forward tax while the company reads as composition now — a dealer that opted into composition
    /// after issuing tax invoices. Here the sale is posted while the company is Regular, and only then is the
    /// registration switched; the vouchers keep their real CGST/SGST legs, so every later filter passes them and the
    /// guard is the one thing standing between them and a levied slab. Remove it and this reddens with ₹5,000.00.
    ///
    /// <para><b>What this pins is the projection's stated reading, not a statutory finding:</b> the supplier-side
    /// gates read the company's <b>current</b> registration — exactly as
    /// <see cref="KeralaFloodCessReturnBuilder.IsKeralaRegisteredSupplier"/> immediately above the guard does. A
    /// dealer who was Regular during the period and composition afterwards is a live question this projection does
    /// not attempt to answer; it answers per the registration the book holds, consistently across both gates.</para>
    /// </summary>
    [Fact]
    public void A_book_that_switched_to_composition_files_nothing_even_on_vouchers_that_posted_forward_tax()
    {
        var book = BuildKeralaBook();                                  // Regular: the sale posts real tax legs
        PostSale(book, book.Consumer, 5_00_000m, 1800, SaleDate);

        // Sanity: as a Regular book this return is emphatically NOT empty — ₹5,00,000 at 18% bears 1% = ₹5,000.
        var asRegular = BuildReturn(book);
        Assert.Single(asRegular.Slabs);
        Assert.Equal(5_000m, asRegular.TotalCess.Amount);

        // …now the same book reads as a composition dealer. The vouchers are untouched and still carry forward tax.
        book.Company.Gst!.RegistrationType = GstRegistrationType.Composition;
        book.Company.Gst!.CompositionSubType = Domain.CompositionSubType.Trader;

        var ret = BuildReturn(book);

        Assert.True(ret.IsEmpty);
        Assert.Empty(ret.Slabs);
        Assert.Equal(0m, ret.TotalCess.Amount);
        // Not merely zero-footed: a composition dealer discloses nothing either, because it files no such return.
        Assert.Equal(0m, ret.TotalTurnover.Amount);
        Assert.Equal(0m, ret.ExemptedBusinessTurnover.Amount);
        Assert.Equal(0m, ret.TurnoverOutsideTheSchedules.Amount);
        // The levy window is still reported, so the screen can say WHICH days a nil return covers.
        Assert.Equal(PeriodFrom, ret.LevyFrom);
        Assert.Equal(PeriodTo, ret.LevyTo);
    }

    // ─────────────────────────────────────────────────────────── credit notes and rounding

    /// <summary>
    /// The cess is on the "turnover of outward supply" ([KFC-FAQ] Q8), and a credit note reduces that turnover, so
    /// it <b>subtracts</b>. ₹5,00,000 sold, ₹1,00,000 returned ⇒ ₹4,00,000 leviable ⇒ ₹4,000 cess. Adding the credit
    /// note would report ₹6,00,000 and over-collect by ₹2,000 — twice the return.
    /// </summary>
    [Fact]
    public void A_credit_note_reduces_the_leviable_turnover()
    {
        var book = BuildKeralaBook();
        var sale = PostSale(book, book.Consumer, 5_00_000m, 1800, SaleDate);
        PostCreditNote(book, sale, book.Consumer, 1_00_000m, 1800, SaleDate.AddDays(3));

        var ret = BuildReturn(book);

        Assert.Equal(4_00_000m, ret.TotalTurnover.Amount);
        Assert.Equal(4_000.00m, ret.TotalCess.Amount);
        Assert.NotEqual(6_000.00m, ret.TotalCess.Amount);   // the "credit note ADDS" answer, named
    }

    /// <summary>A sale fully reversed by a credit note is not a slab of the return at all — a zero-turnover slab
    /// would print a row asserting a nil that no supply stands behind.</summary>
    [Fact]
    public void A_sale_fully_reversed_by_a_credit_note_produces_no_slab()
    {
        var book = BuildKeralaBook();
        var sale = PostSale(book, book.Consumer, 2_00_000m, 1800, SaleDate);
        PostCreditNote(book, sale, book.Consumer, 2_00_000m, 1800, SaleDate.AddDays(1));

        var ret = BuildReturn(book);

        Assert.Empty(ret.Slabs);
        Assert.True(ret.IsEmpty);
    }

    /// <summary>
    /// 🔴 <b>THE ROUNDING BOUNDARY.</b> The slab is rounded <b>once</b>, not per voucher. Three sales of ₹333.33 at
    /// 3% GST bear 0.25% each: per-voucher rounding gives 0.83 + 0.83 + 0.83 = <b>₹2.49</b>, but the slab total
    /// ₹999.99 × 0.25% = 2.499975 → <b>₹2.50</b>. Splitting the same turnover across more or fewer invoices must not
    /// move the filed figure.
    /// </summary>
    [Fact]
    public void The_slab_is_rounded_once_not_per_voucher()
    {
        var book = BuildKeralaBook();
        for (var i = 0; i < 3; i++)
            PostSale(book, book.Consumer, 333.33m, 300, SaleDate.AddDays(i));

        var ret = BuildReturn(book);

        var slab = Assert.Single(ret.Slabs);
        Assert.Equal(999.99m, slab.Turnover.Amount);
        Assert.Equal(2.50m, slab.Cess.Amount);
        Assert.NotEqual(2.49m, slab.Cess.Amount);   // the per-voucher-rounding answer, named
    }

    /// <summary>Slabs come back ordered by GST rate, and each carries its own limb — a 3% and an 18% sale in one
    /// period produce two rows, not a blended one.</summary>
    [Fact]
    public void Two_rates_in_one_period_produce_two_slabs_ordered_by_rate()
    {
        var book = BuildKeralaBook();
        PostSale(book, book.Consumer, 1_00_000m, 1800, SaleDate);
        PostSale(book, book.Consumer, 2_00_000m, 300, SaleDate.AddDays(1));

        var ret = BuildReturn(book);

        Assert.Equal(2, ret.Slabs.Count);
        Assert.Equal(new[] { 300, 1800 }, ret.Slabs.Select(s => s.GstRateBasisPoints).ToArray());
        Assert.Equal(KfcRateLimb.Reduced, ret.Slabs[0].Limb);
        Assert.Equal(KfcRateLimb.Standard, ret.Slabs[1].Limb);
        Assert.Equal(500.00m, ret.Slabs[0].Cess.Amount);     // 0.25% of 2,00,000
        Assert.Equal(1_000.00m, ret.Slabs[1].Cess.Amount);   // 1% of 1,00,000
        Assert.Equal(1_500.00m, ret.TotalCess.Amount);
    }

    /// <summary>A Kerala company with no outward supply at all files a nil return — stated, with a levy period, not
    /// an absent one.</summary>
    [Fact]
    public void A_Kerala_book_with_no_sales_files_a_stated_nil_return()
    {
        var ret = BuildReturn(BuildKeralaBook());

        Assert.True(ret.IsEmpty);
        Assert.True(ret.PeriodIntersectsTheLevy);   // the period WAS inside the levy — the nil is computed, not absent
        Assert.Equal(PeriodFrom, ret.LevyFrom);
        Assert.Equal(PeriodTo, ret.LevyTo);
        Assert.Equal(0m, ret.TotalCess.Amount);
    }
}
