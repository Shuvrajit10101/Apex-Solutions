using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Census row <b>6.23 — Multiple GSTIN registrations for one company</b> (schema v61). The reference product
/// maintains "<i>a single Company data with multiple GST registrations, while enabling you to record transactions
/// and view report for any specific GST registration or all GST registrations</i>"
/// (<c>help.tallysolutions.com/set-up-gst-details-in-company/</c>), with the voucher attributed by "<i>press F3
/// (Company/Tax Registration) and select the registration under which you want to create the voucher</i>".
///
/// <para>🔴 <b>WHAT THESE TESTS ARE ACTUALLY DEFENDING IS A FILED DOCUMENT, NOT A SCREEN.</b> GST returns are
/// filed per registration. A GSTR-1 or GSTR-3B that folded a Maharashtra supply and a Gujarat supply into one
/// document filed under one GSTIN is not a display bug — it is a wrong statutory filing. So the suite proves
/// three separable things, and the middle one is the one that would be silently lost in a refactor:
/// <list type="number">
///   <item><b>ER-13 — the single-registration path is untouched.</b> A company with one GSTIN yields exactly
///     what it yielded at v60, unscoped, with no throw.</item>
///   <item><b>The REFUSAL.</b> Once a second registration exists, every GST projection refuses to build an
///     unscoped return rather than quietly aggregating.</item>
///   <item><b>The SPLIT.</b> Scoped to a registration, each return carries that registration's supplies and
///     ONLY those — and the two scoped returns sum to the whole book, so nothing is dropped either.</item>
/// </list></para>
///
/// <para>The fixture: a Maharashtra (27) company that later registers in Gujarat (24). One intra-Maharashtra
/// sale of ₹1,000 @ 18% under the FIRST registration (CGST 90 + SGST 90) and one intra-Gujarat sale of ₹2,000
/// @ 18% under the SECOND (CGST 180 + SGST 180). Every figure is worked by hand.</para>
/// </summary>
public class GstMultiRegistrationTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly From = new(2024, 4, 1);
    private static readonly DateOnly To = new(2024, 4, 30);
    private static readonly DateOnly D1 = new(2024, 4, 5);    // sale under the FIRST (Maharashtra) registration
    private static readonly DateOnly D2 = new(2024, 4, 7);    // sale under the SECOND (Gujarat) registration

    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";

    private sealed class Fixture
    {
        public required Company Company { get; init; }
        public required GstRegistration Gujarat { get; init; }
    }

    /// <summary>
    /// A Maharashtra company holding a second Gujarat registration, with one sale posted under each. The
    /// Maharashtra sale is left with a NULL <see cref="Voucher.GstRegistrationId"/> on purpose — that is exactly
    /// how every voucher in every pre-v61 book reads, so the fixture exercises the real migration shape rather
    /// than a stamped-everywhere one.
    /// </summary>
    private static Fixture Build(bool addSecondRegistration = true)
    {
        var c = CompanyFactory.CreateSeeded("Multi-GSTIN Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var gujarat = new GstRegistration(
            Guid.NewGuid(), "Gujarat Registration", "24", GstinGujarat,
            GstRegistrationType.Regular, FyStart, GstReturnPeriodicity.Monthly);
        if (addSecondRegistration) c.Gst!.AddRegistration(gujarat);

        var ledgers = new LedgerService(c);
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var mhDebtor = Add(c, "Maharashtra Debtor", "Sundry Debtors", true);
        mhDebtor.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        var gjDebtor = Add(c, "Gujarat Debtor", "Sundry Debtors", true);
        gjDebtor.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24" };

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;

        // ---- D1: ₹1,000 @ 18% intra ⇒ CGST 90 + SGST 90. Recorded under the FIRST registration, stored as NULL.
        var t1 = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) }, false, GstTaxDirection.Output);
        var l1 = new List<EntryLine>
        {
            new(mhDebtor.Id, Money.FromRupees(1180m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
        };
        l1.AddRange(t1.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, D1, l1, partyId: mhDebtor.Id));

        // ---- D2: ₹2,000 @ 18% intra ⇒ CGST 180 + SGST 180. Recorded under the SECOND registration.
        var t2 = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(2000m), 1800) }, false, GstTaxDirection.Output);
        var l2 = new List<EntryLine>
        {
            new(gjDebtor.Id, Money.FromRupees(2360m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(2000m), DrCr.Credit),
        };
        l2.AddRange(t2.TaxLines);
        var v2 = new Voucher(Guid.NewGuid(), salesType, D2, l2, partyId: gjDebtor.Id);
        // Stamped ONLY when the second registration actually exists. Stamping a voucher with the id of a
        // registration the company does not hold would be an inconsistent book, and the ER-13 test below would
        // then be measuring that inconsistency rather than the single-registration path it is named for.
        if (addSecondRegistration) v2.GstRegistrationId = gujarat.Id;
        ledgers.Post(v2);

        return new Fixture { Company = c, Gujarat = gujarat };
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    // ============================================================ 1. the registration collection and its rules

    [Fact]
    public void A_company_with_one_gstin_projects_exactly_one_registration_and_is_not_multi()
    {
        var f = Build(addSecondRegistration: false);
        var gst = f.Company.Gst!;

        Assert.False(gst.IsMultiRegistration);
        Assert.Empty(gst.AdditionalRegistrations);
        var only = Assert.Single(gst.AllRegistrations);

        // The FIRST registration is projected out of the company's own scalar fields, carries the reserved
        // primary id, and takes the vendor's auto-generated "<State> Registration" name.
        Assert.True(only.IsPrimary);
        Assert.Equal(GstRegistration.PrimaryId, only.Id);
        Assert.Equal("27", only.StateCode);
        Assert.Equal(GstinMaharashtra, only.Gstin);
        Assert.Equal("Maharashtra Registration", only.Name);
    }

    [Fact]
    public void Adding_a_second_registration_makes_the_company_multi_and_lists_the_primary_first()
    {
        var f = Build();
        var gst = f.Company.Gst!;

        Assert.True(gst.IsMultiRegistration);
        Assert.Equal(2, gst.AllRegistrations.Count);
        Assert.True(gst.AllRegistrations[0].IsPrimary);
        Assert.Equal("Gujarat Registration", gst.AllRegistrations[1].Name);
    }

    [Fact]
    public void A_null_or_primary_registration_id_on_a_voucher_resolves_to_the_companys_own_registration()
    {
        var f = Build();
        var gst = f.Company.Gst!;

        // These three are the SAME registration, and that identity is what makes the migration a no-op.
        Assert.Equal(GstRegistration.PrimaryId, gst.FindRegistration(null)!.Id);
        Assert.Equal(GstRegistration.PrimaryId, gst.FindRegistration(GstRegistration.PrimaryId)!.Id);
        Assert.Equal(f.Gujarat.Id, gst.FindRegistration(f.Gujarat.Id)!.Id);

        var mhVoucher = f.Company.Vouchers.Single(v => v.Date == D1);
        Assert.Null(mhVoucher.GstRegistrationId);                                   // stored shape
        Assert.Equal(GstRegistration.PrimaryId, GstReportSupport.RegistrationOf(mhVoucher));  // resolved shape
    }

    [Fact]
    public void Two_registrations_in_the_same_state_or_sharing_a_gstin_are_refused()
    {
        var f = Build(addSecondRegistration: false);
        var gst = f.Company.Gst!;

        // Same State as the company's own registration — "which return does this voucher belong to" would have
        // no answer. (Every GSTIN here carries a real Luhn-mod-36 check digit, so the constructor accepts it and
        // the rejection under test is genuinely the SET-level rule, not a checksum failure standing in for it.)
        gst.AddRegistration(new GstRegistration(
            Guid.NewGuid(), "Second Maharashtra", "27", "27AAACC1206D1ZG"));
        Assert.Throws<ArgumentException>(() => gst.EnsureValid());
        gst.RemoveRegistration(gst.AdditionalRegistrations[0].Id);

        // Same GSTIN as the company's own.
        gst.AddRegistration(new GstRegistration(
            Guid.NewGuid(), "Gujarat", "24", GstinMaharashtra));
        Assert.Throws<ArgumentException>(() => gst.EnsureValid());
        gst.RemoveRegistration(gst.AdditionalRegistrations[0].Id);

        // Two additional registrations in one State.
        gst.AddRegistration(new GstRegistration(Guid.NewGuid(), "GJ 1", "24", GstinGujarat));
        gst.AddRegistration(new GstRegistration(Guid.NewGuid(), "GJ 2", "24", "24AABCC1206D1ZL"));
        Assert.Throws<ArgumentException>(() => gst.EnsureValid());
    }

    [Fact]
    public void An_additional_registration_may_not_claim_the_reserved_primary_id()
    {
        var f = Build(addSecondRegistration: false);
        var gst = f.Company.Gst!;

        // Guid.Empty is how a voucher says "the company's own registration". A row claiming it would make the
        // two indistinguishable and silently capture every unstamped voucher in the book.
        gst.AddRegistration(new GstRegistration(
            GstRegistration.PrimaryId, "Impostor", "24", GstinGujarat));
        Assert.Throws<ArgumentException>(() => gst.EnsureValid());
    }

    // ============================================================ 2. ER-13 — one registration is byte-identical

    [Fact]
    public void A_single_registration_company_builds_every_return_unscoped_exactly_as_before()
    {
        var f = Build(addSecondRegistration: false);
        var c = f.Company;

        // No throw, and BOTH sales are folded in — because with one registration there is nothing to separate.
        // ₹1,000 + ₹2,000 = ₹3,000 taxable; CGST 90 + 180 = 270; SGST likewise 270.
        var g1 = Gstr1.Build(c, From, To);
        Assert.Equal(Money.FromRupees(270m), g1.TotalCgst);
        Assert.Equal(Money.FromRupees(270m), g1.TotalSgst);

        var g3b = Gstr3b.Build(c, From, To);
        Assert.Equal(Money.FromRupees(270m), g3b.OutwardCgst);
        Assert.Equal(Money.FromRupees(270m), g3b.OutwardSgst);

        // The v61 filter parameter defaults to null and RegistrationOf() reads the stored NULL as the primary,
        // so naming the primary explicitly must yield the identical figures — otherwise the "NULL means primary"
        // contract is broken somewhere between the store and the report.
        var scoped = Gstr1.Build(c, From, To, GstRegistration.PrimaryId);
        Assert.Equal(g1.TotalCgst, scoped.TotalCgst);
        Assert.Equal(g1.TotalSgst, scoped.TotalSgst);
    }

    // ============================================================ 3. the REFUSAL

    [Fact]
    public void Every_gst_return_refuses_to_build_unscoped_once_a_second_registration_exists()
    {
        var c = Build().Company;

        // 🔴 This is the whole point of the row. Each of these, left unscoped, would previously have produced a
        // document combining supplies made under two different GSTINs — a wrong FILING, not a wrong screen.
        Assert.Throws<InvalidOperationException>(() => Gstr1.Build(c, From, To));
        Assert.Throws<InvalidOperationException>(() => Gstr3b.Build(c, From, To));
        Assert.Throws<InvalidOperationException>(() => TaxAnalysis.Build(c, From, To));

        // ⚠️ KeralaFloodCessReturnBuilder is DELIBERATELY NOT in this list, and the reason is worth recording
        // rather than leaving as an apparent omission. It gates on the supplier being Kerala-registered and
        // returns an empty return BEFORE it ever enumerates vouchers, so this Maharashtra/Gujarat company never
        // reaches the funnel and correctly gets "no such return to file" instead of a refusal. Its scoped path is
        // wired the same way as the others; asserting a throw here would have been asserting the wrong thing.

        // …and the message tells the operator what to do about it rather than just failing.
        var ex = Assert.Throws<InvalidOperationException>(() => Gstr1.Build(c, From, To));
        Assert.Contains("multiple GST registrations", ex.Message);
    }

    [Fact]
    public void The_reverse_charge_sweep_carries_the_same_refusal_as_the_voucher_funnel()
    {
        var c = Build().Company;

        // RcmLines walks company.Vouchers DIRECTLY rather than through PostedDirectionalVouchers, so it needs
        // its own copy of the guard. Without it, GSTR-3B section 3.1(d) would be the one unscoped figure left in
        // an otherwise scoped return.
        Assert.Throws<InvalidOperationException>(
            () => GstReportSupport.RcmLines(c, From, To).ToList());
    }

    // ============================================================ 4. the SPLIT

    [Fact]
    public void Gstr1_scoped_to_a_registration_carries_that_registrations_supplies_and_only_those()
    {
        var f = Build();
        var c = f.Company;

        // The FIRST registration: the ₹1,000 sale only ⇒ CGST 90 + SGST 90.
        var primary = Gstr1.Build(c, From, To, GstRegistration.PrimaryId);
        Assert.Equal(Money.FromRupees(90m), primary.TotalCgst);
        Assert.Equal(Money.FromRupees(90m), primary.TotalSgst);

        // The SECOND registration: the ₹2,000 sale only ⇒ CGST 180 + SGST 180.
        var gujarat = Gstr1.Build(c, From, To, f.Gujarat.Id);
        Assert.Equal(Money.FromRupees(180m), gujarat.TotalCgst);
        Assert.Equal(Money.FromRupees(180m), gujarat.TotalSgst);

        // 🔴 AND NOTHING IS LOST BETWEEN THEM. Scoping that dropped a voucher would understate a filing just as
        // badly as scoping that merged one, and only this assertion can tell the two failures apart.
        Assert.Equal(
            Money.FromRupees(270m),
            new Money(primary.TotalCgst.Amount + gujarat.TotalCgst.Amount));
        Assert.Equal(
            Money.FromRupees(270m),
            new Money(primary.TotalSgst.Amount + gujarat.TotalSgst.Amount));
    }

    [Fact]
    public void Gstr3b_scoped_to_a_registration_splits_the_output_tax_the_same_way()
    {
        var f = Build();
        var c = f.Company;

        var primary = Gstr3b.Build(c, From, To, GstRegistration.PrimaryId);
        Assert.Equal(Money.FromRupees(90m), primary.OutwardCgst);
        Assert.Equal(Money.FromRupees(90m), primary.OutwardSgst);

        var gujarat = Gstr3b.Build(c, From, To, f.Gujarat.Id);
        Assert.Equal(Money.FromRupees(180m), gujarat.OutwardCgst);
        Assert.Equal(Money.FromRupees(180m), gujarat.OutwardSgst);

        Assert.Equal(
            Money.FromRupees(270m),
            new Money(primary.OutwardCgst.Amount + gujarat.OutwardCgst.Amount));
    }

    [Fact]
    public void A_registration_that_holds_no_voucher_files_an_empty_return_rather_than_the_whole_book()
    {
        var f = Build();
        var c = f.Company;

        var kerala = new GstRegistration(Guid.NewGuid(), "Kerala Registration", "32", "32AAACC1206D1ZP");
        c.Gst!.AddRegistration(kerala);

        // 🔴 The failure this catches is a filter written as "if the scope matches NOTHING, fall back to all" —
        // which reads like defensive coding and files the entire book under a registration that traded nothing.
        var empty = Gstr1.Build(c, From, To, kerala.Id);
        Assert.Equal(Money.Zero, empty.TotalCgst);
        Assert.Equal(Money.Zero, empty.TotalSgst);
        Assert.Equal(Money.Zero, empty.TotalIgst);
    }
}
