using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Ledger.Tests;

/// <summary>
/// Census 11.13 — the <see cref="OverdueBills"/> engine behind Overdue Receivables / Overdue Payables.
/// Odd-paisa amounts throughout, so a figure that is rounded, truncated or summed in floating point anywhere
/// in the pipeline shows up as a wrong last digit; and one bill falls due EXACTLY on the as-of date, so the
/// boundary (IV-245: due on the as-of date is NOT overdue) is pinned rather than assumed.
/// </summary>
public class OverdueBillsTests
{
    internal static readonly DateOnly Start = new(2024, 4, 1);
    internal static readonly DateOnly AsOf = Start.AddDays(90);   // 30-Jun-2024

    /// <summary>
    /// The shared fixture, dated off <paramref name="start"/> (as-of = start + 90): a debtor with three bills (one part-settled, one due ON the as-of date, one due the
    /// day before) and a creditor with one bill. Every amount ends in an odd paisa.
    /// </summary>
    internal static Company Seed(Company c, DateOnly start, out Voucher inv1, out Voucher inv2, out Voucher inv3, out Voucher pb9)
    {
        var asOf = start.AddDays(90);
        var svc = new LedgerService(c);
        var journal = c.FindVoucherTypeByName("Journal")!;

        var acme = new DomainLedger(Guid.NewGuid(), "Acme Traders", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, true, maintainBillByBill: true);
        var zenith = new DomainLedger(Guid.NewGuid(), "Zenith Supplies", c.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, false, maintainBillByBill: true);
        var sales = new DomainLedger(Guid.NewGuid(), "Sales Overdue", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, false);
        var purchases = new DomainLedger(Guid.NewGuid(), "Purchases Overdue", c.FindGroupByName("Purchase Accounts")!.Id,
            Money.Zero, true);
        var cash = new DomainLedger(Guid.NewGuid(), "Cash Overdue", c.FindGroupByName("Cash-in-Hand")!.Id,
            Money.Zero, true);
        foreach (var l in new[] { acme, zenith, sales, purchases, cash }) c.AddLedger(l);

        Voucher Sale(DateOnly date, string reference, decimal amount, DateOnly due) =>
            svc.Post(new Voucher(Guid.NewGuid(), journal.Id, date, new[]
            {
                new EntryLine(acme.Id, Money.FromRupees(amount), DrCr.Debit, billAllocations: new[]
                    { new BillAllocation(BillRefType.NewRef, reference, Money.FromRupees(amount), dueDate: due) }),
                new EntryLine(sales.Id, Money.FromRupees(amount), DrCr.Credit),
            }));

        // INV-1: 10,000.37 due 31-May, part-settled by 3,333.33 => 6,667.04 pending, 30 days overdue at 30-Jun.
        inv1 = Sale(start.AddDays(30), "INV-1", 10_000.37m, start.AddDays(60));
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, start.AddDays(65), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(3_333.33m), DrCr.Debit),
            new EntryLine(acme.Id, Money.FromRupees(3_333.33m), DrCr.Credit, billAllocations: new[]
                { new BillAllocation(BillRefType.AgstRef, "INV-1", Money.FromRupees(3_333.33m)) }),
        }));
        // INV-2: 2,500.01 due EXACTLY on the as-of date — due, not overdue.
        inv2 = Sale(start.AddDays(61), "INV-2", 2_500.01m, asOf);
        // INV-3: 1,234.57 due the day before the as-of date — 1 day overdue.
        inv3 = Sale(start.AddDays(62), "INV-3", 1_234.57m, asOf.AddDays(-1));

        // PB-9: a payable 7,777.77 due 15-Jun => 15 days overdue at 30-Jun.
        pb9 = svc.Post(new Voucher(Guid.NewGuid(), journal.Id, start.AddDays(61), new[]
        {
            new EntryLine(purchases.Id, Money.FromRupees(7_777.77m), DrCr.Debit),
            new EntryLine(zenith.Id, Money.FromRupees(7_777.77m), DrCr.Credit, billAllocations: new[]
                { new BillAllocation(BillRefType.NewRef, "PB-9", Money.FromRupees(7_777.77m),
                    dueDate: start.AddDays(75)) }),
        }));
        return c;
    }

    private static Company Seeded(out Voucher inv1, out Voucher inv2, out Voucher inv3, out Voucher pb9)
        => Seed(CompanyFactory.CreateSeeded("Overdue Co", Start), Start, out inv1, out inv2, out inv3, out pb9);

    [Fact]
    public void Overdue_receivables_list_only_bills_past_due_with_odd_paisa_pending_and_the_opening_voucher()
    {
        var c = Seeded(out var inv1, out _, out var inv3, out _);

        var report = OverdueBills.Build(c, AsOf, OutstandingKind.Receivable);

        var party = Assert.Single(report.Parties);
        Assert.Equal("Acme Traders", party.LedgerName);
        Assert.Equal(new[] { "INV-1", "INV-3" }, party.Bills.Select(b => b.Reference).ToArray());
        Assert.Equal(6_667.04m, party.Bills[0].Pending.Amount);
        Assert.Equal(30, party.Bills[0].OverdueDays(AsOf));
        Assert.Equal(inv1.Id, party.Bills[0].OpeningVoucherId);
        Assert.Equal(1_234.57m, party.Bills[1].Pending.Amount);
        Assert.Equal(1, party.Bills[1].OverdueDays(AsOf));
        Assert.Equal(inv3.Id, party.Bills[1].OpeningVoucherId);
        Assert.Equal(7_901.61m, party.Total.Amount);
        Assert.Equal(7_901.61m, report.Total.Amount);
    }

    /// <summary>🔴 THE BOUNDARY. A bill due exactly on the as-of date has fallen due and is NOT overdue; the
    /// next day it is one day overdue. Both halves are asserted so neither direction can drift.</summary>
    [Fact]
    public void A_bill_due_exactly_on_the_as_of_date_is_not_overdue_until_the_next_day()
    {
        var c = Seeded(out _, out var inv2, out _, out _);

        var onTheDay = OverdueBills.Build(c, AsOf, OutstandingKind.Receivable);
        Assert.DoesNotContain(onTheDay.Parties.SelectMany(p => p.Bills), b => b.Reference == "INV-2");
        // ...yet the same bill IS on the full outstandings and counts as "due till today".
        var outstanding = Outstandings.Build(c, AsOf);
        Assert.Contains(outstanding.Receivables, b => b.Reference == "INV-2" && b.IsDueBy(AsOf) && !b.IsOverdue(AsOf));

        var nextDay = OverdueBills.Build(c, AsOf.AddDays(1), OutstandingKind.Receivable);
        var bill = Assert.Single(nextDay.Parties.SelectMany(p => p.Bills), b => b.Reference == "INV-2");
        Assert.Equal(1, bill.OverdueDays(AsOf.AddDays(1)));
        Assert.Equal(inv2.Id, bill.OpeningVoucherId);
        Assert.Equal(10_401.62m, nextDay.Total.Amount);
    }

    [Fact]
    public void Overdue_payables_list_the_creditor_bill_and_never_a_receivable()
    {
        var c = Seeded(out _, out _, out _, out var pb9);

        var report = OverdueBills.Build(c, AsOf, OutstandingKind.Payable);

        var party = Assert.Single(report.Parties);
        Assert.Equal("Zenith Supplies", party.LedgerName);
        var bill = Assert.Single(party.Bills);
        Assert.Equal("PB-9", bill.Reference);
        Assert.Equal(7_777.77m, bill.Pending.Amount);
        Assert.Equal(15, bill.OverdueDays(AsOf));
        Assert.Equal(pb9.Id, bill.OpeningVoucherId);
        Assert.Equal(7_777.77m, report.Total.Amount);
    }

    /// <summary>The as-of date scopes the report: before INV-1 fell due nothing is overdue, and a fully settled
    /// bill drops off.</summary>
    [Fact]
    public void An_earlier_as_of_date_lists_nothing_and_a_settled_bill_is_never_overdue()
    {
        var c = Seeded(out _, out _, out _, out _);
        Assert.Empty(OverdueBills.Build(c, Start.AddDays(60), OutstandingKind.Receivable).Parties);

        var svc = new LedgerService(c);
        var acme = c.FindLedgerByName("Acme Traders")!;
        var cash = c.FindLedgerByName("Cash Overdue")!;
        svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Journal")!.Id, Start.AddDays(80), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(6_667.04m), DrCr.Debit),
            new EntryLine(acme.Id, Money.FromRupees(6_667.04m), DrCr.Credit, billAllocations: new[]
                { new BillAllocation(BillRefType.AgstRef, "INV-1", Money.FromRupees(6_667.04m)) }),
        }));

        var after = OverdueBills.Build(c, AsOf, OutstandingKind.Receivable);
        Assert.Equal(new[] { "INV-3" }, after.Parties.Single().Bills.Select(b => b.Reference).ToArray());
    }
}
