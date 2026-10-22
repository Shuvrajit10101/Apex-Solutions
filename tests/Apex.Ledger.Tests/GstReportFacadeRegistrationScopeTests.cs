using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>THE THREE <c>Report.*</c> FACADES WERE THE PLACE THE REGISTRATION SCOPE WAS LOST (census 6.23).</b>
///
/// <para><b>The shape of the defect, which is worth naming because it is not the usual one.</b> The engines
/// <c>TaxAnalysis.Build</c>, <c>Gstr1.Build</c> and <c>Gstr3b.Build</c> have each taken a
/// <c>Guid? registrationId</c> since v61. Their only Desktop caller reaches them through
/// <c>Report.BuildTaxAnalysis</c> / <c>BuildGstr1</c> / <c>BuildGstr3b</c>, and those three wrappers had
/// <b>no parameter at all</b> — so the caller could not name a registration however much it wanted to. This is
/// therefore not a forgotten argument at a call site; it is a scope that had nowhere to travel. A sweep that
/// greps call sites for a missing argument finds nothing here, which is exactly why it survived.</para>
///
/// <para><b>What it cost.</b> <c>GstReportSupport.EnsureRegistrationScoped</c> refuses an unscoped GST
/// projection on any multi-registration company, so all three reports threw. <c>ReportKind.TaxAnalysis</c>,
/// <c>Gstr1</c> and <c>Gstr3b</c> are dispatched WITHOUT <c>RunStatutoryForm</c>, so the throw was unhandled —
/// measured in <c>Apex.Desktop.Tests.GstReportSurfaceRegistrationScopeTests</c>, where reverting the fix
/// reddens the grid cases with that exception out of <c>ReportsViewModel</c>'s constructor.</para>
///
/// <para>🔴 <b>WHAT THIS FILE ASSERTS THAT THE DESKTOP FILE CANNOT.</b> That the facade <b>forwards</b> the
/// argument rather than merely accepting one. A wrapper that took the parameter and dropped it would compile,
/// would let every Desktop test pass on a single-registration book, and would be the "29 occurrences and zero
/// assignments" failure in miniature. Each test below is therefore run on a TWO-registration book where the
/// two registrations carry DIFFERENT money, and the facade's answer is compared to <b>the engine's own scoped
/// answer</b> — not to a literal, so the test cannot pin a wrong number.</para>
///
/// <para><b>Figures by hand.</b> Maharashtra primary (home State 27): one intra-State sale of ₹1,000 at 18% ⇒
/// CGST 90 / SGST 90. Gujarat branch: one intra-State sale of ₹4,000 at 18% ⇒ CGST 360 / SGST 360. The primary's
/// figure (90), the branch's (360) and the unscoped fold (450) are three distinct numbers.</para>
/// </summary>
public sealed class GstReportFacadeRegistrationScopeTests
{
    private const string Maharashtra = "27";
    private const string Gujarat = "24";

    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly From = new(2024, 4, 1);
    private static readonly DateOnly To = new(2024, 4, 30);

    private static string GstinFor(string stateCode, string pan)
    {
        var body = stateCode + pan + "1Z";
        return body + Gstin.ComputeCheckDigit(body + "0");
    }

    private static readonly string GstinPrimary = GstinFor(Maharashtra, "AAPFU0939F");
    private static readonly string GstinBranch = GstinFor(Gujarat, "AABCC1206D");
    private static readonly string GstinDebtorMh = GstinFor(Maharashtra, "AAACS1206D");
    private static readonly string GstinDebtorGj = GstinFor(Gujarat, "AADCS1206D");

    /// <summary>
    /// 🔴 Before the parameter existed this line could not be written, and the no-argument form THROWS on this
    /// book — asserted below so the two halves are not confused. The scoped facade must agree with the scoped
    /// engine to the paisa, and must NOT carry the branch's 360 or the fold's 450.
    /// </summary>
    [Fact]
    public void BuildGstr1_forwards_the_registration_to_the_engine()
    {
        var (company, branch) = TwoRegistrationBook("Facade Gstr1");

        var viaFacade = Report.BuildGstr1(company, From, To, GstRegistration.PrimaryId);
        var viaEngine = Gstr1.Build(company, From, To, GstRegistration.PrimaryId);

        Assert.Equal(viaEngine.TotalCgst.Amount, viaFacade.TotalCgst.Amount);
        Assert.Equal(90m, viaFacade.TotalCgst.Amount);
        Assert.Equal(GstinPrimary, company.Gst!.FindRegistration(GstRegistration.PrimaryId)!.Gstin);

        // ...and the branch's own return is a DIFFERENT document through the same facade, so the parameter is
        // genuinely read rather than ignored in favour of a constant.
        Assert.Equal(360m, Report.BuildGstr1(company, From, To, branch.Id).TotalCgst.Amount);
        Assert.Equal(GstinBranch, company.Gst!.FindRegistration(branch.Id)!.Gstin);
    }

    /// <summary>GSTR-3B through its own facade — a different engine leg (<c>Gstr3b.ReadSide</c>).</summary>
    [Fact]
    public void BuildGstr3b_forwards_the_registration_to_the_engine()
    {
        var (company, branch) = TwoRegistrationBook("Facade Gstr3b");

        var viaFacade = Report.BuildGstr3b(company, From, To, GstRegistration.PrimaryId);
        var viaEngine = Gstr3b.Build(company, From, To, GstRegistration.PrimaryId);

        Assert.Equal(viaEngine.OutwardCgst.Amount, viaFacade.OutwardCgst.Amount);
        Assert.Equal(90m, viaFacade.OutwardCgst.Amount);
        Assert.Equal(360m, Report.BuildGstr3b(company, From, To, branch.Id).OutwardCgst.Amount);
    }

    /// <summary>Tax Analysis through its own facade — a third sweep (<c>PostedGstVouchers</c> per head/rate).</summary>
    [Fact]
    public void BuildTaxAnalysis_forwards_the_registration_to_the_engine()
    {
        var (company, branch) = TwoRegistrationBook("Facade TaxAnalysis");

        var viaFacade = Report.BuildTaxAnalysis(company, From, To, GstRegistration.PrimaryId);
        var viaEngine = TaxAnalysis.Build(company, From, To, GstRegistration.PrimaryId);

        Assert.Equal(viaEngine.Outward.TotalCgst.Amount, viaFacade.Outward.TotalCgst.Amount);
        Assert.Equal(90m, viaFacade.Outward.TotalCgst.Amount);
        Assert.Equal(360m, Report.BuildTaxAnalysis(company, From, To, branch.Id).Outward.TotalCgst.Amount);
    }

    /// <summary>
    /// <b>The refusal is still there for a caller that names nothing — through the FACADE, which is the form the
    /// defect took.</b> All three are asserted, because a softened guard would make every test above pass for
    /// the wrong reason.
    /// </summary>
    [Fact]
    public void An_unscoped_facade_call_still_refuses_on_a_multi_registration_book()
    {
        var (company, _) = TwoRegistrationBook("Facade Refusal");

        Assert.Throws<InvalidOperationException>(() => Report.BuildGstr1(company, From, To));
        Assert.Throws<InvalidOperationException>(() => Report.BuildGstr3b(company, From, To));
        Assert.Throws<InvalidOperationException>(() => Report.BuildTaxAnalysis(company, From, To));
    }

    /// <summary>
    /// <b>ER-13 at the facade.</b> On a single-registration book the new default argument must reach the same
    /// projection the no-argument form did — for all three facades, compared figure for figure against the
    /// engine called with no scope at all.
    /// </summary>
    [Fact]
    public void A_single_registration_book_reads_the_same_through_every_facade_form()
    {
        var company = SingleRegistrationBook("Facade Er13");

        Assert.Equal(
            Gstr1.Build(company, From, To).TotalCgst.Amount,
            Report.BuildGstr1(company, From, To, GstRegistration.PrimaryId).TotalCgst.Amount);
        Assert.Equal(
            Gstr3b.Build(company, From, To).OutwardCgst.Amount,
            Report.BuildGstr3b(company, From, To, GstRegistration.PrimaryId).OutwardCgst.Amount);
        Assert.Equal(
            TaxAnalysis.Build(company, From, To).Outward.TotalCgst.Amount,
            Report.BuildTaxAnalysis(company, From, To, GstRegistration.PrimaryId).Outward.TotalCgst.Amount);

        // And the figure is not zero on both sides, which would make the three equalities vacuous.
        Assert.Equal(90m, Report.BuildGstr1(company, From, To, GstRegistration.PrimaryId).TotalCgst.Amount);
    }

    // ================================================================ fixture

    private static (Company Company, GstRegistration Branch) TwoRegistrationBook(string name)
    {
        var c = NewGstCompany(name);

        var branch = new GstRegistration(
            Guid.NewGuid(), "Gujarat Registration", Gujarat, GstinBranch,
            GstRegistrationType.Regular, FyStart);
        c.Gst!.AddRegistration(branch);
        c.Gst!.EnsureValid();

        var gst = new GstService(c);
        var ledgers = new LedgerService(c);
        var sales = c.Ledgers.First(l => l.Name == "Sales");

        var debtorMh = AddLedger(c, "Maharashtra Debtor", "Sundry Debtors", true);
        debtorMh.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinDebtorMh, StateCode = Maharashtra };
        var debtorGj = AddLedger(c, "Gujarat Debtor", "Sundry Debtors", true);
        debtorGj.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinDebtorGj, StateCode = Gujarat };

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;

        PostIntraSale(ledgers, gst, salesType, sales, debtorMh, 1_000m, new DateOnly(2024, 4, 5), 1, null);
        PostIntraSale(ledgers, gst, salesType, sales, debtorGj, 4_000m, new DateOnly(2024, 4, 9), 2, branch.Id);

        return (c, branch);
    }

    private static Company SingleRegistrationBook(string name)
    {
        var c = NewGstCompany(name);

        var gst = new GstService(c);
        var ledgers = new LedgerService(c);
        var sales = c.Ledgers.First(l => l.Name == "Sales");

        var debtorMh = AddLedger(c, "Maharashtra Debtor", "Sundry Debtors", true);
        debtorMh.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinDebtorMh, StateCode = Maharashtra };

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        PostIntraSale(ledgers, gst, salesType, sales, debtorMh, 1_000m, new DateOnly(2024, 4, 5), 1, null);

        return c;
    }

    private static Company NewGstCompany(string name)
    {
        var c = CompanyFactory.CreateSeeded(name + " " + Guid.NewGuid().ToString("N")[..6], FyStart);
        c.BooksBeginFrom = FyStart;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = Maharashtra,
            Gstin = GstinPrimary,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var sales = AddLedger(c, "Sales", "Sales Accounts", false);
        sales.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998313", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
        };
        return c;
    }

    private static void PostIntraSale(
        LedgerService ledgers, GstService gst, Guid typeId, Domain.Ledger sales, Domain.Ledger party,
        decimal value, DateOnly date, int number, Guid? registrationId)
    {
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(value), 1800) }, false, GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(party.Id, Money.FromRupees(value + tax.TotalTax.Amount), DrCr.Debit),
            new(sales.Id, Money.FromRupees(value), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);

        var v = new Voucher(Guid.NewGuid(), typeId, date, lines, number: number, partyId: party.Id);
        if (registrationId is { } r) v.GstRegistrationId = r;
        ledgers.Post(v);
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var l = new Domain.Ledger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
