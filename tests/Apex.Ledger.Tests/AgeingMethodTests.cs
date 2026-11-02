using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Ledger.Tests;

/// <summary>
/// Census 11.13 (advancing 11.9 / T2-24) — the <b>F6 Ageing Method</b> and the <b>Basis of Values &gt; Type of
/// Bills &gt; Overdue Bills</b> filter on the <see cref="Outstandings"/> engine.
///
/// <para>Vendor (TallyPrime, help.tallysolutions.com/manage-receivables-outstanding-tally/, opened by content
/// 2026-10-29): <i>"Press F6 (Ageing Method) to configure the report view as per ageing by bill dates or by due
/// dates and set the ageing windows."</i> and <i>"To view only overdue bills, use Basis of Values to select Overdue
/// Bills in Type of Bills."</i></para>
///
/// <para>Fixture (as-of = start + 120), odd paisa throughout: INV-A 10,000.37 dated s+10 due s+40, part-settled by
/// 3,333.33 (6,667.04 pending; 80 days overdue, 110 days old); INV-B 2,500.01 dated s+110 due EXACTLY on the as-of
/// date (not overdue, IV-245; 10 days old); INV-C 1,234.57 dated s+60 with NO due date (so due = bill date; 60
/// days either way); INV-D 999.99 dated s+20 due s+105 (15 days overdue, 100 days old).</para>
/// </summary>
public class AgeingMethodTests
{
    private static readonly DateOnly Start = new(2024, 4, 1);
    private static readonly DateOnly AsOf = Start.AddDays(120);

    private static Company Seeded()
    {
        var c = CompanyFactory.CreateSeeded("Ageing Co", Start);
        var svc = new LedgerService(c);
        var journal = c.FindVoucherTypeByName("Journal")!;
        var acme = new DomainLedger(Guid.NewGuid(), "Acme Traders", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, true, maintainBillByBill: true);
        var sales = new DomainLedger(Guid.NewGuid(), "Sales Ageing", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, false);
        var cash = new DomainLedger(Guid.NewGuid(), "Cash Ageing", c.FindGroupByName("Cash-in-Hand")!.Id,
            Money.Zero, true);
        foreach (var l in new[] { acme, sales, cash }) c.AddLedger(l);

        void Sale(int day, string reference, decimal amount, int? dueDay) =>
            svc.Post(new Voucher(Guid.NewGuid(), journal.Id, Start.AddDays(day), new[]
            {
                new EntryLine(acme.Id, Money.FromRupees(amount), DrCr.Debit, billAllocations: new[]
                {
                    dueDay is { } d
                        ? new BillAllocation(BillRefType.NewRef, reference, Money.FromRupees(amount), dueDate: Start.AddDays(d))
                        : new BillAllocation(BillRefType.NewRef, reference, Money.FromRupees(amount)),
                }),
                new EntryLine(sales.Id, Money.FromRupees(amount), DrCr.Credit),
            }));

        Sale(10, "INV-A", 10_000.37m, 40);
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, Start.AddDays(45), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(3_333.33m), DrCr.Debit),
            new EntryLine(acme.Id, Money.FromRupees(3_333.33m), DrCr.Credit, billAllocations: new[]
                { new BillAllocation(BillRefType.AgstRef, "INV-A", Money.FromRupees(3_333.33m)) }),
        }));
        Sale(110, "INV-B", 2_500.01m, 120);
        Sale(60, "INV-C", 1_234.57m, null);
        Sale(20, "INV-D", 999.99m, 105);
        return c;
    }

    private static string[] Buckets(IReadOnlyList<AgeingBucket> ageing) =>
        ageing.Select(b => $"{b.Label}={b.Pending.Amount:0.00}").ToArray();

    [Fact]
    public void By_due_date_is_the_default_and_buckets_from_each_bills_due_date()
    {
        var report = Outstandings.Build(Seeded(), AsOf);

        Assert.Equal(AgeingMethod.ByDueDate, report.AgeingMethod);
        Assert.Equal(new[]
        {
            "Not due=2500.01", "0-30 days=999.99", "31-60 days=1234.57", "61-90 days=6667.04", "90+ days=0.00",
        }, Buckets(report.ReceivableAgeing));
    }

    [Fact]
    public void By_bill_date_buckets_from_each_bills_own_date_over_the_same_pending_amounts()
    {
        var c = Seeded();
        var byDue = Outstandings.Build(c, AsOf);
        var byBill = Outstandings.Build(c, AsOf, ageingMethod: AgeingMethod.ByBillDate);

        Assert.Equal(AgeingMethod.ByBillDate, byBill.AgeingMethod);
        Assert.Equal(new[]
        {
            "0-30 days=2500.01", "31-60 days=1234.57", "61-90 days=0.00", "90+ days=7667.03",
        }, Buckets(byBill.ReceivableAgeing));

        // The basis moves the buckets, never a bill: same bills, same pending, same total.
        Assert.Equal(byDue.Receivables, byBill.Receivables);
        Assert.Equal(11_401.61m, byBill.TotalReceivable.Amount);
        Assert.Equal(byBill.TotalReceivable.Amount, byBill.ReceivableAgeing.Sum(b => b.Pending.Amount));
    }

    [Fact]
    public void A_bill_without_a_due_date_ages_identically_under_both_bases_and_one_due_on_the_as_of_date_is_not_due()
    {
        var report = Outstandings.Build(Seeded(), AsOf);
        var c = report.Receivables.Single(b => b.Reference == "INV-C");
        Assert.Equal(c.Date, c.DueDate);
        Assert.Equal(60, c.AgeDays(AsOf, AgeingMethod.ByDueDate));
        Assert.Equal(60, c.AgeDays(AsOf, AgeingMethod.ByBillDate));
        Assert.Equal("31-60 days", Outstandings.BucketLabel(c, AsOf, AgeingMethod.ByDueDate));
        Assert.Equal("31-60 days", Outstandings.BucketLabel(c, AsOf, AgeingMethod.ByBillDate));

        var b = report.Receivables.Single(x => x.Reference == "INV-B");
        Assert.Equal("Not due", Outstandings.BucketLabel(b, AsOf, AgeingMethod.ByDueDate));
        Assert.Equal("0-30 days", Outstandings.BucketLabel(b, AsOf, AgeingMethod.ByBillDate));
        Assert.Equal(10, b.AgeDays(AsOf, AgeingMethod.ByBillDate));
    }

    [Fact]
    public void Overdue_bills_type_keeps_only_bills_past_due_and_buckets_over_them()
    {
        var c = Seeded();
        var overdue = Outstandings.Build(c, AsOf, typeOfBills: TypeOfBills.OverdueBills);

        Assert.Equal(TypeOfBills.OverdueBills, overdue.TypeOfBills);
        Assert.Equal(new[] { "INV-A", "INV-C", "INV-D" }, overdue.Receivables.Select(b => b.Reference).ToArray());
        Assert.Equal(8_901.60m, overdue.TotalReceivable.Amount);
        Assert.Equal(new[]
        {
            "Not due=0.00", "0-30 days=999.99", "31-60 days=1234.57", "61-90 days=6667.04", "90+ days=0.00",
        }, Buckets(overdue.ReceivableAgeing));

        var overdueByBill = Outstandings.Build(c, AsOf, ageingMethod: AgeingMethod.ByBillDate,
            typeOfBills: TypeOfBills.OverdueBills);
        Assert.Equal(new[]
        {
            "0-30 days=0.00", "31-60 days=1234.57", "61-90 days=0.00", "90+ days=7667.03",
        }, Buckets(overdueByBill.ReceivableAgeing));

        // The day after, INV-B (due ON today) joins the overdue bills at 1 day.
        var nextDay = Outstandings.Build(c, AsOf.AddDays(1), typeOfBills: TypeOfBills.OverdueBills);
        Assert.Contains(nextDay.Receivables, b => b.Reference == "INV-B");
    }

    [Fact]
    public void Overdue_bills_report_and_the_overdue_type_of_bills_list_the_same_bills_with_the_same_pending()
    {
        var c = Seeded();
        foreach (var method in new[] { AgeingMethod.ByDueDate, AgeingMethod.ByBillDate })
        {
            var filtered = Outstandings.Build(c, AsOf, ageingMethod: method, typeOfBills: TypeOfBills.OverdueBills);
            var report = OverdueBills.Build(c, AsOf, OutstandingKind.Receivable, ageingMethod: method);

            Assert.Equal(filtered.Receivables, report.Parties.SelectMany(p => p.Bills).ToList());
            Assert.Equal(filtered.TotalReceivable, report.Total);
            Assert.Equal(method, report.AgeingMethod);
            Assert.Equal(Buckets(filtered.ReceivableAgeing), Buckets(report.Ageing));
        }
    }

    [Theory]
    [InlineData(int.MinValue, 0)]
    [InlineData(0, 0)]
    [InlineData(30, 0)]
    [InlineData(31, 1)]
    [InlineData(60, 1)]
    [InlineData(61, 2)]
    [InlineData(90, 2)]
    [InlineData(91, 3)]
    [InlineData(5000, 3)]
    public void Bill_date_window_edges(int days, int expected)
        => Assert.Equal(expected, Outstandings.BucketIndex(days, Outstandings.BillDateBuckets));
}
