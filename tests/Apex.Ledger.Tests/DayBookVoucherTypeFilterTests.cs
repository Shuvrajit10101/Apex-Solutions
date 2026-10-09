using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>Census 11.4 gap (a) — the Day Book's F4 (Voucher Type) filter.</b> The ENGINE half; the realised-window
/// half is <c>Apex.Desktop.Tests.DayBookVoucherTypeFilterTests</c>.
///
/// <para><b>FIDELITY (R7 / ruling 14), opened by content on 2026-10-09.</b>
/// <i>help.tallysolutions.com/tally-prime/accounting-financial-reports/day-book-tally/</i>:
/// <i>"Day Book &gt; F4 (Voucher Type), and select the Debit Note voucher type."</i> and <i>"Press F4 (Voucher
/// Type) &gt; Purchase."</i> The vendor's generic report page agrees F4 is the per-report context key —
/// <i>"F4 — This button name differs based on the report you are viewing"</i>
/// (<i>help.tallysolutions.com/working-with-reports/</i>).</para>
///
/// <para>🔴 <b>THE TWO-AGGREGATE CLAUSE IS WHY THIS FILE EXISTS SEPARATELY FROM THE UI ONE.</b> A voucher TYPE
/// spans <c>Company.Vouchers</c> and <c>Company.InventoryVouchers</c>, so a filter written into the first loop
/// only returns an EMPTY book for a type whose vouchers all exist — "you have no Delivery Notes" on a company
/// full of them. That is the defect class census rows 4.9–4.16 record, and a filter is an easy place to
/// re-commit it.</para>
/// </summary>
public class DayBookVoucherTypeFilterTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly On = new(2025, 4, 10);
    private static readonly DateOnly AsOf = new(2026, 3, 31);

    private sealed record Book(Company Company, Guid ItemId, Guid GodownId);

    /// <summary>
    /// A seeded company carrying FOUR vouchers across THREE types and BOTH aggregates:
    /// two Payments (901, 902), one Receipt (903) and one pure-stock Receipt Note (904).
    /// </summary>
    private static Book Seed()
    {
        var c = CompanyFactory.CreateSeeded("F4 Filter Co", FyStart);
        var inv = new InventoryService(c);
        var unit = inv.CreateSimpleUnit("Nos", "Numbers");
        var group = c.FindStockGroupByName("Primary") ?? inv.CreateStockGroup("Primary");
        var item = inv.CreateStockItem("Widget", group.Id, unit.Id);
        var book = new Book(c, item.Id, c.MainLocation!.Id);

        var cash = c.FindLedgerByName("Cash")!;
        var expense = new Apex.Ledger.Domain.Ledger(
            Guid.NewGuid(), "Sundry Expense", c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, true);
        c.AddLedger(expense);

        var payment = c.FindVoucherTypeByName("Payment")!;
        var receipt = c.FindVoucherTypeByName("Receipt")!;
        var svc = new LedgerService(c);

        Voucher Make(int number, Guid typeId) => new(
            Guid.NewGuid(), typeId, On,
            new[]
            {
                new EntryLine(expense.Id, new Money(500m), DrCr.Debit),
                new EntryLine(cash.Id, new Money(500m), DrCr.Credit),
            },
            number: number);

        svc.Post(Make(901, payment.Id));
        svc.Post(Make(902, payment.Id));
        svc.Post(Make(903, receipt.Id));

        var receiptNote = c.FindVoucherTypeByName("Receipt Note")!;
        new InventoryPostingService(c).Post(new InventoryVoucher(
            Guid.NewGuid(), receiptNote.Id, On,
            new[] { new InventoryAllocation(book.ItemId, book.GodownId, 10m, StockDirection.Inward, Money.FromRupees(50m)) },
            number: 904));

        return book;
    }

    private static int[] Numbers(IReadOnlyList<DayBookRow> rows)
    {
        var ns = rows.Select(r => r.Number).ToList();
        ns.Sort();
        return ns.ToArray();
    }

    /// <summary>The default (no filter) is the vendor's unfiltered "All Vouchers" book — all four list.</summary>
    [Fact]
    public void No_filter_lists_every_voucher_of_every_type_in_both_aggregates()
    {
        var b = Seed();
        Assert.Equal(new[] { 901, 902, 903, 904 }, Numbers(DayBook.Build(b.Company, FyStart, AsOf)));
    }

    /// <summary>F4 &gt; Payment keeps the two Payments and drops the Receipt and the Receipt Note.</summary>
    [Fact]
    public void Filtering_to_one_accounting_type_keeps_only_that_types_vouchers()
    {
        var b = Seed();
        var payment = b.Company.FindVoucherTypeByName("Payment")!;

        var rows = DayBook.Build(b.Company, FyStart, AsOf, payment.Id);

        Assert.Equal(new[] { 901, 902 }, Numbers(rows));
        Assert.All(rows, r => Assert.Equal(payment.Id, r.VoucherTypeId));
    }

    /// <summary>
    /// 🔴 <b>THE SECOND-AGGREGATE ASSERTION.</b> A Receipt Note lives in <c>Company.InventoryVouchers</c>, so a
    /// filter applied to the accounting loop alone returns NOTHING here — an empty register that reads as "you
    /// have none" about a voucher the operator posted.
    /// </summary>
    [Fact]
    public void Filtering_to_a_pure_stock_type_keeps_the_voucher_from_the_other_aggregate()
    {
        var b = Seed();
        var receiptNote = b.Company.FindVoucherTypeByName("Receipt Note")!;

        var row = Assert.Single(DayBook.Build(b.Company, FyStart, AsOf, receiptNote.Id));

        Assert.Equal(904, row.Number);
        Assert.True(row.IsInventory, "the filtered row must still declare which aggregate its id addresses");
        Assert.Equal(receiptNote.Id, row.VoucherTypeId);
    }

    /// <summary>
    /// The filter is by TYPE ID, not by base kind: a SECOND Payment series must be separable from the first.
    /// Filtering on the base would silently fold two series the operator chose between into one listing.
    /// </summary>
    [Fact]
    public void Filtering_separates_two_types_that_share_one_base_kind()
    {
        var b = Seed();
        var c = b.Company;
        var second = new VoucherTypeService(c).Create("Petty Payment", VoucherBaseType.Payment, NumberingMethod.Automatic);
        var cash = c.FindLedgerByName("Cash")!;
        var expense = c.FindLedgerByName("Sundry Expense")!;

        new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), second.Id, On,
            new[]
            {
                new EntryLine(expense.Id, new Money(60m), DrCr.Debit),
                new EntryLine(cash.Id, new Money(60m), DrCr.Credit),
            },
            number: 905));

        var firstPayment = c.FindVoucherTypeByName("Payment")!;
        Assert.Equal(VoucherBaseType.Payment, second.BaseType);
        Assert.Equal(VoucherBaseType.Payment, firstPayment.BaseType);

        Assert.Equal(new[] { 905 }, Numbers(DayBook.Build(c, FyStart, AsOf, second.Id)));
        Assert.Equal(new[] { 901, 902 }, Numbers(DayBook.Build(c, FyStart, AsOf, firstPayment.Id)));
    }

    /// <summary>
    /// A filter for a type with no vouchers in the window returns an EMPTY list rather than throwing or
    /// falling back to the unfiltered book — the "falls open on no match" failure mode, which would show the
    /// operator every voucher under a header claiming one type.
    /// </summary>
    [Fact]
    public void A_type_with_no_vouchers_in_the_window_yields_an_empty_book_not_the_whole_one()
    {
        var b = Seed();
        var contra = b.Company.FindVoucherTypeByName("Contra")!;

        Assert.Empty(DayBook.Build(b.Company, FyStart, AsOf, contra.Id));
    }

    /// <summary>The date window still applies under a filter — the two narrowings compose rather than replace.</summary>
    [Fact]
    public void The_period_window_still_applies_under_a_voucher_type_filter()
    {
        var b = Seed();
        var payment = b.Company.FindVoucherTypeByName("Payment")!;
        var before = On.AddDays(-5);

        Assert.Empty(DayBook.Build(b.Company, FyStart, before, payment.Id));
        Assert.Equal(new[] { 901, 902 }, Numbers(DayBook.Build(b.Company, FyStart, AsOf, payment.Id)));
    }

    /// <summary>
    /// Every row carries its own <c>VoucherTypeId</c> and <c>Narration</c>, in BOTH aggregates — the two fields
    /// the filter and the F12 Show-narration knob read. A row whose type id were left at
    /// <see cref="System.Guid.Empty"/> would be invisible to every filter.
    /// </summary>
    [Fact]
    public void Every_row_carries_its_voucher_type_id_in_both_aggregates()
    {
        var b = Seed();
        var rows = DayBook.Build(b.Company, FyStart, AsOf);

        Assert.Contains(rows, r => r.IsInventory);
        Assert.Contains(rows, r => !r.IsInventory);
        Assert.All(rows, r =>
        {
            Assert.NotEqual(Guid.Empty, r.VoucherTypeId);
            Assert.NotNull(b.Company.FindVoucherType(r.VoucherTypeId));
        });
    }

    /// <summary>
    /// A Ctrl+J exception register honours the Day Book's narrowing, because it is built by filtering the Day
    /// Book and its own contract is never to disagree with the book it was opened from. The CANCELLED register
    /// is used because <c>Cancelled</c> is the one flag the accounting aggregate carries on construction.
    /// </summary>
    [Fact]
    public void An_exception_register_honours_the_voucher_type_narrowing()
    {
        var b = Seed();
        var c = b.Company;
        var cash = c.FindLedgerByName("Cash")!;
        var expense = c.FindLedgerByName("Sundry Expense")!;
        var payment = c.FindVoucherTypeByName("Payment")!;
        var receipt = c.FindVoucherTypeByName("Receipt")!;
        var svc = new LedgerService(c);

        Voucher Cancelled(int number, Guid typeId) => new(
            Guid.NewGuid(), typeId, On,
            new[]
            {
                new EntryLine(expense.Id, new Money(900m), DrCr.Debit),
                new EntryLine(cash.Id, new Money(900m), DrCr.Credit),
            },
            number: number, narration: null, partyId: null, cancelled: true);

        svc.Post(Cancelled(911, payment.Id));
        svc.Post(Cancelled(912, receipt.Id));

        Assert.Equal(
            new[] { 911, 912 },
            Numbers(ExceptionVouchers.Build(c, ExceptionVoucherKind.Cancelled, FyStart, AsOf)));

        Assert.Equal(
            new[] { 911 },
            Numbers(ExceptionVouchers.Build(c, ExceptionVoucherKind.Cancelled, FyStart, AsOf, payment.Id)));
    }
}
