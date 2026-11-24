using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Census 6.4, IV-368 (wave 13, track ib, §2AI) — the boundary of <see cref="Gstr1.IsNoBlockServiceLedger"/>: which
/// ledgers WITHOUT a GST block of their own become service legs of a ledger-only invoice. A chapter-99 SAC from the
/// group or company walk admits an income/expense ledger; Round Off, a goods HSN, a Balance-Sheet ledger and a ledger
/// with its own block do not take this route. Each case reads <see cref="Gstr1.ServiceLegs"/> on a posted voucher, the
/// definition every document reader shares.
/// </summary>
public sealed class NoBlockServiceLedgerPredicateTests
{
    private static readonly DateOnly Fy = new(2025, 4, 1);

    private static (Company C, Domain.Ledger Party) Book(string? companySac, string? groupHsn)
    {
        var c = CompanyFactory.CreateSeeded("No Block Predicate Co", Fy);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = "27AAPFU0939F1ZV", RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = Fy, Periodicity = GstReturnPeriodicity.Monthly,
        });
        if (companySac is not null)
            c.Gst!.DefaultGst = new MasterGstDetails
            { HsnSac = companySac, Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        if (groupHsn is not null)
            c.FindGroupByName("Sales Accounts")!.Gst = new MasterGstDetails
            { HsnSac = groupHsn, Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var party = Add(c, "Buyer", "Sundry Debtors", true);
        return (c, party);
    }

    private static Domain.Ledger Add(Company c, string name, string group, bool dr)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(group)!.Id, Money.Zero, openingIsDebit: dr);
        c.AddLedger(l);
        return l;
    }

    private static List<string> LegNames(Company c, Domain.Ledger party, params Domain.Ledger[] credits)
    {
        var lines = new List<EntryLine> { new(party.Id, new Money(123.45m * credits.Length), DrCr.Debit) };
        lines.AddRange(credits.Select(l => new EntryLine(l.Id, new Money(123.45m), DrCr.Credit)));
        var v = new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id,
            new DateOnly(2025, 5, 1), lines, partyId: party.Id);
        return Gstr1.ServiceLegs(c, v).Select(x => x.Ledger.Name).ToList();
    }

    /// <summary>A no-block income ledger under a group declaring 998311 is a service leg (B1 reddens).</summary>
    [Fact]
    public void A_no_block_income_ledger_under_a_sac_group_is_a_service_leg()
    {
        var (c, party) = Book(companySac: null, groupHsn: "998311");
        var income = Add(c, "Consultancy Income", "Sales Accounts", false);
        Assert.Equal(new[] { "Consultancy Income" }, LegNames(c, party, income));
        Assert.True(Gstr1.IsServiceSupplyLeg(income));
    }

    /// <summary>
    /// Round Off sits under Indirect Expenses, so a company-level SAC would otherwise admit it. It is never a service
    /// leg. <b>Mutation B4</b> (the Round Off name check removed) reddens this.
    /// </summary>
    [Fact]
    public void Round_off_is_never_a_service_leg_even_under_a_company_sac()
    {
        var (c, party) = Book(companySac: "998311", groupHsn: null);
        var roundOff = c.FindLedgerByName(GstService.RoundOffLedgerName)!;
        Assert.Null(roundOff.SalesPurchaseGst);
        var income = Add(c, "Consultancy Income", "Sales Accounts", false);
        Assert.Equal(new[] { "Consultancy Income" }, LegNames(c, party, income, roundOff));
    }

    /// <summary>
    /// A no-block sales ledger under a group declaring a GOODS HSN stays out, as on main: nothing states a service.
    /// <b>Mutation B5</b> (the chapter-99 test removed) reddens this.
    /// </summary>
    [Fact]
    public void A_no_block_ledger_whose_walk_lands_on_a_goods_hsn_is_not_a_service_leg()
    {
        var (c, party) = Book(companySac: null, groupHsn: "847130");
        var sales = Add(c, "Sales", "Sales Accounts", false);
        Assert.Empty(LegNames(c, party, sales));
    }

    /// <summary>
    /// A Balance-Sheet ledger (an advance under Current Liabilities) is not a supply line even under a company SAC.
    /// <b>Mutation B6</b> (the P&amp;L test removed) reddens this.
    /// </summary>
    [Fact]
    public void A_balance_sheet_ledger_is_not_a_service_leg_under_a_company_sac()
    {
        var (c, party) = Book(companySac: "998311", groupHsn: null);
        var advance = Add(c, "Advance Held", "Current Liabilities", false);
        Assert.Empty(LegNames(c, party, advance));
    }

    /// <summary>A ledger with no code anywhere in its walk stays out (the plain As-Voucher sale, ER-13).</summary>
    [Fact]
    public void A_no_block_ledger_with_no_code_anywhere_is_not_a_service_leg()
    {
        var (c, party) = Book(companySac: null, groupHsn: null);
        var sales = Add(c, "Sales", "Sales Accounts", false);
        Assert.Empty(LegNames(c, party, sales));
    }
}
