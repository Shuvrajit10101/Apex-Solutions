using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Bill-wise accounting tests (catalog §5; plan.md §5, C-3): New/Agst/Advance/On-Account refs,
/// split lines, the "Σ open bills == ledger closing balance" invariant, ageing/overdue math, a
/// small AR/AP scenario, and <see cref="BillSettlementService.BuildSettlementAllocations"/> — the
/// open-bill validation that outlived the deleted Ctrl+B settlement (Phase 10.11 S2 / register row IV-5).
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

    // ---- BuildSettlementAllocations: the validation that OUTLIVED the Ctrl+B settlement ----
    //
    // These two tests used to drive BillSettlementService.SettleAndPost, which built AND POSTED a whole
    // settlement voucher from the Outstandings report's Ctrl+B binding. Phase 10.11 S2 (VL-4 / register row IV-5)
    // deleted that method: in TallyPrime Ctrl+B is "Basis of Values" and writes nothing, and settlement is now an
    // ordinary Receipt/Payment the operator confirms. BuildSettlementAllocations SURVIVED, because it is the only
    // code in the repository that checks an Agst-Ref against a genuinely open bill and caps each knock at that
    // bill's pending amount — the shell now calls it to build the pre-load AND again at Accept. So these tests are
    // rewritten onto it rather than deleted: the rule they exist to lock is unchanged, only its caller moved.
    //
    // The figures are ODD-PAISA. The originals used round 9,000 / 9,500, which cannot detect a paisa-level slip in
    // exactly the comparison this method is here to make.

    private const decimal OpenBill = 47_318.63m;   // ODD PAISA

    private static Company SeedOneOpenBill(out Domain.Ledger debtor, out Domain.Ledger cash)
    {
        var c = Seed(out cash, out var sales, out _, out debtor, out _, out var journal);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 1), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(OpenBill), DrCr.Debit,
                new[] { new BillAllocation(BillRefType.NewRef, "INV-9", Money.FromRupees(OpenBill)) }),
            new EntryLine(sales.Id, Money.FromRupees(OpenBill), DrCr.Credit),
        }));
        return c;
    }

    [Fact]
    public void BuildSettlementAllocations_turns_an_open_bill_into_an_AgstRef_allocation()
    {
        var c = SeedOneOpenBill(out var debtor, out _);
        var settle = new BillSettlementService(c);

        var allocations = settle.BuildSettlementAllocations(
            debtor, new DateOnly(2024, 4, 15),
            new[] { new BillSettlementService.Knock("INV-9", Money.FromRupees(OpenBill)) });

        var allocation = Assert.Single(allocations);
        Assert.Equal(BillRefType.AgstRef, allocation.RefType);
        Assert.Equal("INV-9", allocation.Name);
        Assert.Equal(OpenBill, allocation.Amount.Amount);

        // It is a PURE builder — nothing was posted and the bill is untouched. (Assert.Single, not
        // Assert.Equal(1, …): xUnit2013 makes the latter a build warning, and the baseline is 0 warnings.)
        Assert.Single(c.Vouchers);
        Assert.Equal(OpenBill,
            Assert.Single(Outstandings.OpenBillsFor(c, debtor, new DateOnly(2024, 4, 30))).Pending.Amount);
    }

    [Fact]
    public void BuildSettlementAllocations_allows_a_part_payment_of_an_open_bill()
    {
        var c = SeedOneOpenBill(out var debtor, out _);
        var settle = new BillSettlementService(c);

        // A part payment is legitimate and must NOT be capped away — only an amount ABOVE the pending is refused.
        var allocation = Assert.Single(settle.BuildSettlementAllocations(
            debtor, new DateOnly(2024, 4, 15),
            new[] { new BillSettlementService.Knock("INV-9", Money.FromRupees(19_999.37m)) }));

        Assert.Equal(19_999.37m, allocation.Amount.Amount);
    }

    [Fact]
    public void BuildSettlementAllocations_rejects_an_over_settlement_by_a_single_paisa()
    {
        var c = SeedOneOpenBill(out var debtor, out _);
        var settle = new BillSettlementService(c);

        // ONE PAISA over the pending — the smallest failure the money type can express.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            settle.BuildSettlementAllocations(
                debtor, new DateOnly(2024, 4, 15),
                new[] { new BillSettlementService.Knock("INV-9", Money.FromRupees(OpenBill + 0.01m)) }));
        Assert.Contains("INV-9", ex.Message);
    }

    [Fact]
    public void BuildSettlementAllocations_rejects_a_reference_that_is_not_an_open_bill()
    {
        var c = SeedOneOpenBill(out var debtor, out _);
        var settle = new BillSettlementService(c);

        // A transposed character (capital O for the zero) must not silently become an orphan allocation: the real
        // bill would stay open while Outstandings drops the non-positive orphan from the report entirely.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            settle.BuildSettlementAllocations(
                debtor, new DateOnly(2024, 4, 15),
                new[] { new BillSettlementService.Knock("INV-O", Money.FromRupees(1_000.11m)) }));
        Assert.Contains("INV-O", ex.Message);
    }

    [Fact]
    public void BuildSettlementAllocations_rejects_a_settlement_against_a_bill_that_is_already_closed()
    {
        var c = SeedOneOpenBill(out var debtor, out var cash);
        var asOf = new DateOnly(2024, 4, 30);

        // Settle the bill in full by hand, exactly as the operator now does through the pre-loaded voucher …
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), Receipt(c).Id, new DateOnly(2024, 4, 15), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(OpenBill), DrCr.Debit),
            new EntryLine(debtor.Id, Money.FromRupees(OpenBill), DrCr.Credit,
                new[] { new BillAllocation(BillRefType.AgstRef, "INV-9", Money.FromRupees(OpenBill)) }),
        }));
        Assert.Empty(Outstandings.OpenBillsFor(c, debtor, asOf));

        // … after which the reference is no longer settleable at all.
        var settle = new BillSettlementService(c);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            settle.BuildSettlementAllocations(
                debtor, asOf, new[] { new BillSettlementService.Knock("INV-9", Money.FromRupees(0.01m)) }));
        Assert.Contains("INV-9", ex.Message);
    }

    // ---- the AGGREGATE cap: two knocks naming ONE bill ------------------------------------------------
    //
    // The per-knock cap above compares each Knock against the bill's ORIGINAL pending. That is sufficient only
    // while every knock names a different bill — which was true at base, where the sole caller built knocks from
    // distinct selected report rows. Phase 10.11 S2 points this method at the operator-editable Agst-Ref rows of a
    // pre-loaded voucher (the bill Name is a free TextBox — register defect D5, and there is an "+ Add bill"
    // button beside it), so two rows can now name the SAME bill. Each row can sit under the pending while their
    // SUM sails over it, which is the exact "must not exceed its pending amount" contract this method's own doc
    // promises. Over-settling drives the bill's accumulated pending NEGATIVE, and Outstandings.OpenBillsFor drops
    // a non-positive pending — so the over-knocked bill VANISHES from the report while the party's ledger balance
    // nets out, and nothing anywhere flags it.

    [Fact]
    public void BuildSettlementAllocations_rejects_two_knocks_that_together_over_settle_one_bill()
    {
        var c = SeedOneOpenBill(out var debtor, out _);
        var settle = new BillSettlementService(c);

        // 30,000.11 + 25,000.13 = 55,000.24 against a 47,318.63 bill. Each knock alone is UNDER the pending, so
        // a per-knock cap passes both; only an aggregate cap catches it. Every figure is odd-paise.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            settle.BuildSettlementAllocations(debtor, new DateOnly(2024, 4, 15), new[]
            {
                new BillSettlementService.Knock("INV-9", Money.FromRupees(30_000.11m)),
                new BillSettlementService.Knock("INV-9", Money.FromRupees(25_000.13m)),
            }));
        Assert.Contains("INV-9", ex.Message);
    }

    [Fact]
    public void BuildSettlementAllocations_rejects_an_aggregate_over_settlement_by_a_single_paisa()
    {
        var c = SeedOneOpenBill(out var debtor, out _);
        var settle = new BillSettlementService(c);

        // The smallest aggregate failure the money type can express: the full pending PLUS one paisa, split
        // across two rows so neither one alone trips the per-knock cap.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            settle.BuildSettlementAllocations(debtor, new DateOnly(2024, 4, 15), new[]
            {
                new BillSettlementService.Knock("INV-9", Money.FromRupees(OpenBill - 0.01m)),
                new BillSettlementService.Knock("INV-9", Money.FromRupees(0.02m)),
            }));
        Assert.Contains("INV-9", ex.Message);
    }

    [Fact]
    public void BuildSettlementAllocations_still_allows_two_knocks_that_together_exactly_settle_one_bill()
    {
        var c = SeedOneOpenBill(out var debtor, out _);
        var settle = new BillSettlementService(c);

        // The aggregate cap must not become a "one row per bill" rule: splitting a knock across two rows is
        // legitimate (two remittances, one bill) as long as the TOTAL stays within the pending amount.
        // 28_411.37 + 18_907.26 == 47_318.63 to the paisa.
        var allocations = settle.BuildSettlementAllocations(debtor, new DateOnly(2024, 4, 15), new[]
        {
            new BillSettlementService.Knock("INV-9", Money.FromRupees(28_411.37m)),
            new BillSettlementService.Knock("INV-9", Money.FromRupees(18_907.26m)),
        });

        Assert.Equal(2, allocations.Count);
        Assert.Equal(OpenBill, allocations.Sum(a => a.Amount.Amount));
        Assert.All(allocations, a => Assert.Equal("INV-9", a.Name));
    }

    [Fact]
    public void BuildSettlementAllocations_aggregates_case_insensitively_matching_its_own_lookup()
    {
        var c = SeedOneOpenBill(out var debtor, out _);
        var settle = new BillSettlementService(c);

        // The open-bill dictionary is OrdinalIgnoreCase, so "inv-9" and "INV-9" resolve to the SAME bill. The
        // aggregate cap must use the same comparer or a case flip would slip the whole guard.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            settle.BuildSettlementAllocations(debtor, new DateOnly(2024, 4, 15), new[]
            {
                new BillSettlementService.Knock("INV-9", Money.FromRupees(30_000.11m)),
                new BillSettlementService.Knock("inv-9", Money.FromRupees(25_000.13m)),
            }));
        Assert.Contains("INV-9", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
