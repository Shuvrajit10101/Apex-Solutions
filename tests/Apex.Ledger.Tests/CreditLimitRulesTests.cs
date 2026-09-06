using System;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>Census 10.1 — Credit Limits: the pure rules (<see cref="CreditLimitRules"/>).</b>
///
/// <para><b>🔴 R7 — FIDELITY, with the two categories kept strictly apart.</b>
/// <list type="bullet">
///   <item><b>ATTESTED</b> (help.tallysolutions.com): limits belong to ledgers <i>"created under the groups Sundry
///     Debtors and Sundry Creditors"</i>; a breach is <i>"an error message … while saving the transaction"</i> that
///     shows the limit and the amount exceeded; the two escapes are a lower value or <i>"Override credit limit
///     using post-dated transactions"</i>; the scope is <i>"During sales, purchase, and order transactions"</i>.</item>
///   <item><b>OURS (ruling 9):</b> the exposure ARITHMETIC (prior balance + this voucher — see
///     <see cref="Exposure_is_the_balance_as_at_the_voucher_date_PLUS_this_voucher"/>), the narrow voucher-family
///     scope, and what the post-dated flag overrides (see
///     <see cref="A_post_dated_voucher_is_exempt_only_when_the_party_allows_the_override"/>). None is claimed as
///     fidelity.</item>
/// </list></para>
///
/// <para>Every test here FAILS on today's <c>main</c>: <see cref="CreditLimitRules"/> does not exist there.</para>
/// </summary>
public class CreditLimitRulesTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly D1 = new(2025, 4, 10);

    private static Company Seed(string name = "Credit Co") => CompanyFactory.CreateSeeded(name, FyStart, FyStart);

    private static DomainLedger AddUnder(Company c, string name, string group, bool debit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(group)!.Id, Money.Zero,
                                 openingIsDebit: debit);
        c.AddLedger(l);
        return l;
    }

    private static DomainLedger AddDebtor(Company c, string name = "Acme Ltd")
        => AddUnder(c, name, "Sundry Debtors", debit: true);

    private static DomainLedger AddSales(Company c)
        => c.FindLedgerByName("Sales") ?? AddUnder(c, "Sales", "Sales Accounts", debit: false);

    /// <summary>An unposted Sales voucher debiting the party — the shape the limit is measured against.</summary>
    private static Voucher SaleOf(Company c, DomainLedger party, decimal rupees, DateOnly on, bool postDated = false)
    {
        var sales = AddSales(c);
        var type = c.FindVoucherTypeByName("Sales")!;
        return new Voucher(Guid.NewGuid(), type.Id, on, new[]
        {
            new EntryLine(party.Id, Money.FromRupees(rupees), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(rupees), DrCr.Credit),
        }, partyId: party.Id, postDated: postDated);
    }

    // ------------------------------------------------------------------ who may carry a limit

    /// <summary>ATTESTED: <i>"Credit limits can be set for ledgers created under the groups Sundry Debtors and
    /// Sundry Creditors."</i> Cash, a bank and a sales ledger carry none, and a limit stored on one is ignored
    /// rather than enforced — the master screen could not have shown the field.</summary>
    [Fact]
    public void Only_Sundry_Debtor_and_Sundry_Creditor_ledgers_carry_a_limit()
    {
        var c = Seed();
        var debtor = AddDebtor(c);
        var creditor = AddUnder(c, "Supplier Ltd", "Sundry Creditors", debit: false);
        var sales = AddSales(c);
        var cash = c.FindLedgerByName("Cash")!;

        Assert.True(CreditLimitRules.CarriesCreditLimit(c, debtor));
        Assert.True(CreditLimitRules.CarriesCreditLimit(c, creditor));
        Assert.False(CreditLimitRules.CarriesCreditLimit(c, sales));
        Assert.False(CreditLimitRules.CarriesCreditLimit(c, cash));

        // A limit sitting on a non-party ledger is ignored, not enforced.
        sales.CreditLimit = Money.FromRupees(1m);
        Assert.Null(CreditLimitRules.EffectiveLimit(c, sales));
        debtor.CreditLimit = Money.FromRupees(1m);
        Assert.Equal(Money.FromRupees(1m), CreditLimitRules.EffectiveLimit(c, debtor)!.Value);
    }

    /// <summary>A sub-group of Sundry Debtors qualifies by ancestry, so "Sundry Debtors &gt; North" is a party
    /// group. Ancestry, not a name match on the ledger's own group.</summary>
    [Fact]
    public void A_sub_group_of_Sundry_Debtors_carries_a_limit_too()
    {
        var c = Seed();
        var north = new Group(Guid.NewGuid(), "North Region", GroupNature.Asset,
                              parentId: c.FindGroupByName("Sundry Debtors")!.Id);
        c.AddGroup(north);
        var l = new DomainLedger(Guid.NewGuid(), "Northern Traders", north.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(l);

        Assert.True(CreditLimitRules.CarriesCreditLimit(c, l));
    }

    // ------------------------------------------------------------------ the arithmetic (OURS)

    /// <summary>
    /// 🔴 <b>OURS (ruling 9).</b> Exposure = the party's balance as at the VOUCHER's date + this voucher's own net
    /// effect. Measured against the prior balance alone, a voucher could never be refused by its own value; the
    /// vendor's remedy ("enter a lower value within the prescribed limit") is only actionable on the sum.
    /// </summary>
    [Fact]
    public void Exposure_is_the_balance_as_at_the_voucher_date_PLUS_this_voucher()
    {
        var c = Seed();
        var party = AddDebtor(c);
        new LedgerService(c).Post(SaleOf(c, party, 3000m, D1));

        var next = SaleOf(c, party, 2000m, D1.AddDays(1));
        Assert.Equal(Money.FromRupees(5000m), CreditLimitRules.ExposureAfter(c, party, next));
    }

    /// <summary>ER-12: the engine reads no clock. A BACK-dated voucher is measured against the book as it stood on
    /// its own date, so a later invoice cannot retro-actively push an earlier one over the line.</summary>
    [Fact]
    public void Exposure_is_measured_as_at_the_voucher_date_not_at_the_latest_date()
    {
        var c = Seed();
        var party = AddDebtor(c);
        new LedgerService(c).Post(SaleOf(c, party, 9000m, D1.AddDays(30)));

        // A voucher dated BEFORE that one sees none of it.
        var backDated = SaleOf(c, party, 1000m, D1);
        Assert.Equal(Money.FromRupees(1000m), CreditLimitRules.ExposureAfter(c, party, backDated));
    }

    /// <summary>A Sundry Creditor's exposure is the CREDIT balance — the same figure with the sign flipped — so a
    /// payable is measured in its own natural direction rather than coming out negative and never breaching.</summary>
    [Fact]
    public void A_Sundry_Creditor_exposure_is_the_credit_balance()
    {
        var c = Seed();
        var supplier = AddUnder(c, "Supplier Ltd", "Sundry Creditors", debit: false);
        var purchases = AddUnder(c, "Purchases", "Purchase Accounts", debit: true);
        var type = c.FindVoucherTypeByName("Purchase")!;

        var buy = new Voucher(Guid.NewGuid(), type.Id, D1, new[]
        {
            new EntryLine(purchases.Id, Money.FromRupees(4000m), DrCr.Debit),
            new EntryLine(supplier.Id, Money.FromRupees(4000m), DrCr.Credit),
        }, partyId: supplier.Id);

        Assert.Equal(Money.FromRupees(4000m), CreditLimitRules.ExposureAfter(c, supplier, buy));
    }

    // ------------------------------------------------------------------ the breach decision

    [Fact]
    public void A_ledger_with_no_limit_never_breaches()
    {
        var c = Seed();
        var party = AddDebtor(c);
        Assert.Null(party.CreditLimit);

        Assert.False(CreditLimitRules.Check(c, SaleOf(c, party, 10_000_000m, D1)).Breached);
    }

    /// <summary>🔴 The case a <c>NOT NULL DEFAULT 0</c> column would have made unreachable: <b>zero is a real,
    /// blocking limit</b>, and it is NOT the same answer as "no limit".</summary>
    [Fact]
    public void A_limit_of_ZERO_blocks_any_credit_sale()
    {
        var c = Seed();
        var party = AddDebtor(c);
        party.CreditLimit = Money.Zero;

        var check = CreditLimitRules.Check(c, SaleOf(c, party, 1m, D1));
        Assert.True(check.Breached);
        Assert.Equal(Money.Zero, check.Limit);
        Assert.Equal(Money.FromRupees(1m), check.Excess);
    }

    [Fact]
    public void A_voucher_inside_the_limit_does_not_breach_and_one_over_it_does()
    {
        var c = Seed();
        var party = AddDebtor(c);
        party.CreditLimit = Money.FromRupees(5000m);

        Assert.False(CreditLimitRules.Check(c, SaleOf(c, party, 5000m, D1)).Breached);   // exactly ON the limit

        var over = CreditLimitRules.Check(c, SaleOf(c, party, 5000.01m, D1));
        Assert.True(over.Breached);
        Assert.Equal(Money.FromRupees(0.01m), over.Excess);
        Assert.Equal(Money.FromRupees(5000.01m), over.Exposure);
        Assert.Equal(party.Id, over.Party!.Id);
    }

    /// <summary>The message states the vendor's own two facts — the limit defined for the party, and the amount by
    /// which it is exceeded. Culture-invariant, so the ubuntu/macos runners read the same string.</summary>
    [Fact]
    public void The_breach_message_names_the_party_the_limit_and_the_excess()
    {
        var c = Seed();
        var party = AddDebtor(c, "Acme Ltd");
        party.CreditLimit = Money.FromRupees(5000m);

        var msg = CreditLimitRules.BreachMessage(CreditLimitRules.Check(c, SaleOf(c, party, 7500m, D1)));

        Assert.Contains("Acme Ltd", msg, StringComparison.Ordinal);
        Assert.Contains("5000.00", msg, StringComparison.Ordinal);   // the limit
        Assert.Contains("7500.00", msg, StringComparison.Ordinal);   // the exposure
        Assert.Contains("2500.00", msg, StringComparison.Ordinal);   // the excess
    }

    // ------------------------------------------------------------------ the two escapes (ATTESTED) + scope (OURS)

    /// <summary>
    /// 🔴 <b>OURS (ruling 9), and a DELIBERATE DIVERGENCE FROM THIS TRACK'S DESIGN BRIEF.</b> The brief said the
    /// flag "excludes post-dated vouchers from the exposure". It cannot mean that: <c>LedgerBalances.CountsAsOf</c>
    /// ALREADY drops a not-yet-due post-dated voucher from the balance, so on that reading the flag would be a
    /// no-op — and a no-op cannot be one of the vendor's two named remedies for a breach. Implemented as the
    /// caption states: the post-dated transaction BEING SAVED overrides the limit, and only when the party allows it.
    /// </summary>
    [Fact]
    public void A_post_dated_voucher_is_exempt_only_when_the_party_allows_the_override()
    {
        var c = Seed();
        var party = AddDebtor(c);
        party.CreditLimit = Money.FromRupees(1000m);

        // Post-dated, but the party does NOT allow the override ⇒ still refused.
        Assert.True(CreditLimitRules.Check(c, SaleOf(c, party, 9000m, D1, postDated: true)).Breached);

        // The override on ⇒ the post-dated voucher passes …
        party.OverrideCreditLimitWithPostDated = true;
        Assert.False(CreditLimitRules.Check(c, SaleOf(c, party, 9000m, D1, postDated: true)).Breached);

        // … and an ORDINARY voucher is still refused. The flag is not a way to switch the limit off.
        Assert.True(CreditLimitRules.Check(c, SaleOf(c, party, 9000m, D1)).Breached);
    }

    /// <summary>
    /// 🔴 <b>OURS (ruling 9): the narrowest reading of the one sentence there is</b> — <i>"During sales, purchase,
    /// and order transactions"</i>. A Receipt reduces a receivable and could never breach; refusing a Journal on a
    /// rule the vendor never states would block legitimate entries, which is the half of the wrong-money risk that
    /// refuses good invoices.
    /// </summary>
    [Fact]
    public void The_check_applies_to_sales_and_purchase_but_not_to_a_journal_or_a_receipt()
    {
        Assert.True(CreditLimitRules.AppliesTo(VoucherBaseType.Sales));
        Assert.True(CreditLimitRules.AppliesTo(VoucherBaseType.Purchase));
        Assert.False(CreditLimitRules.AppliesTo(VoucherBaseType.Journal));
        Assert.False(CreditLimitRules.AppliesTo(VoucherBaseType.Receipt));
        Assert.False(CreditLimitRules.AppliesTo(VoucherBaseType.CreditNote));

        var c = Seed();
        var party = AddDebtor(c);
        party.CreditLimit = Money.Zero;
        var sales = AddSales(c);
        var journal = c.FindVoucherTypeByName("Journal")!;

        var j = new Voucher(Guid.NewGuid(), journal.Id, D1, new[]
        {
            new EntryLine(party.Id, Money.FromRupees(9999m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(9999m), DrCr.Credit),
        });
        Assert.False(CreditLimitRules.Check(c, j).Breached);
    }

    /// <summary>A limit on some OTHER party is not this voucher's business — otherwise lowering one party's limit
    /// would start refusing entries that never name them.</summary>
    [Fact]
    public void A_limit_on_an_unrelated_party_is_not_measured()
    {
        var c = Seed();
        var acme = AddDebtor(c, "Acme Ltd");
        var beta = AddDebtor(c, "Beta Traders");
        beta.CreditLimit = Money.Zero;

        Assert.False(CreditLimitRules.Check(c, SaleOf(c, acme, 9000m, D1)).Breached);
    }
}
