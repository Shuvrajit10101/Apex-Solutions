using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Posting-service tests (design §6/§8.2): the balanced-voucher invariant, ≥2 lines,
/// positive amounts, referential integrity, opening-balance application, closing-balance
/// computation, and automatic numbering.
/// </summary>
public class PostingTests
{
    private static Company SeededWithLedgers(
        out Domain.Ledger cash, out Domain.Ledger sales, out Domain.Ledger debtor, out VoucherType receipt)
    {
        var c = CompanyFactory.CreateSeeded("Posting Co", new DateOnly(2024, 4, 1));

        cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(10000m);
        cash.OpeningIsDebit = true;

        var salesGroup = c.FindGroupByName("Sales Accounts")!;
        sales = new Domain.Ledger(Guid.NewGuid(), "Sales", salesGroup.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);

        var debtorGroup = c.FindGroupByName("Sundry Debtors")!;
        debtor = new Domain.Ledger(Guid.NewGuid(), "A Customer", debtorGroup.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(debtor);

        receipt = c.FindVoucherTypeByName("Receipt")!;
        return c;
    }

    [Fact]
    public void Unbalanced_voucher_is_rejected_and_not_persisted()
    {
        var c = SeededWithLedgers(out var cash, out var sales, out _, out var receipt);
        var svc = new LedgerService(c);

        // Dr 1000 / Cr 900 — off by 100.
        var bad = new Voucher(Guid.NewGuid(), receipt.Id, new DateOnly(2024, 4, 2), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(1000m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(900m), DrCr.Credit),
        });

        var ex = Assert.Throws<UnbalancedVoucherException>(() => svc.Post(bad));
        Assert.Equal(Money.FromRupees(1000m), ex.TotalDebit);
        Assert.Equal(Money.FromRupees(900m), ex.TotalCredit);
        Assert.Empty(c.Vouchers); // never persisted
    }

    [Fact]
    public void Single_line_voucher_is_rejected()
    {
        var c = SeededWithLedgers(out var cash, out _, out _, out var receipt);
        var svc = new LedgerService(c);

        var oneLine = new Voucher(Guid.NewGuid(), receipt.Id, new DateOnly(2024, 4, 2), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(500m), DrCr.Debit),
        });

        Assert.Throws<InvalidVoucherException>(() => svc.Post(oneLine));
        Assert.Empty(c.Vouchers);
    }

    [Fact]
    public void Zero_amount_line_is_rejected()
    {
        var c = SeededWithLedgers(out var cash, out var sales, out _, out var receipt);
        var svc = new LedgerService(c);

        var zero = new Voucher(Guid.NewGuid(), receipt.Id, new DateOnly(2024, 4, 2), new[]
        {
            new EntryLine(cash.Id, Money.Zero, DrCr.Debit),
            new EntryLine(sales.Id, Money.Zero, DrCr.Credit),
        });

        Assert.Throws<InvalidVoucherException>(() => svc.Post(zero));
        Assert.Empty(c.Vouchers);
    }

    [Fact]
    public void Voucher_dated_before_books_begin_is_rejected()
    {
        var c = SeededWithLedgers(out var cash, out var sales, out _, out var receipt);
        var svc = new LedgerService(c);

        var early = new Voucher(Guid.NewGuid(), receipt.Id, new DateOnly(2024, 3, 31), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(500m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(500m), DrCr.Credit),
        });

        Assert.Throws<InvalidVoucherException>(() => svc.Post(early));
    }

    [Fact]
    public void Balanced_voucher_posts_and_balances_compute_from_opening()
    {
        var c = SeededWithLedgers(out var cash, out var sales, out var debtor, out var receipt);
        var svc = new LedgerService(c);
        var asOf = new DateOnly(2024, 4, 30);

        // Cash sale 5000: Dr Cash / Cr Sales.
        svc.Post(new Voucher(Guid.NewGuid(), receipt.Id, new DateOnly(2024, 4, 3), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(5000m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(5000m), DrCr.Credit),
        }));

        Assert.Single(c.Vouchers);

        // Cash = opening 10000 Dr + 5000 Dr = 15000 Dr.
        var cashBal = LedgerBalances.Closing(c, cash, asOf);
        Assert.Equal(DrCr.Debit, cashBal.Side);
        Assert.Equal(Money.FromRupees(15000m), cashBal.Amount);

        // Sales = 5000 Cr.
        var salesBal = LedgerBalances.Closing(c, sales, asOf);
        Assert.Equal(DrCr.Credit, salesBal.Side);
        Assert.Equal(Money.FromRupees(5000m), salesBal.Amount);

        // Untouched debtor = 0.
        var debtorBal = LedgerBalances.Closing(c, debtor, asOf);
        Assert.Equal(Money.Zero, debtorBal.Amount);
    }

    [Fact]
    public void Automatic_numbering_increments_per_type()
    {
        var c = SeededWithLedgers(out var cash, out var sales, out _, out var receipt);
        var svc = new LedgerService(c);

        var v1 = svc.Post(new Voucher(Guid.NewGuid(), receipt.Id, new DateOnly(2024, 4, 3), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(100m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(100m), DrCr.Credit),
        }));
        var v2 = svc.Post(new Voucher(Guid.NewGuid(), receipt.Id, new DateOnly(2024, 4, 4), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(200m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(200m), DrCr.Credit),
        }));

        Assert.Equal(1, v1.Number);
        Assert.Equal(2, v2.Number);
    }

    [Fact]
    public void Cancel_keeps_number_but_zeroes_balance_effect()
    {
        var c = SeededWithLedgers(out var cash, out var sales, out _, out var receipt);
        var svc = new LedgerService(c);
        var asOf = new DateOnly(2024, 4, 30);

        var v = svc.Post(new Voucher(Guid.NewGuid(), receipt.Id, new DateOnly(2024, 4, 3), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(5000m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(5000m), DrCr.Credit),
        }));

        svc.Cancel(v.Id);

        Assert.True(v.Cancelled);
        Assert.Equal(1, v.Number); // number retained in sequence
        // Cash back to opening 10000 (cancelled voucher has zero effect).
        Assert.Equal(Money.FromRupees(10000m), LedgerBalances.Closing(c, cash, asOf).Amount);
    }

    [Fact]
    public void Delete_removes_the_voucher_entirely()
    {
        var c = SeededWithLedgers(out var cash, out var sales, out _, out var receipt);
        var svc = new LedgerService(c);

        var v = svc.Post(new Voucher(Guid.NewGuid(), receipt.Id, new DateOnly(2024, 4, 3), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(5000m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(5000m), DrCr.Credit),
        }));

        svc.Delete(v.Id);
        Assert.Empty(c.Vouchers);
    }
}
