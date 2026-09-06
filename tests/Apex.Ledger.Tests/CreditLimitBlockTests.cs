using System;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>Census 10.1 — the over-limit BLOCK at save (<see cref="VoucherValidator"/>).</b>
///
/// <para><b>ATTESTED</b> (help.tallysolutions.com, <c>/manage-receivables-outstanding-tally/</c> and
/// <c>Exceeding_Credit_Limits.htm</c>): the amount limit is <i>"an error message … while saving the transaction"</i>
/// which <i>"also shows the amount that has been exceeded"</i> — a hard refusal at accept time, with no "proceed
/// anyway" prompt. It is deliberately NOT the same severity as the credit-DAYS check, which the vendor describes as
/// a warning; that warning is not built in this wave.</para>
///
/// <para>🔴 <b>The load-bearing test in this file is
/// <see cref="Lowering_a_limit_below_an_existing_book_still_lets_that_book_be_REHYDRATED"/>.</b> A credit limit can
/// be changed AFTER the vouchers exist, so a check that fired on the rehydration paths would make an existing
/// company permanently unopenable the moment its limit was lowered — with no route back in, because the screen that
/// could raise the limit again lives inside the company that will not open. The end-to-end proof against a real
/// SQLite file is <c>CreditLimitSchemaTests.Lowering_a_limit_below_an_existing_book_still_lets_the_book_LOAD</c>.</para>
///
/// <para>Every test here FAILS on today's <c>main</c>: the block does not exist there.</para>
/// </summary>
public class CreditLimitBlockTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly D1 = new(2025, 4, 10);

    private static Company Seed(string name = "Block Co") => CompanyFactory.CreateSeeded(name, FyStart, FyStart);

    private static DomainLedger AddDebtor(Company c, string name = "Acme Ltd")
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero,
                                 openingIsDebit: true);
        c.AddLedger(l);
        return l;
    }

    private static DomainLedger AddSales(Company c)
    {
        var existing = c.FindLedgerByName("Sales");
        if (existing is not null) return existing;
        var l = new DomainLedger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id, Money.Zero,
                                 openingIsDebit: false);
        c.AddLedger(l);
        return l;
    }

    private static Voucher SaleOf(Company c, DomainLedger party, decimal rupees, DateOnly on)
    {
        var sales = AddSales(c);
        var type = c.FindVoucherTypeByName("Sales")!;
        return new Voucher(Guid.NewGuid(), type.Id, on, new[]
        {
            new EntryLine(party.Id, Money.FromRupees(rupees), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(rupees), DrCr.Credit),
        }, partyId: party.Id);
    }

    // ------------------------------------------------------------------ the refusal

    [Fact]
    public void A_sales_voucher_over_the_limit_is_refused_at_save()
    {
        var c = Seed();
        var party = AddDebtor(c);
        party.CreditLimit = Money.FromRupees(5000m);

        var ex = Assert.Throws<InvalidVoucherException>(
            () => new LedgerService(c).Post(SaleOf(c, party, 7500m, D1)));

        Assert.Contains("Credit limit", ex.Message, StringComparison.Ordinal);
        Assert.Empty(c.Vouchers);            // nothing persisted — the refusal is before the book moves
    }

    /// <summary>The vendor's own two facts appear in the refusal: the limit defined for the party, and the amount
    /// by which it is exceeded.</summary>
    [Fact]
    public void The_refusal_names_the_party_the_limit_and_the_excess()
    {
        var c = Seed();
        var party = AddDebtor(c, "Acme Ltd");
        party.CreditLimit = Money.FromRupees(5000m);

        var ex = Assert.Throws<InvalidVoucherException>(
            () => new LedgerService(c).Post(SaleOf(c, party, 7500m, D1)));

        Assert.Contains("Acme Ltd", ex.Message, StringComparison.Ordinal);
        Assert.Contains("5000.00", ex.Message, StringComparison.Ordinal);
        Assert.Contains("2500.00", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_voucher_within_the_limit_posts_and_the_NEXT_one_that_crosses_it_does_not()
    {
        var c = Seed();
        var party = AddDebtor(c);
        party.CreditLimit = Money.FromRupees(5000m);
        var svc = new LedgerService(c);

        svc.Post(SaleOf(c, party, 3000m, D1));
        Assert.Single(c.Vouchers);

        // 3000 already on the book + 2500 = 5500 > 5000.
        Assert.Throws<InvalidVoucherException>(() => svc.Post(SaleOf(c, party, 2500m, D1.AddDays(1))));
        Assert.Single(c.Vouchers);

        // A smaller value inside the remaining headroom is accepted — the vendor's own remedy.
        svc.Post(SaleOf(c, party, 2000m, D1.AddDays(1)));
        Assert.Equal(2, c.Vouchers.Count);
    }

    [Fact]
    public void A_ledger_with_no_limit_posts_any_amount()
    {
        var c = Seed();
        var party = AddDebtor(c);
        new LedgerService(c).Post(SaleOf(c, party, 10_000_000m, D1));
        Assert.Single(c.Vouchers);
    }

    // ------------------------------------------------------------------ 🔴 the unopenable-company guard

    /// <summary>
    /// 🔴 <b>THE MOST IMPORTANT TEST IN ROW 10.1.</b> A book is built with no limit, the limit is then lowered
    /// below what the book already holds — exactly what an operator does through the Ledger master — and every
    /// stored voucher is re-posted the way <c>SqliteCompanyStore.Load</c> and company import do it, with
    /// <see cref="CostAllocationStrictness.Legacy"/>. Not one of them may be refused. If this ever reddens, lowering
    /// a credit limit has made a real company permanently unopenable.
    /// </summary>
    [Fact]
    public void Lowering_a_limit_below_an_existing_book_still_lets_that_book_be_REHYDRATED()
    {
        var c = Seed();
        var party = AddDebtor(c);
        var svc = new LedgerService(c);
        svc.Post(SaleOf(c, party, 4000m, D1));
        svc.Post(SaleOf(c, party, 4000m, D1.AddDays(1)));

        // The operator now sets a limit far below the book's existing exposure.
        party.CreditLimit = Money.FromRupees(100m);

        // Rehydration: a fresh aggregate carrying the same masters, re-posting every stored voucher the way the
        // store does. Every one must be accepted.
        var reopened = Seed();
        var reParty = AddDebtor(reopened);
        var reSales = AddSales(reopened);
        reParty.CreditLimit = Money.FromRupees(100m);
        var reSvc = new LedgerService(reopened);
        var salesType = reopened.FindVoucherTypeByName("Sales")!;

        foreach (var stored in c.Vouchers.ToList())
            reSvc.Post(new Voucher(Guid.NewGuid(), salesType.Id, stored.Date, new[]
            {
                new EntryLine(reParty.Id, stored.TotalDebit, DrCr.Debit),
                new EntryLine(reSales.Id, stored.TotalDebit, DrCr.Credit),
            }, partyId: reParty.Id), CostAllocationStrictness.Legacy);

        Assert.Equal(2, reopened.Vouchers.Count);

        // …and the ENTRY path is still refused, so the guard is scoped, not switched off.
        Assert.Throws<InvalidVoucherException>(() => reSvc.Post(SaleOf(reopened, reParty, 1m, D1.AddDays(2))));
    }

    // ------------------------------------------------------------------ the Alter path

    /// <summary>
    /// The original is still on the book while <c>Replace</c> validates, so the replacement must be measured
    /// INSTEAD of it, not on top of it. Without the subtraction, altering an at-the-limit voucher — even changing
    /// nothing about its value — would count the party's exposure twice and refuse an edit that changes no figure.
    /// </summary>
    [Fact]
    public void The_Alter_path_measures_the_replacement_instead_of_the_original()
    {
        var c = Seed();
        var party = AddDebtor(c);
        party.CreditLimit = Money.FromRupees(5000m);
        var svc = new LedgerService(c);
        var posted = svc.Post(SaleOf(c, party, 5000m, D1));

        // Re-saving the same value must be accepted (5000 in, 5000 out — not 10000).
        svc.Replace(posted.Id, EditOf(c, posted, party, 5000m));
        Assert.Single(c.Vouchers);

        // Raising it beyond the limit is still refused.
        Assert.Throws<InvalidVoucherException>(
            () => svc.Replace(posted.Id, EditOf(c, posted, party, 6000m)));
        Assert.Equal(Money.FromRupees(5000m), c.Vouchers.Single().TotalDebit);

        // Lowering it is accepted.
        svc.Replace(posted.Id, EditOf(c, posted, party, 1000m));
        Assert.Equal(Money.FromRupees(1000m), c.Vouchers.Single().TotalDebit);
    }

    /// <summary>
    /// 🔴 <b>THE END-TO-END PROOF THAT AN OFF-BOOK ENTRY IS NOT REFUSED.</b> An Optional voucher (Ctrl+L) is
    /// <i>"excluded from live balances until regularised"</i> — it moves the party's exposure by nothing — so
    /// refusing one is the "block a legitimate entry" half of this row's wrong-money risk. This drives the real
    /// <see cref="LedgerService.Post"/> door rather than <c>CreditLimitRules.Check</c>, so the guard is proven
    /// where the operator actually meets it. The premise is asserted first: the identical amount IS refused when
    /// the voucher is a real one, so the Optional assertion cannot pass on an amount that was never a breach.
    /// </summary>
    [Fact]
    public void An_OPTIONAL_voucher_over_the_limit_still_POSTS()
    {
        var c = Seed();
        var party = AddDebtor(c);
        party.CreditLimit = Money.FromRupees(5000m);
        var svc = new LedgerService(c);

        Assert.Throws<InvalidVoucherException>(() => svc.Post(SaleOf(c, party, 7500m, D1)));
        Assert.Empty(c.Vouchers);

        var sales = AddSales(c);
        var type = c.FindVoucherTypeByName("Sales")!;
        var optional = new Voucher(Guid.NewGuid(), type.Id, D1, new[]
        {
            new EntryLine(party.Id, Money.FromRupees(7500m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(7500m), DrCr.Credit),
        }, partyId: party.Id, optional: true);

        svc.Post(optional);
        Assert.Single(c.Vouchers);

        // …and it consumed no headroom either: a real 5000 still posts on top of it.
        svc.Post(SaleOf(c, party, 5000m, D1));
        Assert.Equal(2, c.Vouchers.Count);
    }

    /// <summary>An ALTERED copy of <paramref name="posted"/> — same id, same number, same date, new value. Replace
    /// refuses a change of identity, so the replacement must carry both through.</summary>
    private static Voucher EditOf(Company c, Voucher posted, DomainLedger party, decimal rupees)
    {
        var sales = AddSales(c);
        return new Voucher(posted.Id, posted.TypeId, posted.Date, new[]
        {
            new EntryLine(party.Id, Money.FromRupees(rupees), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(rupees), DrCr.Credit),
        }, number: posted.Number, partyId: party.Id);
    }
}
