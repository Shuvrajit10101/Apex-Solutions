using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Census row <b>6.24 — the ISD return</b>. <see cref="Gstr6"/> over a real book: the credit an Input Service
/// Distributor received in a month, and what Rule 39 says each sibling registration may claim.
///
/// <para><b>The fixture is CBIC's own shape.</b> "<i>The Corporate office of ABC Ltd., is at Bangalore, with its
/// business locations of selling and servicing of goods at Bangalore, Chennai, Mumbai and Kolkata … the Bangalore
/// Corporate office has to act as ISD to distribute the credit</i>"
/// (<c>cbic-gst.gov.in/pdf/e-version-gst-fliers/InputServiceDistributorinGST.pdf</c>). So: a Karnataka (29)
/// operating registration, a Karnataka ISD registration <b>beside it in the same State</b>, and a Tamil Nadu (33)
/// branch. That same-State pair is exactly what census row 6.23's set rules used to forbid, and relaxing that for
/// an ISD — and only for an ISD — is part of this slice.</para>
///
/// <para>The arithmetic is worked by hand: a ₹50,000 input service bought intra-Karnataka by the ISD bears
/// CGST 4,500 + SGST 4,500. Preceding-FY turnover is ₹6,00,000 in Karnataka and ₹4,00,000 in Tamil Nadu, so the
/// pro rata is 60/40. Karnataka is the ISD's own State and keeps its heads (2,700 + 2,700); Tamil Nadu is
/// elsewhere and its 1,800 + 1,800 aggregate into IGST 3,600. Total out = 9,000 = total in.</para>
/// </summary>
public class Gstr6IsdReturnTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    // The preceding financial year for a May-2025 distribution: Rule 39 Explanation (a), first limb.
    private static readonly DateOnly PrevFyFrom = new(2024, 4, 1);
    private static readonly DateOnly PrevFySale = new(2024, 6, 10);

    // The return period.
    private static readonly DateOnly From = new(2025, 5, 1);
    private static readonly DateOnly To = new(2025, 5, 31);
    private static readonly DateOnly PurchaseDate = new(2025, 5, 12);

    private const string Karnataka = "29";
    private const string TamilNadu = "33";

    /// <summary>A GSTIN with a genuine Luhn-mod-36 check digit, computed by the app's own validator so the test
    /// can never be measuring a checksum failure when it means to measure a domain rule.</summary>
    private static string GstinFor(string stateCode, string pan)
    {
        var body = stateCode + pan + "1Z";
        return body + Gstin.ComputeCheckDigit(body + "0");
    }

    private static readonly string GstinOperating = GstinFor(Karnataka, "AAPFU0939F");
    private static readonly string GstinIsd = GstinFor(Karnataka, "AAACC1206D");
    private static readonly string GstinBranch = GstinFor(TamilNadu, "AABCC1206D");

    private sealed class Fixture
    {
        public required Company Company { get; init; }
        public required GstRegistration Isd { get; init; }
        public required GstRegistration Branch { get; init; }
        public required Domain.Ledger BlockedServices { get; init; }
        public required Domain.Ledger Creditor { get; init; }
        public required Guid PurchaseTypeId { get; init; }
        public required GstService Gst { get; init; }
    }

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

        // The head office ISD — in the SAME State as the operating registration, which is CBIC's own example.
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
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinFor(Karnataka, "AAACS1206D"), StateCode = Karnataka };

        var services = Add(c, "Software Licence & Maintenance", "Indirect Expenses", true);
        var blocked = Add(c, "Employee Motor Vehicle Hire", "Indirect Expenses", true);
        blocked.SalesPurchaseGst = new StockItemGstDetails
        {
            Taxability = GstTaxability.Taxable,
            RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
            ItcEligibility = ItcEligibility.BlockedSection17_5,
            BlockedCreditCategory = BlockedCreditCategory.MotorVehicles,
        };

        var salesTypeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var purchaseTypeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;

        // ---- Preceding-FY turnover: ₹6,00,000 under the operating (Karnataka) registration, ₹4,00,000 under the
        //      Tamil Nadu branch. Plain sales, no tax line — turnover is read from the posted supply VALUE.
        PostSale(ledgers, salesTypeId, debtor, sales, 600_000m, null);
        PostSale(ledgers, salesTypeId, debtor, sales, 400_000m, branch.Id);

        // ---- The ISD's inward input service in May-2025: ₹50,000 @ 18% intra ⇒ CGST 4,500 + SGST 4,500.
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(50_000m), 1800) }, false, GstTaxDirection.Input);
        var lines = new List<EntryLine>
        {
            new(services.Id, Money.FromRupees(50_000m), DrCr.Debit),
            new(creditor.Id, Money.FromRupees(59_000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        var purchase = new Voucher(Guid.NewGuid(), purchaseTypeId, PurchaseDate, lines, partyId: creditor.Id)
        {
            GstRegistrationId = isd.Id,
        };
        ledgers.Post(purchase);

        return new Fixture
        {
            Company = c, Isd = isd, Branch = branch, BlockedServices = blocked,
            Creditor = creditor, PurchaseTypeId = purchaseTypeId, Gst = gst,
        };
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

    // ==========================================================================================================
    //  1. The return itself
    // ==========================================================================================================

    [Fact]
    public void The_credit_received_for_distribution_is_the_posted_input_tax_under_the_isd_registration()
    {
        var f = Build();
        var r = Gstr6.Build(f.Company, From, To, f.Isd.Id);

        Assert.Equal(4_500m, r.ReceivedCgst.Amount);
        Assert.Equal(4_500m, r.ReceivedSgst.Amount);
        Assert.Equal(0m, r.ReceivedIgst.Amount);
        Assert.Equal(9_000m, r.TotalReceived.Amount);
        Assert.Equal(9_000m, r.EligibleCredit.Amount);
        Assert.Equal(0m, r.IneligibleCredit.Amount);
        Assert.Equal(GstinIsd, r.IsdGstin);
    }

    [Fact]
    public void The_distribution_is_pro_rata_on_preceding_financial_year_turnover_and_converts_the_head_out_of_state()
    {
        var f = Build();
        var r = Gstr6.Build(f.Company, From, To, f.Isd.Id);

        Assert.Contains("Preceding financial year", r.RelevantPeriodBasis);
        Assert.Equal(PrevFyFrom, r.RelevantPeriodFrom);
        Assert.Equal(new DateOnly(2025, 3, 31), r.RelevantPeriodTo);

        var karnataka = Assert.Single(r.Distribution, d => d.StateCode == Karnataka);
        var tamilNadu = Assert.Single(r.Distribution, d => d.StateCode == TamilNadu);

        // 60% to Karnataka, in the ISD's own State ⇒ heads survive (Rule 39(1)(j)(i)).
        Assert.Equal(2_700m, karnataka.Cgst.Amount);
        Assert.Equal(2_700m, karnataka.Sgst.Amount);
        Assert.Equal(0m, karnataka.Igst.Amount);

        // 40% to Tamil Nadu, elsewhere ⇒ 1,800 + 1,800 aggregate into IGST 3,600 (Rule 39(1)(j)(ii)).
        Assert.Equal(0m, tamilNadu.Cgst.Amount);
        Assert.Equal(0m, tamilNadu.Sgst.Amount);
        Assert.Equal(3_600m, tamilNadu.Igst.Amount);
    }

    [Fact]
    public void Nothing_is_left_undistributed_so_the_return_satisfies_section_20_2_b()
    {
        var f = Build();
        var r = Gstr6.Build(f.Company, From, To, f.Isd.Id);

        Assert.Equal(9_000m, r.TotalDistributed.Amount);
        Assert.Equal(0m, r.UndistributedCredit.Amount);
    }

    [Fact]
    public void The_due_date_is_the_thirteenth_of_the_following_month_derived_not_tabulated()
    {
        // §39(4) of the CGST Act: an ISD files "within thirteen days after the end of such month".
        Assert.Equal(new DateOnly(2025, 6, 13), Gstr6.DueDateFor(To));
        Assert.Equal(new DateOnly(2025, 1, 13), Gstr6.DueDateFor(new DateOnly(2024, 12, 31)));
        Assert.Equal(new DateOnly(2024, 3, 13), Gstr6.DueDateFor(new DateOnly(2024, 2, 29))); // a leap February

        var f = Build();
        Assert.Equal(new DateOnly(2025, 6, 13), Gstr6.Build(f.Company, From, To, f.Isd.Id).DueDate);
    }

    // ==========================================================================================================
    //  2. Rule 39(1)(g) — eligible and ineligible separately, off the real §17(5) classifier
    // ==========================================================================================================

    [Fact]
    public void Credit_on_a_blocked_service_is_distributed_as_ineligible_and_never_folded_into_the_eligible_row()
    {
        var f = Build();

        // A second May-2025 input service under the ISD, on a ledger flagged §17(5)-blocked: ₹10,000 @ 18%.
        var tax = f.Gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(10_000m), 1800) }, false, GstTaxDirection.Input);
        var lines = new List<EntryLine>
        {
            new(f.BlockedServices.Id, Money.FromRupees(10_000m), DrCr.Debit),
            new(f.Creditor.Id, Money.FromRupees(11_800m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        new LedgerService(f.Company).Post(
            new Voucher(Guid.NewGuid(), f.PurchaseTypeId, PurchaseDate, lines, partyId: f.Creditor.Id)
            { GstRegistrationId = f.Isd.Id });

        var r = Gstr6.Build(f.Company, From, To, f.Isd.Id);

        Assert.Equal(10_800m, r.TotalReceived.Amount);       // 9,000 eligible + 1,800 blocked
        Assert.Equal(9_000m, r.EligibleCredit.Amount);
        Assert.Equal(1_800m, r.IneligibleCredit.Amount);

        // FOUR rows: two registrations × two eligibilities. A build that merged them would file a blocked credit
        // as an available one, which is the failure Rule 39(1)(g) exists to prevent.
        Assert.Equal(4, r.Distribution.Count);
        Assert.Equal(1_800m, r.Distribution.Where(d => !d.IsEligible).Sum(d => d.Total.Amount));
        Assert.Equal(0m, r.UndistributedCredit.Amount);
    }

    // ==========================================================================================================
    //  3. The refusals and the honesty of the statement
    // ==========================================================================================================

    [Fact]
    public void A_registration_that_is_not_an_isd_produces_an_empty_return_and_says_why()
    {
        var f = Build();

        // The operating Karnataka registration files GSTR-1 and GSTR-3B, not GSTR-6. Quietly building a
        // distribution statement for it would invite a filed document with no legal basis.
        var r = Gstr6.Build(f.Company, From, To, GstRegistration.PrimaryId);

        Assert.Empty(r.Distribution);
        Assert.Equal(0m, r.TotalReceived.Amount);
        Assert.Contains(r.Diagnostics, d => d.Contains("not an ISD"));
    }

    [Fact]
    public void The_statement_says_on_every_build_that_direct_attribution_is_not_recorded()
    {
        // Rule 39(1)(c) is implemented in the engine but nothing stores which units an invoice was for, so every pool
        // is distributed as common credit. A reader of the statement must be told that, or they cannot tell
        // "genuinely common" from "the product could not say otherwise".
        var f = Build();
        var r = Gstr6.Build(f.Company, From, To, f.Isd.Id);

        Assert.Contains(r.Diagnostics, d => d.Contains("COMMON credit"));
        Assert.False(r.IsFullyDistributed);   // the caveat is surfaced, not swallowed
    }

    [Fact]
    public void A_reverse_charge_line_under_the_isd_registration_is_not_treated_as_credit_for_distribution()
    {
        // CBIC: "An ISD cannot accept any invoices on which tax is to be discharged under reverse charge
        // mechanism … The ISD itself cannot discharge any tax liability".
        var f = Build();
        var before = Gstr6.Build(f.Company, From, To, f.Isd.Id).TotalReceived.Amount;

        var rcmLines = new List<EntryLine>
        {
            new(f.BlockedServices.Id, Money.FromRupees(10_000m), DrCr.Debit),
            new(f.Creditor.Id, Money.FromRupees(10_000m), DrCr.Credit),
            new(f.Company.Ledgers.First(l => l.Name.Contains("Input CGST")).Id, Money.FromRupees(900m), DrCr.Debit,
                gst: new GstLineTax(GstTaxHead.Central, 900, Money.FromRupees(10_000m), isReverseCharge: true,
                    rcmScheme: RcmItcScheme.OtherRcm)),
            new(f.Company.Ledgers.First(l => l.Name.Contains("Output CGST")).Id, Money.FromRupees(900m), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.Central, 900, Money.FromRupees(10_000m), isReverseCharge: true,
                    rcmScheme: RcmItcScheme.OtherRcm)),
        };
        new LedgerService(f.Company).Post(
            new Voucher(Guid.NewGuid(), f.PurchaseTypeId, PurchaseDate, rcmLines, partyId: f.Creditor.Id)
            { GstRegistrationId = f.Isd.Id });

        Assert.Equal(before, Gstr6.Build(f.Company, From, To, f.Isd.Id).TotalReceived.Amount);
    }

    // ==========================================================================================================
    //  4. The 6.23 set rule, relaxed for an ISD and only for an ISD
    // ==========================================================================================================

    [Fact]
    public void An_isd_registration_may_sit_in_the_same_state_as_an_operating_registration()
    {
        // The fixture already builds this shape; EnsureValid running clean inside Build() is the assertion, and
        // this restates it so a regression names the right rule.
        var f = Build();
        f.Company.Gst!.EnsureValid();

        Assert.Equal(Karnataka, f.Company.Gst!.HomeStateCode);
        Assert.Equal(Karnataka, f.Isd.StateCode);
        Assert.True(f.Company.Gst!.HasIsdRegistration);
    }

    [Fact]
    public void Two_isd_registrations_in_one_state_are_still_refused()
    {
        var f = Build();
        f.Company.Gst!.AddRegistration(new GstRegistration(
            Guid.NewGuid(), "Second Karnataka ISD", Karnataka, GstinFor(Karnataka, "AAECC1206D"),
            GstRegistrationType.InputServiceDistributor));

        Assert.Throws<ArgumentException>(() => f.Company.Gst!.EnsureValid());
    }

    [Fact]
    public void Two_ordinary_registrations_in_one_state_are_still_refused()
    {
        var f = Build();
        f.Company.Gst!.AddRegistration(new GstRegistration(
            Guid.NewGuid(), "Second Karnataka", Karnataka, GstinFor(Karnataka, "AAFCC1206D")));

        Assert.Throws<ArgumentException>(() => f.Company.Gst!.EnsureValid());
    }

    [Fact]
    public void The_isd_is_not_a_recipient_of_its_own_distribution()
    {
        // Rule 39 Explanation (b) makes a recipient a SUPPLIER with the same PAN; the distributor is not one of them,
        // and an ISD cannot receive distributed credit at all.
        var f = Build();
        var recipients = f.Company.Gst!.IsdRecipientRegistrations(f.Isd.Id);

        Assert.Equal(2, recipients.Count);
        Assert.DoesNotContain(recipients, r => r.Id == f.Isd.Id);
        Assert.DoesNotContain(recipients, r => r.RegistrationType == GstRegistrationType.InputServiceDistributor);
        Assert.Contains(recipients, r => r.IsPrimary);
        Assert.Contains(recipients, r => r.Id == f.Branch.Id);
    }
}
