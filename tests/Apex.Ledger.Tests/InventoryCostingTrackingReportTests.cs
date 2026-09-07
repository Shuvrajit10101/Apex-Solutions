using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// W-K1 — the engines behind census rows <b>9.8</b> (Tracking Numbers / Bills Pending), <b>9.7</b> (Item Cost
/// Tracking), <b>9.6</b> (Job Costing / Job Work Analysis) and <b>9.9</b> (Stock Journal transfer class).
///
/// <para>🔴 <b>The load-bearing tests are the three Bills Pending cases.</b> Row 9.8's correctness property is
/// that a receipted-but-unbilled quantity appears as such and must NOT double-count once the bill arrives. That
/// is three distinct states — nothing billed, PART billed, ALL billed — and an implementation that lists
/// movements and bills separately passes the first and fails the other two. Testing only the first is the shape
/// of test that ships a broken report green.</para>
/// </summary>
public sealed class InventoryCostingTrackingReportTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly AsOf = new(2026, 3, 31);

    // ═══════════════════════════════════════════════ census 9.8 — Bills Pending

    /// <summary>Goods received under a tracking number with NO bill ⇒ the full quantity is pending, once, under
    /// the vendor's "Goods Recd. but Bills not Recd." section. Fails on today's main, where the report does not
    /// exist.</summary>
    [Fact]
    public void Goods_received_with_no_bill_are_pending_in_full()
    {
        var b = new Fixture("Pending Co");
        b.ReceiptNote("TRK/1", 40m);

        var row = Assert.Single(BillsPending.BuildPurchase(b.Company, AsOf));
        Assert.Equal(BillsPendingSection.GoodsNotBilled, row.Section);
        Assert.Equal("TRK/1", row.TrackingNumber);
        Assert.Equal(40m, row.ReceivedQuantity);
        Assert.Equal(0m, row.BilledQuantity);
        Assert.Equal(40m, row.PendingQuantity);
    }

    /// <summary>
    /// 🔴 <b>THE DOUBLE-COUNT CASE.</b> A PART bill arrives for 25 of the 40 received. Only the 15-unit
    /// remainder may be pending — an implementation that keeps listing the whole receipt reports 40, and one that
    /// lists the receipt and the bill as separate pending items reports two rows totalling 65.
    /// </summary>
    [Fact]
    public void A_part_bill_leaves_only_the_unbilled_remainder_pending()
    {
        var b = new Fixture("Part Bill Co");
        b.ReceiptNote("TRK/1", 40m);
        b.PurchaseBill("TRK/1", 25m);

        var row = Assert.Single(BillsPending.BuildPurchase(b.Company, AsOf));
        Assert.Equal(BillsPendingSection.GoodsNotBilled, row.Section);
        Assert.Equal(40m, row.ReceivedQuantity);
        Assert.Equal(25m, row.BilledQuantity);
        Assert.Equal(15m, row.PendingQuantity);
    }

    /// <summary>
    /// 🔴 <b>THE OTHER HALF OF THE DOUBLE-COUNT CASE, and the one a naive report gets wrong.</b> Once the bill
    /// covers the whole receipt, NOTHING is pending — not a zero row, not two cancelling rows, nothing at all.
    /// </summary>
    [Fact]
    public void A_full_bill_clears_the_pending_item_entirely()
    {
        var b = new Fixture("Full Bill Co");
        b.ReceiptNote("TRK/1", 40m);
        b.PurchaseBill("TRK/1", 40m);

        Assert.Empty(BillsPending.BuildPurchase(b.Company, AsOf));
    }

    /// <summary>The reverse end: a bill arrives before the goods ⇒ the quantity appears once under "Bills Recd.
    /// but Goods not Recd.", and the section — not just the figure — must be the right one.</summary>
    [Fact]
    public void A_bill_ahead_of_the_goods_is_pending_on_the_other_side()
    {
        var b = new Fixture("Early Bill Co");
        b.PurchaseBill("TRK/9", 30m);

        var row = Assert.Single(BillsPending.BuildPurchase(b.Company, AsOf));
        Assert.Equal(BillsPendingSection.BilledNotReceived, row.Section);
        Assert.Equal(0m, row.ReceivedQuantity);
        Assert.Equal(30m, row.BilledQuantity);
        Assert.Equal(30m, row.PendingQuantity);
    }

    /// <summary>Two different tracking numbers do not net against each other — the whole point of keying a
    /// reference is that it identifies ONE consignment. A report that grouped by item alone would show these two
    /// as reconciled and report nothing.</summary>
    [Fact]
    public void Two_tracking_numbers_are_reconciled_independently()
    {
        var b = new Fixture("Two Track Co");
        b.ReceiptNote("TRK/1", 40m);
        b.PurchaseBill("TRK/2", 40m);

        var rows = BillsPending.BuildPurchase(b.Company, AsOf);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.TrackingNumber == "TRK/1" && r.Section == BillsPendingSection.GoodsNotBilled);
        Assert.Contains(rows, r => r.TrackingNumber == "TRK/2" && r.Section == BillsPendingSection.BilledNotReceived);
    }

    /// <summary>🔴 With the F11 feature OFF the report is empty even though the data is present. Stale tracking
    /// numbers from a period when the flag was on must not put pending items on screen the operator has no
    /// field to clear.</summary>
    [Fact]
    public void With_the_feature_off_the_report_is_empty()
    {
        var b = new Fixture("Feature Off Co");
        b.ReceiptNote("TRK/1", 40m);
        b.Company.UseTrackingNumbers = false;

        Assert.Empty(BillsPending.BuildPurchase(b.Company, AsOf));
    }

    /// <summary>A CANCELLED bill does not reconcile anything — the goods go back to pending in full. Excluding
    /// cancelled vouchers is the same rule every other register applies, and forgetting it here would let a
    /// cancelled document silently discharge a real obligation.</summary>
    [Fact]
    public void A_cancelled_bill_does_not_reconcile_the_goods()
    {
        var b = new Fixture("Cancelled Bill Co");
        b.ReceiptNote("TRK/1", 40m);
        b.PurchaseBill("TRK/1", 40m, cancelled: true);

        var row = Assert.Single(BillsPending.BuildPurchase(b.Company, AsOf));
        Assert.Equal(40m, row.PendingQuantity);
        Assert.Equal(0m, row.BilledQuantity);
    }

    /// <summary>The Delivery Note ↔ Sales twin runs the same netting on the outward side, and the Purchase
    /// report must NOT pick up sales-side documents (or one operator's delivery would clear another's receipt).</summary>
    [Fact]
    public void The_sales_report_nets_delivery_notes_against_sales_bills()
    {
        var b = new Fixture("Sales Pending Co");
        b.DeliveryNote("DLV/1", 12m);

        Assert.Empty(BillsPending.BuildPurchase(b.Company, AsOf));
        var row = Assert.Single(BillsPending.BuildSales(b.Company, AsOf));
        Assert.Equal(BillsPendingSection.GoodsNotBilled, row.Section);
        Assert.Equal(12m, row.PendingQuantity);
    }

    // ═══════════════════════════════════════════════ census 9.7 — Item Cost Analysis

    /// <summary>
    /// A lot bought 100 @ ₹10 (₹1,000) and half of it sold 50 @ ₹14 (₹700). Cost ₹1,000, Revenue ₹700, Balance
    /// at Cost ₹500 (the 50 still on hand at the lot's own ₹10), Profit ₹700 − (₹1,000 − ₹500) = <b>₹200</b>.
    /// <para>🔴 Omitting the Balance-at-Cost subtraction would report a ₹300 LOSS on a lot that has made ₹200 —
    /// the single most consequential arithmetic choice in this report.</para>
    /// </summary>
    [Fact]
    public void Item_cost_analysis_subtracts_the_balance_at_cost_from_the_cost_of_sales()
    {
        var b = new Fixture("Cost Track Co");
        b.PurchaseBill(null, 100m, rate: 10m, costTracking: "LOT/1");
        b.SalesBill(null, 50m, rate: 14m, costTracking: "LOT/1");

        var row = Assert.Single(ItemCostAnalysis.BuildItems(b.Company, AsOf));
        Assert.Equal(Money.FromRupees(1000m), row.Cost);
        Assert.Equal(Money.FromRupees(700m), row.Revenue);
        Assert.Equal(Money.FromRupees(500m), row.BalanceAtCost);
        Assert.Equal(Money.FromRupees(200m), row.ProfitOrLoss);
        Assert.Equal(100m, row.InwardQuantity);
        Assert.Equal(50m, row.OutwardQuantity);
    }

    /// <summary>A fully sold lot has NO balance left, so its profit is the plain revenue less cost.</summary>
    [Fact]
    public void A_fully_sold_lot_has_no_balance_at_cost()
    {
        var b = new Fixture("Sold Out Co");
        b.PurchaseBill(null, 100m, rate: 10m, costTracking: "LOT/1");
        b.SalesBill(null, 100m, rate: 14m, costTracking: "LOT/1");

        var row = Assert.Single(ItemCostAnalysis.BuildItems(b.Company, AsOf));
        Assert.Equal(Money.Zero, row.BalanceAtCost);
        Assert.Equal(Money.FromRupees(400m), row.ProfitOrLoss);
    }

    /// <summary>Cost Track Break-up is the per-lot grain: two lots of one item are two rows, and the item-level
    /// report rolls the same figures into one. A break-up that merged the lots would make the feature pointless.</summary>
    [Fact]
    public void Cost_track_breakup_is_per_lot_and_the_item_report_rolls_them_up()
    {
        var b = new Fixture("Two Lot Co");
        b.PurchaseBill(null, 100m, rate: 10m, costTracking: "LOT/1");
        b.PurchaseBill(null, 50m, rate: 20m, costTracking: "LOT/2");

        var breakup = ItemCostAnalysis.BuildBreakup(b.Company, AsOf);
        Assert.Equal(2, breakup.Count);
        Assert.Contains(breakup, r => r.TrackingNumber == "LOT/1" && r.Cost == Money.FromRupees(1000m));
        Assert.Contains(breakup, r => r.TrackingNumber == "LOT/2" && r.Cost == Money.FromRupees(1000m));

        var item = Assert.Single(ItemCostAnalysis.BuildItems(b.Company, AsOf));
        Assert.Equal(Money.FromRupees(2000m), item.Cost);
        Assert.Equal(150m, item.InwardQuantity);
    }

    /// <summary>🔴 With F11 "Enable Cost Tracking" off, every Item Cost Analysis report is empty — same reason as
    /// the Bills Pending gate.</summary>
    [Fact]
    public void With_cost_tracking_off_every_item_cost_report_is_empty()
    {
        var b = new Fixture("Cost Off Co");
        b.PurchaseBill(null, 100m, rate: 10m, costTracking: "LOT/1");
        b.Company.EnableCostTracking = false;

        Assert.Empty(ItemCostAnalysis.BuildItems(b.Company, AsOf));
        Assert.Empty(ItemCostAnalysis.BuildGroups(b.Company, AsOf));
        Assert.Empty(ItemCostAnalysis.BuildBreakup(b.Company, AsOf));
    }

    // ═══════════════════════════════════════════════ census 9.6 — Job Work Analysis

    /// <summary>
    /// A godown designated a job/project reports the Revenue (Income) and Cost (Expenses) allocated to its cost
    /// centre, and the Nett Profit/Loss between them.
    /// <para>🔴 The classification is by the LEDGER'S nature, not by the allocation's sign: both allocations here
    /// are positive magnitudes, and only the ledger's group says which is income.</para>
    /// </summary>
    [Fact]
    public void Job_work_analysis_splits_revenue_and_cost_by_the_ledgers_nature()
    {
        var b = new Fixture("Job Co");
        var centre = b.JobProject("Bridge Project", "Site A");
        b.AllocateToCostCentre(centre, b.SalesLedger, Money.FromRupees(9000m), income: true);
        b.AllocateToCostCentre(centre, b.ExpenseLedger, Money.FromRupees(3500m), income: false);

        var job = Assert.Single(JobWorkAnalysis.Build(b.Company, FyStart, AsOf));
        Assert.Equal("Bridge Project", job.JobName);
        Assert.Equal(new[] { "Site A" }, job.GodownNames);
        Assert.Equal(Money.FromRupees(9000m), job.TotalRevenue);
        Assert.Equal(Money.FromRupees(3500m), job.TotalCost);
        Assert.Equal(Money.FromRupees(5500m), job.NettProfit);
    }

    /// <summary>A cost centre NO godown designates is an ordinary cost centre and is not a job — the membership
    /// rule is the godown link, and without it every cost centre in the book would appear as a project.</summary>
    [Fact]
    public void A_cost_centre_no_godown_designates_is_not_a_job()
    {
        var b = new Fixture("Not A Job Co");
        var plain = b.CostCentre("Marketing");
        b.AllocateToCostCentre(plain, b.ExpenseLedger, Money.FromRupees(500m), income: false);

        Assert.Empty(JobWorkAnalysis.Build(b.Company, FyStart, AsOf));
    }

    /// <summary>A job spread over two sites is ONE row naming both godowns — the vendor's explicit case. Two
    /// rows would double-count the job's money on any total.</summary>
    [Fact]
    public void A_job_across_two_godowns_is_one_row_naming_both()
    {
        var b = new Fixture("Two Site Co");
        var centre = b.JobProject("Bridge Project", "Site A");
        b.DesignateGodown("Site B", centre);
        b.AllocateToCostCentre(centre, b.ExpenseLedger, Money.FromRupees(100m), income: false);

        var job = Assert.Single(JobWorkAnalysis.Build(b.Company, FyStart, AsOf));
        Assert.Equal(new[] { "Site A", "Site B" }, job.GodownNames);
        Assert.Equal(Money.FromRupees(100m), job.TotalCost);
    }

    /// <summary>🔴 With F11 "Enable Job Costing" off the report is empty even with a designated godown.</summary>
    [Fact]
    public void With_job_costing_off_the_report_is_empty()
    {
        var b = new Fixture("Job Off Co");
        var centre = b.JobProject("Bridge Project", "Site A");
        b.AllocateToCostCentre(centre, b.ExpenseLedger, Money.FromRupees(100m), income: false);
        b.Company.EnableJobCosting = false;

        Assert.Empty(JobWorkAnalysis.Build(b.Company, FyStart, AsOf));
    }

    // ═══════════════════════════════════════════════ census 9.9 — the Stock Journal transfer class

    /// <summary>
    /// The transfer class mirrors every source line to one destination godown, carrying the batch, the rate and
    /// the unit. 🔴 Dropping the rate would let the transfer REVALUE the stock as it moved, and dropping the
    /// batch would merge tracked lots — both money bugs, and both exactly what a hand-keyed mirror gets wrong.
    /// </summary>
    [Fact]
    public void The_transfer_class_mirrors_every_source_line_including_batch_and_rate()
    {
        var b = new Fixture("Transfer Co");
        var from = b.Godown("Site A");
        var to = b.Godown("Site B");
        var source = new[]
        {
            new InventoryAllocation(b.Item.Id, from.Id, 12m, StockDirection.Outward,
                rate: Money.FromRupees(25m), batchLabel: "B-7", costTrackingNumber: "LOT/1"),
        };

        var mirrored = InterGodownTransfer.Mirror(source, to.Id);

        var line = Assert.Single(mirrored);
        Assert.Equal(to.Id, line.GodownId);
        Assert.Equal(StockDirection.Inward, line.Direction);
        Assert.Equal(b.Item.Id, line.StockItemId);
        Assert.Equal(12m, line.Quantity);
        Assert.Equal(Money.FromRupees(25m), line.Rate);
        Assert.Equal("B-7", line.BatchLabel);
        Assert.Equal("LOT/1", line.CostTrackingNumber);
    }

    /// <summary>A transfer from a godown to itself moves nothing and is refused — it would post two cancelling
    /// arms that the Stock Journal's balance guard cannot tell from a real transfer.</summary>
    [Fact]
    public void A_transfer_to_the_same_godown_is_refused()
    {
        var b = new Fixture("Self Transfer Co");
        var from = b.Godown("Site A");
        var source = new[] { new InventoryAllocation(b.Item.Id, from.Id, 1m, StockDirection.Outward) };

        Assert.Throws<InvalidOperationException>(() => InterGodownTransfer.Mirror(source, from.Id));
    }

    /// <summary>The mirrored arm balances the source in the base unit BY CONSTRUCTION — the property the entry
    /// screen relies on when it stops asking the operator to balance a class-driven transfer by hand.</summary>
    [Fact]
    public void The_mirrored_arm_balances_the_source_by_construction()
    {
        var b = new Fixture("Balanced Transfer Co");
        var from = b.Godown("Site A");
        var to = b.Godown("Site B");
        var source = new[]
        {
            new InventoryAllocation(b.Item.Id, from.Id, 12m, StockDirection.Outward),
            new InventoryAllocation(b.Item.Id, from.Id, 8m, StockDirection.Outward),
        };

        var mirrored = InterGodownTransfer.Mirror(source, to.Id);
        Assert.Equal(source.Sum(a => a.Quantity), mirrored.Sum(a => a.Quantity));
        Assert.Equal(source.Length, mirrored.Count);
    }

    /// <summary>The inter-godown flag is refused on anything but a Stock Journal type — a class advertising a
    /// transfer the entry screen cannot perform must not be creatable at all.</summary>
    [Fact]
    public void The_inter_godown_flag_is_refused_on_a_non_stock_journal_type()
    {
        var b = new Fixture("Class Guard Co");
        var sales = b.Company.FindVoucherTypeByName("Sales")!;
        var journal = b.Company.FindVoucherTypeByName("Stock Journal")!;
        var service = new VoucherTypeService(b.Company);

        Assert.Throws<InvalidOperationException>(() => service.AddClass(sales.Id, "Transfer", true));

        // …and the same class name IS accepted on the Stock Journal, so the refusal is about the base type and
        // not about the name.
        var cls = service.AddClass(journal.Id, "Transfer", true);
        Assert.True(cls.UseClassForInterGodownTransfers);
        Assert.Single(journal.InterGodownTransferClasses);

        // A duplicate name on the same type is refused, matching the schema's unique index.
        Assert.Throws<InvalidOperationException>(() => service.AddClass(journal.Id, "transfer", true));
    }

    // ═══════════════════════════════════════════════ fixture

    /// <summary>A minimal trading book with one item, one supplier, one customer and the ledgers the reports
    /// classify by. Deliberately hand-posts vouchers through <see cref="LedgerService"/> so every test exercises
    /// the same posting path the app uses.</summary>
    private sealed class Fixture
    {
        public Company Company { get; }
        public StockItem Item { get; }
        public Domain.Ledger Supplier { get; }
        public Domain.Ledger Customer { get; }
        public Domain.Ledger PurchaseLedger { get; }
        public Domain.Ledger SalesLedger { get; }
        public Domain.Ledger ExpenseLedger { get; }

        private readonly Godown _main;
        private int _number;

        public Fixture(string name)
        {
            Company = CompanyFactory.CreateSeeded(name, FyStart);
            Company.UseTrackingNumbers = true;
            Company.EnableCostTracking = true;
            Company.EnableJobCosting = true;

            var inventory = new InventoryService(Company);
            var unit = inventory.CreateSimpleUnit("Nos", "Numbers");
            var group = Company.FindStockGroupByName("Primary") ?? inventory.CreateStockGroup("Primary");
            Item = inventory.CreateStockItem("Widget", group.Id, unit.Id);
            _main = Company.MainLocation!;

            Supplier = Ledger("Supplier Ltd", "Sundry Creditors", debit: false);
            Customer = Ledger("Customer Ltd", "Sundry Debtors", debit: true);
            PurchaseLedger = Ledger("Purchases", "Purchase Accounts", debit: true);
            SalesLedger = Ledger("Sales", "Sales Accounts", debit: false);
            ExpenseLedger = Ledger("Site Wages", "Direct Expenses", debit: true);
        }

        private Domain.Ledger Ledger(string name, string group, bool debit)
        {
            var l = new Domain.Ledger(Guid.NewGuid(), name, Company.FindGroupByName(group)!.Id,
                Money.Zero, openingIsDebit: debit);
            Company.AddLedger(l);
            return l;
        }

        public Godown Godown(string name) =>
            Company.FindGodownByName(name) ?? new InventoryService(Company).CreateGodown(name);

        public CostCentre CostCentre(string name)
        {
            var centre = new CostCentre(Guid.NewGuid(), name, Company.CostCategories.First().Id);
            Company.AddCostCentre(centre);
            return centre;
        }

        /// <summary>Creates a cost centre and designates <paramref name="godownName"/> as its site — the
        /// vendor's job/project shape.</summary>
        public CostCentre JobProject(string name, string godownName)
        {
            var centre = CostCentre(name);
            DesignateGodown(godownName, centre);
            return centre;
        }

        public void DesignateGodown(string godownName, CostCentre centre) =>
            new InventoryService(Company).SetGodownJobCostCentre(Godown(godownName).Id, centre.Id);

        public void ReceiptNote(string tracking, decimal qty) =>
            Note("Receipt Note", StockDirection.Inward, tracking, qty);

        public void DeliveryNote(string tracking, decimal qty) =>
            Note("Delivery Note", StockDirection.Outward, tracking, qty);

        private void Note(string typeName, StockDirection direction, string tracking, decimal qty) =>
            Company.AddInventoryVoucher(new InventoryVoucher(
                Guid.NewGuid(), Company.FindVoucherTypeByName(typeName)!.Id, new DateOnly(2025, 4, 5),
                new[]
                {
                    new InventoryAllocation(Item.Id, _main.Id, qty, direction,
                        rate: Money.FromRupees(10m), trackingNumber: tracking),
                },
                number: ++_number));

        public void PurchaseBill(
            string? tracking, decimal qty, decimal rate = 10m, string? costTracking = null, bool cancelled = false) =>
            Bill("Purchase", PurchaseLedger, Supplier, StockDirection.Inward,
                tracking, qty, rate, costTracking, cancelled);

        public void SalesBill(
            string? tracking, decimal qty, decimal rate = 10m, string? costTracking = null, bool cancelled = false) =>
            Bill("Sales", SalesLedger, Customer, StockDirection.Outward,
                tracking, qty, rate, costTracking, cancelled);

        private void Bill(
            string typeName, Domain.Ledger nominal, Domain.Ledger party, StockDirection direction,
            string? tracking, decimal qty, decimal rate, string? costTracking, bool cancelled)
        {
            var value = Money.FromRupees(qty * rate);
            var purchase = direction == StockDirection.Inward;
            var voucher = new Voucher(
                Guid.NewGuid(), Company.FindVoucherTypeByName(typeName)!.Id, new DateOnly(2025, 4, 9),
                new[]
                {
                    new EntryLine(nominal.Id, value, purchase ? DrCr.Debit : DrCr.Credit),
                    new EntryLine(party.Id, value, purchase ? DrCr.Credit : DrCr.Debit),
                },
                number: ++_number,
                partyId: party.Id,
                inventoryLines: new[]
                {
                    new VoucherInventoryLine(Item.Id, _main.Id, qty, Money.FromRupees(rate), direction,
                        trackingNumber: tracking, costTrackingNumber: costTracking),
                });

            // Post first, then CANCEL through the engine's own verb. Constructing the voucher pre-cancelled and
            // attaching it would not exercise the shape an operator actually produces — a real cancelled bill in
            // a real book has been posted and then withdrawn, and the report must skip THAT.
            var service = new LedgerService(Company);
            service.Post(voucher);
            if (cancelled) service.Cancel(voucher.Id);
        }

        /// <summary>Posts a balanced Journal that allocates <paramref name="amount"/> to
        /// <paramref name="centre"/> against <paramref name="ledger"/> — the ordinary cost-allocation shape Job
        /// Work Analysis reads, with no job-specific posting anywhere.</summary>
        public void AllocateToCostCentre(CostCentre centre, Domain.Ledger ledger, Money amount, bool income)
        {
            var contra = income ? Customer : Supplier;
            var allocation = new[] { new CostAllocation(centre.CategoryId, centre.Id, amount) };
            var nominalLine = income
                ? new EntryLine(ledger.Id, amount, DrCr.Credit, costAllocations: allocation)
                : new EntryLine(ledger.Id, amount, DrCr.Debit, costAllocations: allocation);
            var contraLine = income
                ? new EntryLine(contra.Id, amount, DrCr.Debit)
                : new EntryLine(contra.Id, amount, DrCr.Credit);

            new LedgerService(Company).Post(new Voucher(
                Guid.NewGuid(), Company.FindVoucherTypeByName("Journal")!.Id, new DateOnly(2025, 5, 1),
                new[] { nominalLine, contraLine }, number: ++_number));
        }
    }
}
