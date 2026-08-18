using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Tests.Support;

/// <summary>
/// The item-invoice fixture for the phase-10-11 lifecycle suite (design §7.4 — "the accounts+stock atomic
/// case"): a Purchase of 100 @ ₹1,010.33, an item-invoice Sale that the tests alter, and a later unrelated sale
/// so the altered voucher is genuinely mid-sequence.
///
/// <para>Shared rather than nested, because three test classes need it: the family equivalence tests, the
/// rejected-replacement direction guard, and the Physical-Stock-count case §7.4 calls
/// <i>"the single nastiest family in the phase"</i>.</para>
/// </summary>
public sealed class ItemInvoiceBook
{
    public static readonly DateOnly Books = new(2024, 4, 1);
    public static readonly DateOnly AsOf = new(2025, 3, 31);
    public static readonly DateOnly SaleDate = Books.AddDays(6);

    public required Company Company { get; init; }
    public required LedgerService Service { get; init; }
    public required Guid ItemId { get; init; }
    public required Guid GodownId { get; init; }
    public required Domain.Ledger Purchases { get; init; }
    public required Domain.Ledger SalesLedger { get; init; }
    public required Domain.Ledger Creditor { get; init; }
    public required Domain.Ledger Debtor { get; init; }
    public required Guid PurchaseTypeId { get; init; }
    public required Guid SalesTypeId { get; init; }
    public required Guid SaleVoucherId { get; init; }

    /// <summary>
    /// <paramref name="physicalCount"/> posts a Physical Stock count AFTER the altered sale.
    /// <c>InventoryLedger</c> treats a count as a CHECKPOINT that RESETS the running balance, so every figure
    /// downstream of it is blind to a quantity change upstream — which is exactly what the §7.2 equivalence
    /// instrument has to be able to see.
    /// </summary>
    public static ItemInvoiceBook Build(
        decimal saleQuantity = 3.75m, Guid? fixedSaleId = null, decimal? physicalCount = null, Money? saleRate = null)
    {
        var company = CompanyFactory.CreateSeeded("Item Lifecycle Co", Books, Books);
        var masters = new InventoryService(company);
        var group = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", group.Id, nos.Id, valuationMethod: StockValuationMethod.Fifo);

        Domain.Ledger Add(string name, string groupName, bool debit)
        {
            var l = new Domain.Ledger(
                Guid.NewGuid(), name, company.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit: debit);
            company.AddLedger(l);
            return l;
        }

        var book = new ItemInvoiceBook
        {
            Company = company,
            Service = new LedgerService(company),
            ItemId = item.Id,
            GodownId = company.MainLocation!.Id,
            Purchases = Add("Purchases", "Purchase Accounts", true),
            SalesLedger = Add("Sales", "Sales Accounts", false),
            Creditor = Add("Creditor", "Sundry Creditors", false),
            Debtor = Add("Debtor", "Sundry Debtors", true),
            PurchaseTypeId = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id,
            SalesTypeId = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id,
            SaleVoucherId = fixedSaleId ?? Guid.NewGuid(),
        };

        var inward = new VoucherInventoryLine(book.ItemId, book.GodownId, 100m, Money.FromRupees(1010.33m));
        book.Service.Post(new Voucher(
            Guid.NewGuid(), book.PurchaseTypeId, Books.AddDays(2),
            new[]
            {
                new EntryLine(book.Purchases.Id, inward.Value, DrCr.Debit),
                new EntryLine(book.Creditor.Id, inward.Value, DrCr.Credit),
            },
            inventoryLines: new[] { inward }));

        book.Service.Post(SaleInvoice(
            book, book.SaleVoucherId, SaleDate, saleQuantity, saleRate ?? Money.FromRupees(1412.55m)));

        // A later, unrelated sale so the altered voucher is genuinely mid-sequence.
        book.Service.Post(SaleInvoice(book, Guid.NewGuid(), Books.AddDays(9), 2.5m));

        if (physicalCount is { } counted)
        {
            var physicalType = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.PhysicalStock);
            new InventoryPostingService(company).Post(InventoryVoucher.PhysicalStock(
                Guid.NewGuid(), physicalType.Id, Books.AddDays(11),
                new[] { new PhysicalStockLine(book.ItemId, book.GodownId, counted, null) }));
        }

        return book;
    }

    /// <summary>
    /// Direction is deliberately left at the ctor default (Inward) — the posting path is what stamps Outward on a
    /// Sales carrier, and Replace must do the same or the stock moves the wrong way.
    /// </summary>
    public static Voucher SaleInvoice(ItemInvoiceBook book, Guid id, DateOnly date, decimal quantity) =>
        SaleInvoice(book, id, date, quantity, Money.FromRupees(1412.55m));

    public static Voucher SaleInvoice(ItemInvoiceBook book, Guid id, DateOnly date, decimal quantity, Money rate)
    {
        var line = new VoucherInventoryLine(book.ItemId, book.GodownId, quantity, rate);
        return new Voucher(
            id, book.SalesTypeId, date,
            new[]
            {
                new EntryLine(book.Debtor.Id, line.Value, DrCr.Debit),
                new EntryLine(book.SalesLedger.Id, line.Value, DrCr.Credit),
            },
            narration: "item invoice",
            partyId: book.Debtor.Id,
            inventoryLines: new[] { line });
    }
}
