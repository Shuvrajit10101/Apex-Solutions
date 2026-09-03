using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests.Inventory;

/// <summary>
/// The <b>negative-stock policy</b> (plan.md NS-3/NS-4/NS-5). TallyPrime does <b>not</b> block negative stock
/// anywhere: the only built-in reaction is the voucher-screen F12 option <i>"Warn on Negative Stock Balance"</i>,
/// which shows the shortfall with quantity details and <b>still accepts the voucher</b>.
///
/// <para><b>Sourcing — read this before changing an assertion.</b> The licensed corpus is <b>SILENT</b>: re-grepped
/// this slice with <c>pdftotext -layout</c> over all ten PDFs in <c>tally/</c> for
/// <c>negative stock|negative balance|allow negative</c> — <b>0 hits in every one of the ten</b>. So the behaviour
/// below is <b>docs-sourced, NOT corpus-sourced</b>: official TallyHelp (<c>help.tallysolutions.com</c> —
/// <i>Configuring an Invoice</i> and the Sales FAQ). Corroborating negative evidence: third-party TDL add-ons are
/// sold specifically to ADD a hard block, which only makes sense because none is built in.</para>
///
/// <para>These tests pin the behaviour we ship in its place: <see cref="InventoryPostingService"/> <b>persists</b>
/// every posting and <b>detects</b> the resulting shortfalls instead of throwing, and the company-level
/// <see cref="Company.WarnOnNegativeStock"/> toggle defaults <b>ON</b> and is warn-only — it gates the operator
/// surface, never the engine.</para>
///
/// <para><b>Deliberately out of scope.</b> How a negative on-hand is <i>valued</i> is unresolved (eight prior
/// attempts, all reverted) and <see cref="StockValuationService"/> is untouched by this slice — no assertion here
/// claims a valuation figure for a negative quantity. The voucher-entry warning surface and the per-item
/// "Ignore negative balances?" report switch are separate, later work and are not modelled here.</para>
/// </summary>
public class NegativeStockPolicyTests
{
    private sealed class Kit
    {
        public required Company Company { get; init; }
        public required InventoryService Masters { get; init; }
        public required InventoryPostingService Posting { get; init; }
        public required InventoryLedger Ledger { get; init; }
        public required Guid ItemId { get; init; }
        public required Guid MainGodownId { get; init; }
        public required Guid SecondGodownId { get; init; }
    }

    private static readonly DateOnly D1 = new(2024, 4, 10);
    private static readonly DateOnly D2 = new(2024, 4, 20);
    private static readonly DateOnly D3 = new(2024, 5, 1);

    // Odd-paisa opening rate throughout: ₹1,234.57 — a round figure would assert nothing about the money paths
    // these movements touch.
    private static readonly Money OddRate = Money.FromRupees(1_234.57m);

    private static Kit NewKit(decimal openingQty = 10m)
    {
        var c = CompanyFactory.CreateSeeded("Negative Stock Co", new DateOnly(2024, 4, 1));
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        var wh2 = masters.CreateGodown("Warehouse 2");
        if (openingQty > 0m)
            masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, openingQty, OddRate);
        return new Kit
        {
            Company = c,
            Masters = masters,
            Posting = new InventoryPostingService(c),
            Ledger = new InventoryLedger(c),
            ItemId = item.Id,
            MainGodownId = c.MainLocation!.Id,
            SecondGodownId = wh2.Id,
        };
    }

    private static Guid TypeId(Company c, VoucherBaseType baseType) =>
        c.VoucherTypes.First(t => t.BaseType == baseType).Id;

    private static InventoryAllocation Line(Guid item, Guid godown, decimal qty, StockDirection dir,
        Money? rate = null, string? batch = null) =>
        new(item, godown, qty, dir, rate, batch);

    // ================================================================ the block is gone

    [Fact]
    public void Delivery_beyond_on_hand_is_accepted_and_drives_on_hand_negative()
    {
        // The single most common real-world Indian trading sequence — deliver today, book the supplier's purchase
        // bill next week. TallyPrime accepts it; so do we now.
        var k = NewKit(openingQty: 3m);

        var posted = k.Posting.Post(new InventoryVoucher(Guid.NewGuid(),
            TypeId(k.Company, VoucherBaseType.DeliveryNote), D1,
            new[] { Line(k.ItemId, k.MainGodownId, 5m, StockDirection.Outward, OddRate) }));

        Assert.Contains(posted, k.Company.InventoryVouchers);
        Assert.Equal(-2m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1));
    }

    [Fact]
    public void The_detector_reports_the_shortfall_and_never_throws()
    {
        var k = NewKit(openingQty: 3m);
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(),
            TypeId(k.Company, VoucherBaseType.DeliveryNote), D1,
            new[] { Line(k.ItemId, k.MainGodownId, 5m, StockDirection.Outward, OddRate) }));

        var shortfalls = k.Posting.DetectNegativeStock();

        var s = Assert.Single(shortfalls);
        Assert.Equal(k.ItemId, s.StockItemId);
        Assert.Equal("Widget", s.ItemName);
        Assert.Equal(k.MainGodownId, s.GodownId);
        Assert.Equal(D1, s.AsOf);
        Assert.Equal(-2m, s.OnHand);
        Assert.Equal(string.Empty, s.Batch);
        // The operator-facing text names the item, the godown and the shortfall (TallyHelp: "a warning message of
        // negative stock with quantity details will be displayed").
        Assert.Contains("Widget", s.Message, StringComparison.Ordinal);
        Assert.Contains("-2", s.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_book_that_never_goes_negative_reports_no_shortfall()
    {
        var k = NewKit(openingQty: 10m);
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(),
            TypeId(k.Company, VoucherBaseType.DeliveryNote), D1,
            new[] { Line(k.ItemId, k.MainGodownId, 10m, StockDirection.Outward, OddRate) }));

        Assert.Empty(k.Posting.DetectNegativeStock());
    }

    [Fact]
    public void The_detector_reports_the_EARLIEST_date_a_key_went_negative_not_every_date()
    {
        // The contract: ONE row per (item, godown, batch) key, carrying the FIRST date it went negative and the
        // on-hand at that date. Without it the same shortfall would be re-reported at every subsequent voucher
        // date in the book and the warning surface would drown the real one.
        var k = NewKit(openingQty: 3m);
        foreach (var d in new[] { D1, D2, D3 })
            k.Posting.Post(new InventoryVoucher(Guid.NewGuid(),
                TypeId(k.Company, VoucherBaseType.DeliveryNote), d,
                new[] { Line(k.ItemId, k.MainGodownId, 4m, StockDirection.Outward, OddRate) }));

        var s = Assert.Single(k.Posting.DetectNegativeStock());
        Assert.Equal(D1, s.AsOf);
        Assert.Equal(-1m, s.OnHand);          // 3 − 4 at D1, not the −9 it reaches by D3
        Assert.Equal(-9m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D3));
    }

    [Fact]
    public void Rejection_out_stock_journal_source_and_a_batch_shortfall_all_post_and_are_all_detected()
    {
        var k = NewKit(openingQty: 1m);

        // (a) Rejection Out over the on-hand.
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(),
            TypeId(k.Company, VoucherBaseType.RejectionOut), D1,
            new[] { Line(k.ItemId, k.MainGodownId, 2m, StockDirection.Outward, OddRate) }));
        Assert.Equal(-1m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1));

        // (b) Stock-Journal source over the on-hand — a BALANCED journal, so the balance rule is satisfied and only
        //     the (removed) negative block was ever in the way.
        k.Posting.Post(InventoryVoucher.StockJournal(Guid.NewGuid(),
            TypeId(k.Company, VoucherBaseType.StockJournal), D2,
            source: new[] { Line(k.ItemId, k.MainGodownId, 3m, StockDirection.Outward, OddRate) },
            destination: new[] { Line(k.ItemId, k.SecondGodownId, 3m, StockDirection.Inward, OddRate) }));
        Assert.Equal(-4m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D2));
        Assert.Equal(3m, k.Ledger.OnHand(k.ItemId, k.SecondGodownId, D2));

        // (c) The detector is batch-aware: a shortfall in batch "B" while batch "A" is positive is still reported.
        k.Masters.AddOpeningBalance(k.ItemId, k.SecondGodownId, 5m, OddRate, batchLabel: "A");
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(),
            TypeId(k.Company, VoucherBaseType.DeliveryNote), D3,
            new[] { Line(k.ItemId, k.SecondGodownId, 1m, StockDirection.Outward, OddRate, batch: "B") }));

        var b = Assert.Single(k.Posting.DetectNegativeStock(), s => s.Batch == "B");
        Assert.Equal(-1m, b.OnHand);
        Assert.Equal(k.SecondGodownId, b.GodownId);
        Assert.Contains("'B'", b.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Delete_that_retro_negatives_a_later_delivery_now_succeeds_and_is_detected()
    {
        var k = NewKit(openingQty: 0m);
        var grn = new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.ReceiptNote), D1,
            new[] { Line(k.ItemId, k.MainGodownId, 5m, StockDirection.Inward, OddRate) });
        k.Posting.Post(grn);
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.DeliveryNote), D2,
            new[] { Line(k.ItemId, k.MainGodownId, 5m, StockDirection.Outward, OddRate) }));

        k.Posting.Delete(grn.Id);                                   // no longer blocked

        Assert.Null(k.Company.FindInventoryVoucher(grn.Id));
        Assert.Equal(-5m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D2));
        Assert.Contains(k.Posting.DetectNegativeStock(), s => s.OnHand == -5m);
    }

    [Fact]
    public void Cancel_that_retro_negatives_a_later_delivery_now_succeeds_and_is_detected()
    {
        var k = NewKit(openingQty: 0m);
        var grn = new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.ReceiptNote), D1,
            new[] { Line(k.ItemId, k.MainGodownId, 5m, StockDirection.Inward, OddRate) });
        k.Posting.Post(grn);
        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.DeliveryNote), D2,
            new[] { Line(k.ItemId, k.MainGodownId, 5m, StockDirection.Outward, OddRate) }));

        k.Posting.Cancel(grn.Id);                                   // no longer blocked

        Assert.True(k.Company.FindInventoryVoucher(grn.Id)!.Cancelled);
        Assert.Equal(-5m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D2));
        Assert.Contains(k.Posting.DetectNegativeStock(), s => s.OnHand == -5m);
    }

    /// <summary>
    /// ⚠️ The bypass this slice exists to close. The old guard had FOUR call sites: Post/Cancel/Delete on
    /// <see cref="InventoryPostingService"/> called the PRIVATE <c>EnsureNoNegativeStockAnywhere</c> directly,
    /// while <see cref="LedgerService"/>'s three item-invoice paths came through a PUBLIC <c>EnsureNoNegativeStock</c>
    /// wrapper. Un-blocking only the wrapper would have left the pure-stock engine still hard-blocking; un-blocking
    /// only the private method would have left item-invoices blocked — and either half-fix compiles and looks done.
    /// This test drives the <see cref="LedgerService"/> path; the ones above drive the pure-stock path.
    /// </summary>
    [Fact]
    public void An_item_invoice_sale_beyond_on_hand_posts_through_the_public_wrapper_path()
    {
        var k = NewKit(openingQty: 2m);
        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales A/c",
            k.Company.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
        var debtor = new Domain.Ledger(Guid.NewGuid(), "Acme Ltd",
            k.Company.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, openingIsDebit: true);
        k.Company.AddLedger(sales);
        k.Company.AddLedger(debtor);

        // 5 Nos @ ₹1,234.57 = ₹6,172.85 — odd paisa on both legs.
        var amount = Money.FromRupees(6_172.85m);
        var posted = new LedgerService(k.Company).Post(
            new Voucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.Sales), D1,
                new[]
                {
                    new EntryLine(debtor.Id, amount, DrCr.Debit),
                    new EntryLine(sales.Id, amount, DrCr.Credit),
                },
                partyId: debtor.Id,
                inventoryLines: new[] { new VoucherInventoryLine(k.ItemId, k.MainGodownId, 5m, OddRate) }));

        Assert.Contains(posted, k.Company.Vouchers);
        Assert.Equal(-3m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1));
        Assert.Contains(k.Posting.DetectNegativeStock(), s => s.OnHand == -3m);
    }

    /// <summary>
    /// The consequence for the Negative Stock EXCEPTION report (catalog §16). While the block stood, that report was
    /// structurally incapable of ever showing a row on a book this engine had built — the only way in was an import.
    /// It must now be capable, and stay capable.
    /// </summary>
    [Fact]
    public void The_negative_stock_exception_report_can_now_actually_show_a_row()
    {
        var k = NewKit(openingQty: 3m);
        Assert.Empty(NegativeStock.Build(k.Company, D1).Rows);

        k.Posting.Post(new InventoryVoucher(Guid.NewGuid(),
            TypeId(k.Company, VoucherBaseType.DeliveryNote), D1,
            new[] { Line(k.ItemId, k.MainGodownId, 5m, StockDirection.Outward, OddRate) }));

        var row = Assert.Single(NegativeStock.Build(k.Company, D1).Rows);
        Assert.Equal(k.ItemId, row.StockItemId);
        Assert.Equal(-2m, row.Quantity);
    }

    // ================================================================ what is STILL rejected

    [Fact]
    public void An_unbalanced_stock_journal_is_still_rejected_and_never_persists()
    {
        // Only the negative-stock block was lifted. The Stock-Journal balance rule (ER-13) is untouched, and it is
        // the rejection the import-rollback gate now rides on.
        var k = NewKit(openingQty: 100m);
        var before = k.Company.InventoryVouchers.Count;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            k.Posting.Post(InventoryVoucher.StockJournal(Guid.NewGuid(),
                TypeId(k.Company, VoucherBaseType.StockJournal), D1,
                source: new[] { Line(k.ItemId, k.MainGodownId, 4m, StockDirection.Outward, OddRate) },
                destination: new[] { Line(k.ItemId, k.SecondGodownId, 3m, StockDirection.Inward, OddRate) })));

        Assert.Contains("balance", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, k.Company.InventoryVouchers.Count);
    }

    [Fact]
    public void A_physical_count_of_a_negative_quantity_is_still_rejected()
    {
        // A COUNT is a statement of fact about what is on the shelf; it can never be negative. Unchanged.
        var k = NewKit();
        Assert.ThrowsAny<Exception>(() =>
            k.Posting.Post(InventoryVoucher.PhysicalStock(Guid.NewGuid(),
                TypeId(k.Company, VoucherBaseType.PhysicalStock), D1,
                new[] { new PhysicalStockLine(k.ItemId, k.MainGodownId, -1m, null) })));
    }

    // ================================================================ the company toggle (schema v50)

    [Fact]
    public void Warn_on_negative_stock_defaults_to_on_for_a_new_company()
    {
        // THE DEFAULT-TRUE ASYMMETRY. Every other company flag defaults false, so "unset" and "off" coincide. This
        // one does not: an operator who never touches the switch must still be warned. Both construction paths.
        Assert.True(CompanyFactory.CreateSeeded("Fresh Co", new DateOnly(2024, 4, 1)).WarnOnNegativeStock);
        Assert.True(new Company(Guid.NewGuid(), "Bare Co", new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 1))
            .WarnOnNegativeStock);
    }

    [Fact]
    public void The_warn_toggle_never_changes_what_posts_only_whether_the_operator_is_told()
    {
        // Warn-only means warn-only: turning the switch OFF must not resurrect any block, and turning it ON must not
        // reject anything. The DETECTOR is unconditional; only the WARNING surface is gated.
        foreach (var warn in new[] { true, false })
        {
            var k = NewKit(openingQty: 3m);
            k.Company.WarnOnNegativeStock = warn;

            k.Posting.Post(new InventoryVoucher(Guid.NewGuid(),
                TypeId(k.Company, VoucherBaseType.DeliveryNote), D1,
                new[] { Line(k.ItemId, k.MainGodownId, 5m, StockDirection.Outward, OddRate) }));

            Assert.Equal(-2m, k.Ledger.OnHand(k.ItemId, k.MainGodownId, D1));
            Assert.Single(k.Posting.DetectNegativeStock());               // engine: never gated
            Assert.Equal(warn ? 1 : 0, k.Posting.NegativeStockWarnings().Count);   // surface: gated
        }
    }
}
