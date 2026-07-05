using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Exception Reports (catalog §17 report tree — "Exception Reports (Memo/Reversing/negative stock/cash)";
/// RQ-5 part 2). Four read-only projections that compose existing engines:
/// <list type="bullet">
///   <item><see cref="NegativeStock"/> — stock items whose on-hand is negative as of a date.</item>
///   <item><see cref="NegativeCashBank"/> — cash/bank ledgers whose balance is negative as of a date.</item>
///   <item><see cref="MemorandumRegister"/> — Memorandum vouchers over a period.</item>
///   <item><see cref="ReversingJournalRegister"/> — Reversing-Journal vouchers over a period.</item>
/// </list>
/// Each is pure (no UI, no DB) and honours the same as-of/cancelled conventions as the core engines.
/// </summary>
public class ExceptionReportsTests
{
    private static readonly DateOnly Start = new(2024, 4, 1);
    private static readonly DateOnly AsOf = new(2024, 4, 30);

    // ------------------------------------------------------------------ Negative Stock

    // A trading company with one item that has been driven negative on-hand, and one normal item.
    private static Company SeedInventory(out Guid negItemId, out Guid okItemId, out Guid godownId)
    {
        var c = CompanyFactory.CreateSeeded("Exc Inv Co", Start);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");

        var neg = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        neg.StandardCost = Money.FromRupees(100m); // deterministic unit cost for negative valuation
        var ok = masters.CreateStockItem("Gadget", grp.Id, nos.Id);
        ok.StandardCost = Money.FromRupees(50m);
        godownId = c.MainLocation!.Id;

        // Gadget: a normal positive on-hand (10 opening).
        masters.AddOpeningBalance(ok.Id, godownId, 10m, Money.FromRupees(50m));

        // Widget: no opening; then a raw outward Delivery of 5 units, appended directly to bypass the
        // no-negative posting guard (a real Tally file can carry negative stock via "negative stock allowed"
        // items or imported data). This drives on-hand to −5 as of D1.
        var deliveryType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.DeliveryNote);
        var outward = new InventoryVoucher(Guid.NewGuid(), deliveryType.Id, new DateOnly(2024, 4, 10),
            new[] { new InventoryAllocation(neg.Id, godownId, 5m, StockDirection.Outward) });
        c.AddInventoryVoucher(outward);

        negItemId = neg.Id;
        okItemId = ok.Id;
        return c;
    }

    [Fact]
    public void Negative_stock_lists_an_item_driven_below_zero_and_omits_a_positive_item()
    {
        var c = SeedInventory(out var negItemId, out var okItemId, out _);

        var report = NegativeStock.Build(c, AsOf);

        var row = Assert.Single(report.Rows);
        Assert.Equal(negItemId, row.StockItemId);
        Assert.Equal("Widget", row.ItemName);
        Assert.Equal(-5m, row.Quantity);
        Assert.Equal(-500m, row.Value.Amount); // −5 × ₹100 standard cost
        Assert.DoesNotContain(report.Rows, r => r.StockItemId == okItemId);
    }

    [Fact]
    public void Negative_stock_is_reported_per_godown()
    {
        var c = SeedInventory(out var negItemId, out _, out var godownId);

        var report = NegativeStock.Build(c, AsOf);

        var row = Assert.Single(report.Rows);
        Assert.Equal(godownId, row.GodownId);
        Assert.Equal("Main Location", row.GodownName);
    }

    [Fact]
    public void Negative_stock_is_empty_when_every_item_is_non_negative()
    {
        var c = CompanyFactory.CreateSeeded("Clean Inv Co", Start);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Gadget", grp.Id, nos.Id);
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, 10m, Money.FromRupees(50m));

        var report = NegativeStock.Build(c, AsOf);

        Assert.Empty(report.Rows);
    }

    // Fix B: an item whose ONLY inward is an item-invoice Purchase (no StandardCost, no rated stock-journal
    // inward, no opening) must value its negative on-hand at the item-invoice purchase rate — NOT ₹0.
    [Fact]
    public void Negative_stock_values_an_item_invoice_purchased_item_at_its_purchase_rate()
    {
        var c = CompanyFactory.CreateSeeded("Item Invoice Neg Co", Start);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Bolt", grp.Id, nos.Id); // no StandardCost, no opening balance
        var godownId = c.MainLocation!.Id;

        var purchasesGrp = c.FindGroupByName("Purchase Accounts")!;
        var creditorsGrp = c.FindGroupByName("Sundry Creditors")!;
        var purchases = new Domain.Ledger(Guid.NewGuid(), "Purchases", purchasesGrp.Id, Money.Zero, openingIsDebit: true);
        var creditor = new Domain.Ledger(Guid.NewGuid(), "Creditor", creditorsGrp.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(purchases);
        c.AddLedger(creditor);

        var svc = new LedgerService(c);
        // Item-invoice Purchase: 10 @ ₹80 = ₹800 (Dr Purchases / Cr Creditor), stock inward 10 @ ₹80.
        svc.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id,
            new DateOnly(2024, 4, 5), new[]
            {
                new EntryLine(purchases.Id, Money.FromRupees(800m), DrCr.Debit),
                new EntryLine(creditor.Id, Money.FromRupees(800m), DrCr.Credit),
            }, inventoryLines: new[] { new VoucherInventoryLine(item.Id, godownId, 10m, Money.FromRupees(80m)) }));

        // Oversell to −5 via a raw outward Delivery appended directly (bypasses the atomic no-negative guard, as
        // a real file can carry negative stock via imports / "negative stock allowed").
        var deliveryType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.DeliveryNote);
        c.AddInventoryVoucher(new InventoryVoucher(Guid.NewGuid(), deliveryType.Id, new DateOnly(2024, 4, 12),
            new[] { new InventoryAllocation(item.Id, godownId, 15m, StockDirection.Outward) }));

        var report = NegativeStock.Build(c, AsOf);

        var row = Assert.Single(report.Rows);
        Assert.Equal(item.Id, row.StockItemId);
        Assert.Equal(-5m, row.Quantity);
        Assert.Equal(-400m, row.Value.Amount); // −5 × ₹80 item-invoice purchase rate (NOT ₹0)
    }

    // ------------------------------------------------------------------ Negative Cash / Bank

    // A company with a bank ledger driven negative (OD) and a cash ledger left positive.
    private static Company SeedCashBank(out Domain.Ledger bank, out Domain.Ledger cash, out Domain.Ledger expense)
    {
        var c = CompanyFactory.CreateSeeded("Exc Cash Co", Start);

        cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(100000m);
        cash.OpeningIsDebit = true;

        var capital = new Domain.Ledger(Guid.NewGuid(), "Capital", c.FindGroupByName("Capital Account")!.Id,
            Money.FromRupees(100000m), openingIsDebit: false);
        c.AddLedger(capital);

        bank = new Domain.Ledger(Guid.NewGuid(), "HDFC Bank", c.FindGroupByName("Bank Accounts")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(bank);

        expense = new Domain.Ledger(Guid.NewGuid(), "Purchases", c.FindGroupByName("Purchase Accounts")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(expense);

        return c;
    }

    [Fact]
    public void Negative_cash_bank_lists_a_bank_driven_negative_and_omits_a_positive_cash()
    {
        var c = SeedCashBank(out var bank, out var cash, out var expense);
        var svc = new LedgerService(c);
        // Pay ₹30,000 out of the bank (opening ₹0) → bank goes Cr 30,000 (overdrawn).
        svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Payment")!.Id, new DateOnly(2024, 4, 12),
            new[]
            {
                new EntryLine(expense.Id, Money.FromRupees(30000m), DrCr.Debit),
                new EntryLine(bank.Id, Money.FromRupees(30000m), DrCr.Credit),
            }));

        var report = NegativeCashBank.Build(c, AsOf);

        var row = Assert.Single(report.Rows);
        Assert.Equal(bank.Id, row.LedgerId);
        Assert.Equal("HDFC Bank", row.LedgerName);
        Assert.Equal(DrCr.Credit, row.Balance.Side);          // negative for an asset ledger = credit balance
        Assert.Equal(30000m, row.Balance.Amount.Amount);
        Assert.Equal(AsOf, row.AsOf);
        Assert.DoesNotContain(report.Rows, r => r.LedgerId == cash.Id); // positive cash omitted
    }

    [Fact]
    public void Negative_cash_bank_is_empty_when_no_cash_or_bank_ledger_is_negative()
    {
        var c = SeedCashBank(out _, out _, out _);
        var report = NegativeCashBank.Build(c, AsOf);
        Assert.Empty(report.Rows);
    }

    // Fix A (a): a Bank OD / OCC ledger is a LIABILITY-nature facility — its credit balance is the drawn amount,
    // by design, and must NOT be flagged as a negative-cash/bank exception.
    [Fact]
    public void Negative_cash_bank_excludes_a_bank_od_occ_credit_balance()
    {
        var c = CompanyFactory.CreateSeeded("OD Co", Start);
        var cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(100000m);
        cash.OpeningIsDebit = true;

        var capital = new Domain.Ledger(Guid.NewGuid(), "Capital", c.FindGroupByName("Capital Account")!.Id,
            Money.FromRupees(100000m), openingIsDebit: false);
        c.AddLedger(capital);

        // A Bank OD A/c ledger (liability nature, under Loans (Liability)) with a credit (drawn) balance.
        var od = new Domain.Ledger(Guid.NewGuid(), "SBI Overdraft", c.FindGroupByName("Bank OD A/c")!.Id,
            Money.FromRupees(50000m), openingIsDebit: false); // Cr 50,000 drawn — normal for an OD facility
        c.AddLedger(od);

        var report = NegativeCashBank.Build(c, AsOf);

        Assert.DoesNotContain(report.Rows, r => r.LedgerId == od.Id); // OD credit balance is NOT an exception
        Assert.Empty(report.Rows);
    }

    // Fix A (b): a NORMAL Bank Account (asset nature) driven to a credit balance IS an (unintended-overdraft)
    // exception and must be listed.
    [Fact]
    public void Negative_cash_bank_lists_a_normal_bank_account_driven_credit()
    {
        var c = SeedCashBank(out var bank, out _, out var expense);
        var svc = new LedgerService(c);
        svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Payment")!.Id, new DateOnly(2024, 4, 12),
            new[]
            {
                new EntryLine(expense.Id, Money.FromRupees(30000m), DrCr.Debit),
                new EntryLine(bank.Id, Money.FromRupees(30000m), DrCr.Credit),
            }));

        var report = NegativeCashBank.Build(c, AsOf);

        var row = Assert.Single(report.Rows);
        Assert.Equal(bank.Id, row.LedgerId);
        Assert.Equal(DrCr.Credit, row.Balance.Side);
        Assert.Equal(30000m, row.Balance.Amount.Amount);
    }

    // Fix A (c): a Cash-in-Hand ledger driven to a credit (impossible) balance IS an exception and must be listed.
    [Fact]
    public void Negative_cash_bank_lists_a_cash_in_hand_driven_credit()
    {
        var c = CompanyFactory.CreateSeeded("Neg Cash Co", Start);
        var cash = c.FindLedgerByName("Cash")!; // Cash-in-Hand (asset)
        cash.OpeningBalance = Money.Zero;
        cash.OpeningIsDebit = true;

        var expense = new Domain.Ledger(Guid.NewGuid(), "Sundry Expenses", c.FindGroupByName("Indirect Expenses")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(expense);

        var svc = new LedgerService(c);
        // Pay ₹5,000 out of an empty cash box → Cash goes Cr 5,000 (impossible negative cash).
        svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Payment")!.Id, new DateOnly(2024, 4, 12),
            new[]
            {
                new EntryLine(expense.Id, Money.FromRupees(5000m), DrCr.Debit),
                new EntryLine(cash.Id, Money.FromRupees(5000m), DrCr.Credit),
            }));

        var report = NegativeCashBank.Build(c, AsOf);

        var row = Assert.Single(report.Rows);
        Assert.Equal(cash.Id, row.LedgerId);
        Assert.Equal(DrCr.Credit, row.Balance.Side);
        Assert.Equal(5000m, row.Balance.Amount.Amount);
    }

    // ------------------------------------------------------------------ Memorandum Register

    private static Company SeedMemoAndReversing(
        out Domain.Ledger rent, out Domain.Ledger provision, out Domain.Ledger cash)
    {
        var c = CompanyFactory.CreateSeeded("Exc Memo Co", Start);
        cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(500000m);
        cash.OpeningIsDebit = true;

        rent = new Domain.Ledger(Guid.NewGuid(), "Rent", c.FindGroupByName("Indirect Expenses")!.Id,
            Money.Zero, openingIsDebit: true);
        provision = new Domain.Ledger(Guid.NewGuid(), "Provision for Expenses", c.FindGroupByName("Provisions")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(rent);
        c.AddLedger(provision);
        return c;
    }

    [Fact]
    public void Memorandum_register_lists_memoranda_and_omits_a_normal_voucher()
    {
        var c = SeedMemoAndReversing(out var rent, out _, out var cash);
        var svc = new LedgerService(c);

        var memoType = c.FindVoucherTypeByName("Memorandum")!;
        var memo = new Voucher(Guid.NewGuid(), memoType.Id, new DateOnly(2024, 4, 12), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(2500m), DrCr.Debit),
            new EntryLine(cash.Id, Money.FromRupees(2500m), DrCr.Credit),
        }, number: 1, narration: "Petty cash reminder");
        svc.Post(memo);

        // A normal journal that must NOT list in the memo register.
        svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Journal")!.Id, new DateOnly(2024, 4, 15),
            new[]
            {
                new EntryLine(rent.Id, Money.FromRupees(1000m), DrCr.Debit),
                new EntryLine(cash.Id, Money.FromRupees(1000m), DrCr.Credit),
            }));

        var report = MemorandumRegister.Build(c, Start, AsOf);

        var row = Assert.Single(report.Rows);
        Assert.Equal(memo.Id, row.VoucherId);
        Assert.Equal(new DateOnly(2024, 4, 12), row.Date);
        Assert.Equal(1, row.Number);
        Assert.Equal(2500m, row.Amount.Amount);
        Assert.Equal(2500m, report.Total.Amount);
    }

    [Fact]
    public void Memorandum_register_honours_the_period_window()
    {
        var c = SeedMemoAndReversing(out var rent, out _, out var cash);
        var svc = new LedgerService(c);
        var memoType = c.FindVoucherTypeByName("Memorandum")!;

        // One memo inside the window, one after it.
        svc.Post(new Voucher(Guid.NewGuid(), memoType.Id, new DateOnly(2024, 4, 12), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(2500m), DrCr.Debit),
            new EntryLine(cash.Id, Money.FromRupees(2500m), DrCr.Credit),
        }));
        svc.Post(new Voucher(Guid.NewGuid(), memoType.Id, new DateOnly(2024, 5, 20), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(9000m), DrCr.Debit),
            new EntryLine(cash.Id, Money.FromRupees(9000m), DrCr.Credit),
        }));

        var report = MemorandumRegister.Build(c, Start, AsOf); // April only
        var row = Assert.Single(report.Rows);
        Assert.Equal(2500m, row.Amount.Amount);
    }

    // ------------------------------------------------------------------ Reversing Journal Register

    [Fact]
    public void Reversing_journal_register_lists_with_applicable_date_and_omits_a_normal_journal()
    {
        var c = SeedMemoAndReversing(out var rent, out var provision, out var cash);
        var svc = new LedgerService(c);

        var revType = c.FindVoucherTypeByName("Reversing Journal")!;
        var rev = new Voucher(Guid.NewGuid(), revType.Id, new DateOnly(2024, 4, 10), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(3000m), DrCr.Debit),
            new EntryLine(provision.Id, Money.FromRupees(3000m), DrCr.Credit),
        }, number: 1, applicableUpto: new DateOnly(2024, 4, 30));
        svc.Post(rev);

        // A normal journal that must NOT list in the reversing register.
        svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Journal")!.Id, new DateOnly(2024, 4, 15),
            new[]
            {
                new EntryLine(rent.Id, Money.FromRupees(1000m), DrCr.Debit),
                new EntryLine(provision.Id, Money.FromRupees(1000m), DrCr.Credit),
            }));

        var report = ReversingJournalRegister.Build(c, Start, AsOf);

        var row = Assert.Single(report.Rows);
        Assert.Equal(rev.Id, row.VoucherId);
        Assert.Equal(new DateOnly(2024, 4, 10), row.Date);
        Assert.Equal(new DateOnly(2024, 4, 30), row.ApplicableUpto);
        Assert.Equal(1, row.Number);
        Assert.Equal(3000m, row.Amount.Amount);
        Assert.Equal(3000m, report.Total.Amount);
    }

    [Fact]
    public void Reversing_journal_register_honours_the_period_window()
    {
        var c = SeedMemoAndReversing(out var rent, out var provision, out _);
        var svc = new LedgerService(c);
        var revType = c.FindVoucherTypeByName("Reversing Journal")!;

        svc.Post(new Voucher(Guid.NewGuid(), revType.Id, new DateOnly(2024, 4, 10), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(3000m), DrCr.Debit),
            new EntryLine(provision.Id, Money.FromRupees(3000m), DrCr.Credit),
        }, applicableUpto: new DateOnly(2024, 4, 30)));
        svc.Post(new Voucher(Guid.NewGuid(), revType.Id, new DateOnly(2024, 5, 5), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(7000m), DrCr.Debit),
            new EntryLine(provision.Id, Money.FromRupees(7000m), DrCr.Credit),
        }, applicableUpto: new DateOnly(2024, 5, 31)));

        var report = ReversingJournalRegister.Build(c, Start, AsOf); // April only
        var row = Assert.Single(report.Rows);
        Assert.Equal(3000m, row.Amount.Amount);
    }
}
