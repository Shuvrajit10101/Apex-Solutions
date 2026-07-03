using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Bill-wise accounting tests (catalog §5; plan.md §5, C-3): New/Agst/Advance/On-Account refs,
/// split lines, the "Σ open bills == ledger closing balance" invariant, ageing/overdue math, a
/// small AR/AP scenario, and the Settle-Bill (Ctrl+B) API.
/// </summary>
public class BillWiseTests
{
    // A company with a bill-by-bill debtor (Sundry Debtors), a bill-by-bill creditor
    // (Sundry Creditors), Cash, and a Sales/Purchase ledger — enough for AR + AP.
    private static Company Seed(
        out Domain.Ledger cash,
        out Domain.Ledger sales,
        out Domain.Ledger purchases,
        out Domain.Ledger debtor,
        out Domain.Ledger creditor,
        out VoucherType journal)
    {
        var c = CompanyFactory.CreateSeeded("Bill-wise Co", new DateOnly(2024, 4, 1));

        cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(100000m);
        cash.OpeningIsDebit = true;

        sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);

        purchases = new Domain.Ledger(Guid.NewGuid(), "Purchases", c.FindGroupByName("Purchase Accounts")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(purchases);

        debtor = new Domain.Ledger(Guid.NewGuid(), "Acme Ltd", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true, maintainBillByBill: true, defaultCreditPeriodDays: 30);
        c.AddLedger(debtor);

        creditor = new Domain.Ledger(Guid.NewGuid(), "Supplier Co", c.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, openingIsDebit: false, maintainBillByBill: true, defaultCreditPeriodDays: 45);
        c.AddLedger(creditor);

        journal = c.FindVoucherTypeByName("Journal")!;
        return c;
    }

    private static VoucherType Receipt(Company c) => c.FindVoucherTypeByName("Receipt")!;
    private static VoucherType Payment(Company c) => c.FindVoucherTypeByName("Payment")!;

    // ---- master fields ----

    [Fact]
    public void Ledger_carries_bill_by_bill_and_default_credit_period()
    {
        var c = Seed(out _, out _, out _, out var debtor, out _, out _);
        Assert.True(debtor.MaintainBillByBill);
        Assert.Equal(30, debtor.DefaultCreditPeriodDays);
    }

    // ---- NewRef opens a bill ----

    [Fact]
    public void NewRef_opens_a_bill_equal_to_the_invoice()
    {
        var c = Seed(out _, out var sales, out _, out var debtor, out _, out var journal);
        var svc = new LedgerService(c);
        var asOf = new DateOnly(2024, 4, 30);

        // Credit sale 10000: Dr Acme (New Ref "INV-1") / Cr Sales.
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 1), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(10000m), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "INV-1", Money.FromRupees(10000m)),
            }),
            new EntryLine(sales.Id, Money.FromRupees(10000m), DrCr.Credit),
        }));

        var bills = Outstandings.OpenBillsFor(c, debtor, asOf);
        var bill = Assert.Single(bills);
        Assert.Equal("INV-1", bill.Reference);
        Assert.Equal(BillRefType.NewRef, bill.OpenedAs);
        Assert.Equal(Money.FromRupees(10000m), bill.Original);
        Assert.Equal(Money.FromRupees(10000m), bill.Pending);
        Assert.Equal(OutstandingKind.Receivable, bill.Kind);
        // Due date derives from credit-period days (30) since no explicit due date.
        Assert.Equal(new DateOnly(2024, 5, 1), bill.DueDate);
    }

    // ---- AgstRef knocks off ----

    [Fact]
    public void AgstRef_settles_a_pending_bill_to_zero()
    {
        var c = Seed(out var cash, out var sales, out _, out var debtor, out _, out var journal);
        var svc = new LedgerService(c);
        var asOf = new DateOnly(2024, 4, 30);

        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 1), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(10000m), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "INV-1", Money.FromRupees(10000m)),
            }),
            new EntryLine(sales.Id, Money.FromRupees(10000m), DrCr.Credit),
        }));

        // Full receipt against INV-1: Dr Cash / Cr Acme (Agst Ref "INV-1").
        svc.Post(new Voucher(Guid.NewGuid(), Receipt(c).Id, new DateOnly(2024, 4, 10), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(10000m), DrCr.Debit),
            new EntryLine(debtor.Id, Money.FromRupees(10000m), DrCr.Credit, new[]
            {
                new BillAllocation(BillRefType.AgstRef, "INV-1", Money.FromRupees(10000m)),
            }),
        }));

        Assert.Empty(Outstandings.OpenBillsFor(c, debtor, asOf)); // pending → 0, bill closed
        Assert.Equal(Money.Zero, LedgerBalances.Closing(c, debtor, asOf).Amount);
    }

    [Fact]
    public void Partial_AgstRef_leaves_remaining_pending()
    {
        var c = Seed(out var cash, out var sales, out _, out var debtor, out _, out var journal);
        var svc = new LedgerService(c);
        var asOf = new DateOnly(2024, 4, 30);

        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 1), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(10000m), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "INV-1", Money.FromRupees(10000m)),
            }),
            new EntryLine(sales.Id, Money.FromRupees(10000m), DrCr.Credit),
        }));

        svc.Post(new Voucher(Guid.NewGuid(), Receipt(c).Id, new DateOnly(2024, 4, 10), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(4000m), DrCr.Debit),
            new EntryLine(debtor.Id, Money.FromRupees(4000m), DrCr.Credit, new[]
            {
                new BillAllocation(BillRefType.AgstRef, "INV-1", Money.FromRupees(4000m)),
            }),
        }));

        var bill = Assert.Single(Outstandings.OpenBillsFor(c, debtor, asOf));
        Assert.Equal(Money.FromRupees(10000m), bill.Original);
        Assert.Equal(Money.FromRupees(6000m), bill.Pending);
    }

    // ---- Advance ----

    [Fact]
    public void Advance_opens_an_advance_bill()
    {
        var c = Seed(out var cash, out _, out _, out _, out var creditor, out _);
        var svc = new LedgerService(c);
        var asOf = new DateOnly(2024, 4, 30);

        // Advance paid to supplier: Dr Supplier (Advance "ADV-1") / Cr Cash.
        svc.Post(new Voucher(Guid.NewGuid(), Payment(c).Id, new DateOnly(2024, 4, 5), new[]
        {
            new EntryLine(creditor.Id, Money.FromRupees(3000m), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.Advance, "ADV-1", Money.FromRupees(3000m)),
            }),
            new EntryLine(cash.Id, Money.FromRupees(3000m), DrCr.Credit),
        }));

        // For a payable ledger, a debit advance is a NEGATIVE payable (we prepaid) — it nets against
        // future purchases; on its own it is not a "we owe them" open bill, so payables is empty but
        // the allocation is recorded and round-trips. Assert it does not appear as a positive payable.
        Assert.Empty(Outstandings.Build(c, asOf).Payables);
        // The advance itself reduced what we owe — reflected in the (debit) ledger balance.
        Assert.Equal(DrCr.Debit, LedgerBalances.Closing(c, creditor, asOf).Side);
    }

    [Fact]
    public void Advance_received_from_debtor_then_billed_nets_to_pending()
    {
        var c = Seed(out var cash, out var sales, out _, out var debtor, out _, out var journal);
        var svc = new LedgerService(c);
        var asOf = new DateOnly(2024, 4, 30);

        // Advance received 2000 against ref "ORD-9": Dr Cash / Cr Acme (Advance "ORD-9").
        svc.Post(new Voucher(Guid.NewGuid(), Receipt(c).Id, new DateOnly(2024, 4, 2), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(2000m), DrCr.Debit),
            new EntryLine(debtor.Id, Money.FromRupees(2000m), DrCr.Credit, new[]
            {
                new BillAllocation(BillRefType.Advance, "ORD-9", Money.FromRupees(2000m)),
            }),
        }));

        // Later invoice 5000 against same ref (Agst the advance): Dr Acme (Agst "ORD-9") / Cr Sales.
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 20), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(5000m), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.AgstRef, "ORD-9", Money.FromRupees(5000m)),
            }),
            new EntryLine(sales.Id, Money.FromRupees(5000m), DrCr.Credit),
        }));

        // Net pending on ORD-9 = 5000 invoiced − 2000 advance = 3000 receivable.
        var bill = Assert.Single(Outstandings.OpenBillsFor(c, debtor, asOf));
        Assert.Equal(Money.FromRupees(3000m), bill.Pending);
    }

    // ---- On-Account ----

    [Fact]
    public void OnAccount_is_unallocated_and_opens_no_named_bill()
    {
        var c = Seed(out var cash, out _, out _, out var debtor, out _, out _);
        var svc = new LedgerService(c);
        var asOf = new DateOnly(2024, 4, 30);

        // Receipt on account (no bill picked): Dr Cash / Cr Acme (On Account).
        svc.Post(new Voucher(Guid.NewGuid(), Receipt(c).Id, new DateOnly(2024, 4, 8), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(1500m), DrCr.Debit),
            new EntryLine(debtor.Id, Money.FromRupees(1500m), DrCr.Credit, new[]
            {
                new BillAllocation(BillRefType.OnAccount, "", Money.FromRupees(1500m)),
            }),
        }));

        // No named open bill; but the ledger balance still moved (Cr 1500).
        Assert.Empty(Outstandings.OpenBillsFor(c, debtor, asOf));
        Assert.Equal(DrCr.Credit, LedgerBalances.Closing(c, debtor, asOf).Side);
        Assert.Equal(Money.FromRupees(1500m), LedgerBalances.Closing(c, debtor, asOf).Amount);
    }

    // ---- split line across two refs ----

    [Fact]
    public void Split_line_across_two_refs_sums_to_line_amount()
    {
        var c = Seed(out _, out var sales, out _, out var debtor, out _, out var journal);
        var svc = new LedgerService(c);
        var asOf = new DateOnly(2024, 4, 30);

        // One 8000 invoice line split into two bills 5000 + 3000.
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 1), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(8000m), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "INV-A", Money.FromRupees(5000m)),
                new BillAllocation(BillRefType.NewRef, "INV-B", Money.FromRupees(3000m)),
            }),
            new EntryLine(sales.Id, Money.FromRupees(8000m), DrCr.Credit),
        }));

        var bills = Outstandings.OpenBillsFor(c, debtor, asOf);
        Assert.Equal(2, bills.Count);
        Assert.Equal(Money.FromRupees(5000m), bills.Single(b => b.Reference == "INV-A").Pending);
        Assert.Equal(Money.FromRupees(3000m), bills.Single(b => b.Reference == "INV-B").Pending);
    }

    [Fact]
    public void Split_that_does_not_sum_to_line_amount_is_rejected()
    {
        var c = Seed(out _, out var sales, out _, out var debtor, out _, out var journal);
        var svc = new LedgerService(c);

        var bad = new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 1), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(8000m), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "INV-A", Money.FromRupees(5000m)),
                new BillAllocation(BillRefType.NewRef, "INV-B", Money.FromRupees(2000m)), // 7000 ≠ 8000
            }),
            new EntryLine(sales.Id, Money.FromRupees(8000m), DrCr.Credit),
        });

        Assert.Throws<InvalidVoucherException>(() => svc.Post(bad));
        Assert.Empty(c.Vouchers);
    }

    [Fact]
    public void Bill_allocations_on_a_non_bill_by_bill_ledger_are_rejected()
    {
        var c = Seed(out _, out var sales, out _, out _, out _, out var journal);
        var svc = new LedgerService(c);

        // sales is NOT bill-by-bill.
        var bad = new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 1), new[]
        {
            new EntryLine(sales.Id, Money.FromRupees(1000m), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "X", Money.FromRupees(1000m)),
            }),
            new EntryLine(c.FindLedgerByName("Cash")!.Id, Money.FromRupees(1000m), DrCr.Credit),
        });

        Assert.Throws<InvalidVoucherException>(() => svc.Post(bad));
    }

    // ---- Σ open bills == ledger closing balance ----

    [Fact]
    public void Sum_of_open_bills_equals_ledger_closing_balance()
    {
        var c = Seed(out var cash, out var sales, out _, out var debtor, out _, out var journal);
        var svc = new LedgerService(c);
        var asOf = new DateOnly(2024, 4, 30);

        // Three invoices, one partial receipt.
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 1), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(10000m), DrCr.Debit,
                new[] { new BillAllocation(BillRefType.NewRef, "INV-1", Money.FromRupees(10000m)) }),
            new EntryLine(sales.Id, Money.FromRupees(10000m), DrCr.Credit),
        }));
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 5), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(6000m), DrCr.Debit,
                new[] { new BillAllocation(BillRefType.NewRef, "INV-2", Money.FromRupees(6000m)) }),
            new EntryLine(sales.Id, Money.FromRupees(6000m), DrCr.Credit),
        }));
        svc.Post(new Voucher(Guid.NewGuid(), Receipt(c).Id, new DateOnly(2024, 4, 12), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(3000m), DrCr.Debit),
            new EntryLine(debtor.Id, Money.FromRupees(3000m), DrCr.Credit,
                new[] { new BillAllocation(BillRefType.AgstRef, "INV-1", Money.FromRupees(3000m)) }),
        }));

        var bills = Outstandings.OpenBillsFor(c, debtor, asOf);
        var sumPending = bills.Aggregate(0m, (s, b) => s + b.Pending.Amount);
        var closing = LedgerBalances.Closing(c, debtor, asOf);

        // Debtor is debit-nature; closing magnitude equals the sum of open (receivable) bills.
        Assert.Equal(DrCr.Debit, closing.Side);
        Assert.Equal(closing.Amount.Amount, sumPending);
        Assert.Equal(13000m, sumPending); // 10000 + 6000 − 3000
    }

    // ---- ageing / overdue math ----

    [Fact]
    public void Overdue_days_and_ageing_bucket_are_correct()
    {
        var c = Seed(out _, out var sales, out _, out var debtor, out _, out var journal);
        var svc = new LedgerService(c);

        // Invoice on 1-Apr, explicit due 11-Apr (10-day term).
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 1), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(5000m), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "INV-1", Money.FromRupees(5000m),
                    dueDate: new DateOnly(2024, 4, 11)),
            }),
            new EntryLine(sales.Id, Money.FromRupees(5000m), DrCr.Credit),
        }));

        // As of 25-Apr: 14 days overdue → "0-30 days" bucket.
        var asOf = new DateOnly(2024, 4, 25);
        var bill = Assert.Single(Outstandings.OpenBillsFor(c, debtor, asOf));
        Assert.Equal(14, bill.OverdueDays(asOf));

        var report = Outstandings.Build(c, asOf);
        var bucket = report.ReceivableAgeing[Outstandings.BucketIndex(14)];
        Assert.Equal("0-30 days", bucket.Label);
        Assert.Equal(Money.FromRupees(5000m), bucket.Pending);

        // Before the due date, overdue is floored at 0 → "Not due".
        Assert.Equal(0, bill.OverdueDays(new DateOnly(2024, 4, 5)));
        Assert.Equal(0, Outstandings.BucketIndex(0));
    }

    // ---- AR / AP scenario ----

    [Fact]
    public void Small_AR_AP_scenario_splits_receivables_and_payables()
    {
        var c = Seed(out var cash, out var sales, out var purchases, out var debtor, out var creditor, out var journal);
        var svc = new LedgerService(c);
        var asOf = new DateOnly(2024, 5, 31);

        // AR: credit sale 12000 to Acme.
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 1), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(12000m), DrCr.Debit,
                new[] { new BillAllocation(BillRefType.NewRef, "S-1", Money.FromRupees(12000m)) }),
            new EntryLine(sales.Id, Money.FromRupees(12000m), DrCr.Credit),
        }));

        // AP: credit purchase 7000 from Supplier.
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 3), new[]
        {
            new EntryLine(purchases.Id, Money.FromRupees(7000m), DrCr.Debit),
            new EntryLine(creditor.Id, Money.FromRupees(7000m), DrCr.Credit,
                new[] { new BillAllocation(BillRefType.NewRef, "P-1", Money.FromRupees(7000m)) }),
        }));

        var report = Outstandings.Build(c, asOf);
        Assert.Equal(Money.FromRupees(12000m), report.TotalReceivable);
        Assert.Equal(Money.FromRupees(7000m), report.TotalPayable);
        Assert.Equal("Acme Ltd", Assert.Single(report.Receivables).LedgerName);
        Assert.Equal("Supplier Co", Assert.Single(report.Payables).LedgerName);
    }

    [Fact]
    public void Payable_bill_settles_with_agst_ref()
    {
        var c = Seed(out var cash, out _, out var purchases, out _, out var creditor, out var journal);
        var svc = new LedgerService(c);
        var asOf = new DateOnly(2024, 5, 31);

        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 3), new[]
        {
            new EntryLine(purchases.Id, Money.FromRupees(7000m), DrCr.Debit),
            new EntryLine(creditor.Id, Money.FromRupees(7000m), DrCr.Credit,
                new[] { new BillAllocation(BillRefType.NewRef, "P-1", Money.FromRupees(7000m)) }),
        }));

        // Pay 7000 against P-1: Dr Supplier (Agst "P-1") / Cr Cash.
        svc.Post(new Voucher(Guid.NewGuid(), Payment(c).Id, new DateOnly(2024, 4, 20), new[]
        {
            new EntryLine(creditor.Id, Money.FromRupees(7000m), DrCr.Debit,
                new[] { new BillAllocation(BillRefType.AgstRef, "P-1", Money.FromRupees(7000m)) }),
            new EntryLine(cash.Id, Money.FromRupees(7000m), DrCr.Credit),
        }));

        Assert.Empty(Outstandings.OpenBillsFor(c, creditor, asOf));
        Assert.Equal(Money.Zero, LedgerBalances.Closing(c, creditor, asOf).Amount);
    }

    // ---- Settle-Bill (Ctrl+B) API ----

    [Fact]
    public void SettleAndPost_knocks_off_a_receivable_via_the_settlement_service()
    {
        var c = Seed(out var cash, out var sales, out _, out var debtor, out _, out var journal);
        var svc = new LedgerService(c);
        var asOf = new DateOnly(2024, 4, 30);

        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 1), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(9000m), DrCr.Debit,
                new[] { new BillAllocation(BillRefType.NewRef, "INV-9", Money.FromRupees(9000m)) }),
            new EntryLine(sales.Id, Money.FromRupees(9000m), DrCr.Credit),
        }));

        var settle = new BillSettlementService(c);
        settle.SettleAndPost(
            debtor, cash, Receipt(c).Id, new DateOnly(2024, 4, 15),
            new[] { new BillSettlementService.Knock("INV-9", Money.FromRupees(9000m)) });

        Assert.Empty(Outstandings.OpenBillsFor(c, debtor, asOf));
        Assert.Equal(Money.Zero, LedgerBalances.Closing(c, debtor, asOf).Amount);
        // Cash rose from opening 100000 by the 9000 received.
        Assert.Equal(Money.FromRupees(109000m), LedgerBalances.Closing(c, cash, asOf).Amount);
    }

    [Fact]
    public void SettleAndPost_rejects_over_settlement()
    {
        var c = Seed(out var cash, out var sales, out _, out var debtor, out _, out var journal);
        var svc = new LedgerService(c);

        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 1), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(9000m), DrCr.Debit,
                new[] { new BillAllocation(BillRefType.NewRef, "INV-9", Money.FromRupees(9000m)) }),
            new EntryLine(sales.Id, Money.FromRupees(9000m), DrCr.Credit),
        }));

        var settle = new BillSettlementService(c);
        Assert.Throws<InvalidOperationException>(() =>
            settle.SettleAndPost(
                debtor, cash, Receipt(c).Id, new DateOnly(2024, 4, 15),
                new[] { new BillSettlementService.Knock("INV-9", Money.FromRupees(9500m)) }));
    }
}
