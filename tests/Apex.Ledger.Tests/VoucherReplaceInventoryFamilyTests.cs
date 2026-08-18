using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Ledger.Tests.Support;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// S5a beyond the accounting eight (design §7.4): a lifecycle test written only over Receipt/Payment/Journal
/// ships <b>green by construction</b> over the families that actually carry state. The item-invoice case is the
/// accounts+stock atomic one — it is the family where <see cref="LedgerService.Replace(Guid, Voucher)"/> must
/// re-stamp inventory-line direction, and where the derived surface includes on-hand and closing valuation.
///
/// <para><b>Recorded gap, not an omission.</b> A PURE-stock voucher (Stock Journal, Physical Stock, Delivery
/// Note, the orders) is an <c>InventoryVoucher</c>, a different aggregate list with its own posting service.
/// <c>LedgerService.Replace</c> cannot reach it and does not pretend to — the last test here pins that, so the
/// inventory twin is a visible piece of work rather than a silent hole.</para>
/// </summary>
public class VoucherReplaceInventoryFamilyTests
{
    private static readonly DateOnly Books = new(2024, 4, 1);
    private static readonly DateOnly AsOf = new(2025, 3, 31);

    private sealed class Kit
    {
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
    }

    /// <summary>
    /// Purchase 100 @ ₹1,010.33 (odd paise), then an item-invoice Sale of <paramref name="saleQuantity"/> at
    /// ₹1,412.55. The sale is the voucher the tests alter; a later, unrelated sale keeps it mid-sequence.
    /// </summary>
    private static Kit Build(decimal saleQuantity, Guid? fixedSaleId = null)
    {
        var company = CompanyFactory.CreateSeeded("Item Lifecycle Co", Books, Books);
        var masters = new InventoryService(company);
        var group = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", group.Id, nos.Id, valuationMethod: StockValuationMethod.Fifo);

        Domain.Ledger Add(string name, string groupName, bool debit) =>
            AddTo(company, name, company.FindGroupByName(groupName)!.Id, debit);

        var kit = new Kit
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

        var inward = new VoucherInventoryLine(kit.ItemId, kit.GodownId, 100m, Money.FromRupees(1010.33m));
        kit.Service.Post(new Voucher(
            Guid.NewGuid(), kit.PurchaseTypeId, Books.AddDays(2),
            new[]
            {
                new EntryLine(kit.Purchases.Id, inward.Value, DrCr.Debit),
                new EntryLine(kit.Creditor.Id, inward.Value, DrCr.Credit),
            },
            inventoryLines: new[] { inward }));

        kit.Service.Post(SaleInvoice(kit, kit.SaleVoucherId, Books.AddDays(6), saleQuantity));

        // A later, unrelated sale so the altered voucher is genuinely mid-sequence.
        kit.Service.Post(SaleInvoice(kit, Guid.NewGuid(), Books.AddDays(9), 2.5m));
        return kit;
    }

    private static Domain.Ledger AddTo(Company c, string name, Guid groupId, bool debit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, groupId, Money.Zero, openingIsDebit: debit);
        c.AddLedger(l);
        return l;
    }

    private static Voucher SaleInvoice(Kit kit, Guid id, DateOnly date, decimal quantity)
    {
        // Direction is deliberately left at the ctor default (Inward) — the posting path is what stamps
        // Outward on a Sales carrier, and Replace must do the same or the stock moves the wrong way.
        var line = new VoucherInventoryLine(kit.ItemId, kit.GodownId, quantity, Money.FromRupees(1412.55m));
        return new Voucher(
            id, kit.SalesTypeId, date,
            new[]
            {
                new EntryLine(kit.Debtor.Id, line.Value, DrCr.Debit),
                new EntryLine(kit.SalesLedger.Id, line.Value, DrCr.Credit),
            },
            narration: "item invoice",
            partyId: kit.Debtor.Id,
            inventoryLines: new[] { line });
    }

    [Fact]
    public void An_altered_item_invoice_equals_a_directly_posted_book_on_every_derived_figure()
    {
        // §7.5: 3.75 units altered to 12.125 units — 6-dp quantity, odd-paise value.
        var a = Build(3.75m);
        var b = Build(12.125m, a.SaleVoucherId);

        a.Service.Replace(a.SaleVoucherId, SaleInvoice(a, a.SaleVoucherId, Books.AddDays(6), 12.125m));

        Assert.Equal(
            DerivedStateSnapshot.Snapshot(b.Company, AsOf),
            DerivedStateSnapshot.Snapshot(a.Company, AsOf));
    }

    [Fact]
    public void Replace_restamps_the_item_line_direction_exactly_as_Post_does()
    {
        var kit = Build(3.75m);

        var accepted = kit.Service.Replace(
            kit.SaleVoucherId, SaleInvoice(kit, kit.SaleVoucherId, Books.AddDays(6), 12.125m));

        var line = Assert.Single(accepted.InventoryLines);
        Assert.Equal(StockDirection.Outward, line.Direction);

        // And the stock actually moved by the altered quantity: 100 in, 12.125 + 2.5 out.
        Assert.Equal(85.375m, new InventoryLedger(kit.Company).OnHand(kit.ItemId, AsOf));
    }

    [Fact]
    public void A_rejected_item_invoice_replacement_leaves_the_stock_untouched()
    {
        var kit = Build(3.75m);
        var before = DerivedStateSnapshot.Snapshot(kit.Company, AsOf);

        // The item line says 12.125 units but the accounting legs still carry the 3.75-unit value — the §10
        // pairing invariant refuses it.
        var mismatched = new Voucher(
            kit.SaleVoucherId, kit.SalesTypeId, Books.AddDays(6),
            new[]
            {
                new EntryLine(kit.Debtor.Id, Money.FromRupees(5297.06m), DrCr.Debit),
                new EntryLine(kit.SalesLedger.Id, Money.FromRupees(5297.06m), DrCr.Credit),
            },
            inventoryLines: new[]
            {
                new VoucherInventoryLine(kit.ItemId, kit.GodownId, 12.125m, Money.FromRupees(1412.55m)),
            });

        Assert.Throws<InvalidVoucherException>(() => kit.Service.Replace(kit.SaleVoucherId, mismatched));
        Assert.Equal(before, DerivedStateSnapshot.Snapshot(kit.Company, AsOf));
        Assert.Equal(93.75m, new InventoryLedger(kit.Company).OnHand(kit.ItemId, AsOf));
    }

    [Fact]
    public void Replace_does_not_reach_a_pure_stock_InventoryVoucher_and_says_so()
    {
        var kit = Build(3.75m);
        var inventory = new InventoryPostingService(kit.Company);
        var physicalType = kit.Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.PhysicalStock);

        var count = InventoryVoucher.PhysicalStock(
            Guid.NewGuid(), physicalType.Id, Books.AddDays(12),
            new[] { new PhysicalStockLine(kit.ItemId, kit.GodownId, 90m, null) });
        inventory.Post(count);

        // The pure-stock aggregate is a different list with its own posting service; LedgerService.Replace
        // cannot see it. Recorded, not silently tolerated: the inventory twin is future work.
        Assert.Throws<InvalidOperationException>(() => kit.Service.Replace(
            count.Id, SaleInvoice(kit, count.Id, Books.AddDays(12), 1m)));
        Assert.Single(kit.Company.InventoryVouchers);
    }
}
