using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Ledger.Tests;

/// <summary>
/// Census 11.13 (advancing 11.9 / T2-24) — <b>operator-set ageing windows</b> on the F6 ageing criteria (IV-305..
/// IV-308), at the engine. Vendor (TallyPrime), opened by content 2026-11-02:
/// help.tallysolutions.com/manage-receivables-outstanding-tally/ — <i>"Press F6 (Ageing Method) to configure the
/// report view as per ageing by bill dates or by due dates and set the ageing windows."</i>;
/// help.tallysolutions.com/tally-prime/analysis-verification/outstandings-tally/ — <i>"Specify the required ageing
/// criteria and press Enter."</i>
///
/// <para>Fixture (as-of = start + 120), odd paisa throughout, the same bills <see cref="AgeingMethodTests"/> uses:
/// INV-A 6,667.04 pending (80 days overdue, 110 days old); INV-B 2,500.01 (due ON the as-of date: 0 overdue, 10 old);
/// INV-C 1,234.57 (no due date: 60 either way); INV-D 999.99 (15 overdue, 100 old). Zenith (creditor): PB-9 7,777.77
/// (20 overdue, 70 old); PB-10 3,000.03 (not due, 5 old). Every custom window below puts a bill EXACTLY on an edge.</para>
/// </summary>
public class AgeingWindowsTests
{
    private static readonly DateOnly Start = new(2024, 4, 1);
    private static readonly DateOnly AsOf = Start.AddDays(120);

    private static Company Seeded()
    {
        var c = CompanyFactory.CreateSeeded("Ageing Windows Co", Start);
        var svc = new LedgerService(c);
        var journal = c.FindVoucherTypeByName("Journal")!;
        var acme = new DomainLedger(Guid.NewGuid(), "Acme Traders", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, true, maintainBillByBill: true);
        var zenith = new DomainLedger(Guid.NewGuid(), "Zenith Supplies", c.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, false, maintainBillByBill: true);
        var sales = new DomainLedger(Guid.NewGuid(), "Sales Ageing", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, false);
        var purchases = new DomainLedger(Guid.NewGuid(), "Purchases Ageing", c.FindGroupByName("Purchase Accounts")!.Id,
            Money.Zero, true);
        var cash = new DomainLedger(Guid.NewGuid(), "Cash Ageing", c.FindGroupByName("Cash-in-Hand")!.Id,
            Money.Zero, true);
        foreach (var l in new[] { acme, zenith, sales, purchases, cash }) c.AddLedger(l);

        BillAllocation Alloc(string reference, decimal amount, int? dueDay) => dueDay is { } d
            ? new BillAllocation(BillRefType.NewRef, reference, Money.FromRupees(amount), dueDate: Start.AddDays(d))
            : new BillAllocation(BillRefType.NewRef, reference, Money.FromRupees(amount));

        void Sale(int day, string reference, decimal amount, int? dueDay) =>
            svc.Post(new Voucher(Guid.NewGuid(), journal.Id, Start.AddDays(day), new[]
            {
                new EntryLine(acme.Id, Money.FromRupees(amount), DrCr.Debit,
                    billAllocations: new[] { Alloc(reference, amount, dueDay) }),
                new EntryLine(sales.Id, Money.FromRupees(amount), DrCr.Credit),
            }));

        void Purchase(int day, string reference, decimal amount, int dueDay) =>
            svc.Post(new Voucher(Guid.NewGuid(), journal.Id, Start.AddDays(day), new[]
            {
                new EntryLine(purchases.Id, Money.FromRupees(amount), DrCr.Debit),
                new EntryLine(zenith.Id, Money.FromRupees(amount), DrCr.Credit,
                    billAllocations: new[] { Alloc(reference, amount, dueDay) }),
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
        Purchase(50, "PB-9", 7_777.77m, 100);
        Purchase(115, "PB-10", 3_000.03m, 130);
        return c;
    }

    private static string[] Buckets(IReadOnlyList<AgeingBucket> ageing) =>
        ageing.Select(b => $"{b.Label}={b.Pending.Amount:0.00}").ToArray();

    /// <summary>Due basis: 1-15 / 16-60 / 61+. INV-D sits on 15, INV-C on 60.</summary>
    internal static readonly AgeingPeriod[] DuePeriods =
        { new(1, 15), new(16, 60), new(61, null) };

    /// <summary>Bill basis: 0-10 / 11-59 / 60-100, closed, so "Over 100 days" is appended. INV-B on 10, INV-C on
    /// 60, INV-D on 100.</summary>
    internal static readonly AgeingPeriod[] BillPeriods =
        { new(0, 10), new(11, 59), new(60, 100) };

    [Fact]
    public void Custom_due_date_windows_bucket_the_receivables_and_payables_on_their_exact_edges()
    {
        var report = Outstandings.Build(Seeded(), AsOf, ageingPeriods: DuePeriods);

        Assert.Equal(new[] { "Not due=2500.01", "1-15 days=999.99", "16-60 days=1234.57", "61+ days=6667.04" },
            Buckets(report.ReceivableAgeing));
        Assert.Equal(new[] { "Not due=3000.03", "1-15 days=0.00", "16-60 days=7777.77", "61+ days=0.00" },
            Buckets(report.PayableAgeing));
        Assert.Equal(report.TotalReceivable.Amount, report.ReceivableAgeing.Sum(b => b.Pending.Amount));

        var d = report.Receivables.Single(b => b.Reference == "INV-D");
        Assert.Equal("1-15 days", Outstandings.BucketLabel(d, AsOf, AgeingMethod.ByDueDate, report.Windows));
        var c = report.Receivables.Single(b => b.Reference == "INV-C");
        Assert.Equal("16-60 days", Outstandings.BucketLabel(c, AsOf, AgeingMethod.ByDueDate, report.Windows));
    }

    [Fact]
    public void Custom_bill_date_windows_append_an_open_tail_and_lose_no_pending()
    {
        var report = Outstandings.Build(Seeded(), AsOf, ageingMethod: AgeingMethod.ByBillDate, ageingPeriods: BillPeriods);

        Assert.Equal(new[] { "0-10 days=2500.01", "11-59 days=0.00", "60-100 days=2234.56", "Over 100 days=6667.04" },
            Buckets(report.ReceivableAgeing));
        Assert.Equal(new[] { "0-10 days=3000.03", "11-59 days=0.00", "60-100 days=7777.77", "Over 100 days=0.00" },
            Buckets(report.PayableAgeing));
        Assert.Equal(11_401.61m, report.ReceivableAgeing.Sum(b => b.Pending.Amount));
        Assert.Equal(10_777.80m, report.PayableAgeing.Sum(b => b.Pending.Amount));
    }

    [Fact]
    public void Custom_windows_reach_the_overdue_view_through_the_one_engine()
    {
        var c = Seeded();
        foreach (var (method, periods) in new[] { (AgeingMethod.ByDueDate, DuePeriods), (AgeingMethod.ByBillDate, BillPeriods) })
        {
            var filtered = Outstandings.Build(c, AsOf, ageingMethod: method, typeOfBills: TypeOfBills.OverdueBills,
                ageingPeriods: periods);
            var overdue = OverdueBills.Build(c, AsOf, OutstandingKind.Receivable, ageingMethod: method, ageingPeriods: periods);
            Assert.Equal(Buckets(filtered.ReceivableAgeing), Buckets(overdue.Ageing));
            Assert.Equal(filtered.Windows, overdue.Windows);
        }

        var dueOverdue = OverdueBills.Build(c, AsOf, OutstandingKind.Receivable, ageingPeriods: DuePeriods);
        Assert.Equal(new[] { "Not due=0.00", "1-15 days=999.99", "16-60 days=1234.57", "61+ days=6667.04" },
            Buckets(dueOverdue.Ageing));
    }

    [Fact]
    public void The_default_periods_give_the_basis_own_windows_byte_for_byte()
    {
        foreach (var method in new[] { AgeingMethod.ByDueDate, AgeingMethod.ByBillDate })
        {
            var windows = Outstandings.TryWindowsFor(method, Outstandings.DefaultPeriodsFor(method), out var error);
            Assert.Equal(string.Empty, error);
            Assert.Same(Outstandings.BucketsFor(method), windows);
        }
        Assert.Same(Outstandings.BucketsFor(AgeingMethod.ByDueDate), Outstandings.WindowsFor(AgeingMethod.ByDueDate, null));
    }

    [Theory]
    [InlineData("due", "0-15,16-", "Period 1 must start from 1 days")]
    [InlineData("bill", "1-15,16-", "Period 1 must start from 0 days")]
    [InlineData("due", "1-15,17-", "Period 2 must start from 16 days")]
    [InlineData("due", "1-15,10-30", "Period 2 must start from 16 days")]
    [InlineData("due", "1-,31-60", "Only the last period may be left open")]
    [InlineData("bill", "0-10,11-5", "Period 2: To (5) is below From (11)")]
    [InlineData("bill", "0-40000", "may not exceed 36500")]
    [InlineData("bill", "", "Enter at least one ageing period")]
    public void Refused_periods_name_the_rule_and_the_engine_refuses_them_too(string basis, string spec, string expected)
    {
        var method = basis == "bill" ? AgeingMethod.ByBillDate : AgeingMethod.ByDueDate;
        var periods = Parse(spec);

        Assert.Null(Outstandings.TryWindowsFor(method, periods, out var error));
        Assert.Contains(expected, error, StringComparison.Ordinal);
        var thrown = Assert.Throws<ArgumentException>(() =>
            Outstandings.Build(Seeded(), AsOf, ageingMethod: method, ageingPeriods: periods));
        Assert.Contains(expected, thrown.Message, StringComparison.Ordinal);
    }

    private static AgeingPeriod[] Parse(string spec) => spec.Length == 0
        ? Array.Empty<AgeingPeriod>()
        : spec.Split(',').Select(p =>
        {
            var parts = p.Split('-');
            return new AgeingPeriod(int.Parse(parts[0]), parts[1].Length == 0 ? null : int.Parse(parts[1]));
        }).ToArray();
}
