using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests.Inventory;

/// <summary>
/// Batch-aware engine + batch-report tests (Phase 6 Cluster 1; requirements RQ-1..RQ-8, DP-1/DP-8, PR-3). Covers
/// the batch master + per-item uniqueness (RQ-1), the three independent item switches (RQ-2), expiry as an
/// absolute date OR a resolvable period (RQ-4), batch-aware on-hand (RQ-5), the FEFO-when-expiry-else-FIFO
/// default issue order with manual pin (DP-1/RQ-5), per-batch valuation at the batch's own inward rate
/// (RQ-6/DP-8), warn-not-block on expired/near-expiry selection (RQ-7), the Batch-wise + Age-Analysis reports
/// (RQ-8), and the PR-3 hard gate (two batches, FEFO sale valued at that batch's cost, age report flags the
/// near/past-expiry batch, expired selection warns-not-blocks). ER-13: a non-batch item is byte-identical to
/// today. Pure, deterministic, paisa-exact — exactly like the accounting core.
/// </summary>
public class BatchTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly D1 = new(2024, 4, 5);
    private static readonly DateOnly D2 = new(2024, 4, 10);
    private static readonly DateOnly D3 = new(2024, 4, 15);
    private static readonly DateOnly D4 = new(2024, 4, 20);

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required Company Company { get; init; }
        public required InventoryService Masters { get; init; }
        public required InventoryPostingService Posting { get; init; }
        public required BatchService Batches { get; init; }
        public required BatchStockService BatchStock { get; init; }
        public required Guid GroupId { get; init; }
        public required Guid UnitId { get; init; }
        public required Guid GodownId { get; init; }
    }

    private static Kit NewKit()
    {
        var c = CompanyFactory.CreateSeeded("Batch Co", FyStart);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        return new Kit
        {
            Company = c,
            Masters = masters,
            Posting = new InventoryPostingService(c),
            Batches = new BatchService(c),
            BatchStock = new BatchStockService(c),
            GroupId = grp.Id,
            UnitId = nos.Id,
            GodownId = c.MainLocation!.Id,
        };
    }

    private static Guid TypeId(Company c, VoucherBaseType baseType) =>
        c.VoucherTypes.First(t => t.BaseType == baseType).Id;

    private Guid Item(Kit k, string name, StockValuationMethod method = StockValuationMethod.AverageCost,
        bool maintainBatches = true, bool useExpiry = true)
    {
        var item = k.Masters.CreateStockItem(name, k.GroupId, k.UnitId, valuationMethod: method);
        item.MaintainInBatches = maintainBatches;
        item.UseExpiryDates = useExpiry;
        return item.Id;
    }

    private void Receive(Kit k, Guid item, DateOnly date, decimal qty, Money rate, string? batch = null)
        => k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.ReceiptNote), date,
            new[] { new InventoryAllocation(item, k.GodownId, qty, StockDirection.Inward, rate, batch) }));

    private void Deliver(Kit k, Guid item, DateOnly date, decimal qty, string? batch = null)
        => k.Posting.Post(new InventoryVoucher(Guid.NewGuid(), TypeId(k.Company, VoucherBaseType.DeliveryNote), date,
            new[] { new InventoryAllocation(item, k.GodownId, qty, StockDirection.Outward, null, batch) }));

    // ================================================================ RQ-1 batch master + uniqueness

    [Fact]
    public void Batch_number_is_unique_within_an_item()
    {
        var k = NewKit();
        var item = Item(k, "Med");
        k.Batches.CreateBatch(item, "B-001");
        var ex = Assert.Throws<InvalidOperationException>(() => k.Batches.CreateBatch(item, "B-001"));
        Assert.Contains("already exists", ex.Message);
        Assert.Contains("unique per item", ex.Message);
    }

    [Fact]
    public void Batch_number_may_be_reused_across_different_items()
    {
        var k = NewKit();
        var a = Item(k, "Med A");
        var b = Item(k, "Med B");
        var ba = k.Batches.CreateBatch(a, "B-001");
        var bb = k.Batches.CreateBatch(b, "B-001"); // same number, different item → allowed (RQ-1)
        Assert.NotEqual(ba.Id, bb.Id);
        Assert.Equal("B-001", ba.BatchNumber);
        Assert.Equal("B-001", bb.BatchNumber);
    }

    [Fact]
    public void Create_batch_rejects_unknown_item_and_unknown_godown()
    {
        var k = NewKit();
        Assert.Throws<InvalidOperationException>(() => k.Batches.CreateBatch(Guid.NewGuid(), "B"));
        var item = Item(k, "Med");
        Assert.Throws<InvalidOperationException>(() => k.Batches.CreateBatch(item, "B", godownId: Guid.NewGuid()));
    }

    [Fact]
    public void Delete_batch_is_blocked_while_referenced_by_a_movement()
    {
        var k = NewKit();
        var item = Item(k, "Med");
        var batch = k.Batches.CreateBatch(item, "B-001");
        Receive(k, item, D1, 10m, Money.FromRupees(50m), "B-001");
        Assert.Throws<InvalidOperationException>(() => k.Batches.DeleteBatch(batch.Id));
    }

    // ================================================================ RQ-2 three independent switches

    [Fact]
    public void Item_batch_switches_are_independent_and_default_off()
    {
        var k = NewKit();
        var plain = k.Masters.CreateStockItem("Plain", k.GroupId, k.UnitId);
        Assert.False(plain.MaintainInBatches);
        Assert.False(plain.TrackManufacturingDate);
        Assert.False(plain.UseExpiryDates);

        // Use-Expiry may be on WITHOUT Track-Mfg (subtlety a).
        plain.UseExpiryDates = true;
        Assert.True(plain.UseExpiryDates);
        Assert.False(plain.TrackManufacturingDate);
    }

    // ================================================================ RQ-4 expiry: absolute OR period

    [Fact]
    public void Expiry_period_resolves_to_a_concrete_date_from_mfg()
    {
        // "12 Months" from 15-Jan-2024 → 15-Jan-2025 (calendar-correct, culture-invariant).
        var period = ExpiryPeriod.Parse("12 Months");
        Assert.NotNull(period);
        Assert.Equal(new DateOnly(2025, 1, 15), period!.Value.ResolveFrom(new DateOnly(2024, 1, 15)));

        Assert.Equal(new DateOnly(2024, 2, 14), new ExpiryPeriod(30, ExpiryPeriodUnit.Days).ResolveFrom(new DateOnly(2024, 1, 15)));
        Assert.Equal(new DateOnly(2024, 1, 29), new ExpiryPeriod(2, ExpiryPeriodUnit.Weeks).ResolveFrom(new DateOnly(2024, 1, 15)));
        Assert.Equal(new DateOnly(2026, 1, 15), new ExpiryPeriod(2, ExpiryPeriodUnit.Years).ResolveFrom(new DateOnly(2024, 1, 15)));
    }

    [Fact]
    public void Expiry_period_text_round_trips()
    {
        Assert.Equal("12 Months", new ExpiryPeriod(12, ExpiryPeriodUnit.Months).RawText);
        Assert.Equal("1 Year", new ExpiryPeriod(1, ExpiryPeriodUnit.Years).RawText);
        Assert.Equal(new ExpiryPeriod(6, ExpiryPeriodUnit.Weeks), ExpiryPeriod.Parse("6 weeks"));
        Assert.Null(ExpiryPeriod.Parse("not a period"));
        Assert.Null(ExpiryPeriod.Parse(null));
    }

    [Fact]
    public void Batch_master_resolves_expiry_from_absolute_date_or_period()
    {
        var k = NewKit();
        var item = Item(k, "Med");

        var abs = k.Batches.CreateBatch(item, "ABS", expiryDate: new DateOnly(2024, 12, 31));
        Assert.Equal(new DateOnly(2024, 12, 31), abs.ResolvedExpiryDate);

        var per = k.Batches.CreateBatch(item, "PER",
            manufacturingDate: new DateOnly(2024, 1, 15),
            expiryPeriod: new ExpiryPeriod(12, ExpiryPeriodUnit.Months));
        Assert.Equal(new DateOnly(2025, 1, 15), per.ResolvedExpiryDate);

        // Absolute date wins over a period if both are present.
        var both = k.Batches.CreateBatch(item, "BOTH",
            manufacturingDate: new DateOnly(2024, 1, 1),
            expiryDate: new DateOnly(2024, 6, 30),
            expiryPeriod: new ExpiryPeriod(12, ExpiryPeriodUnit.Months));
        Assert.Equal(new DateOnly(2024, 6, 30), both.ResolvedExpiryDate);
    }

    // ================================================================ RQ-5 batch-aware on-hand

    [Fact]
    public void On_hand_is_computed_per_item_godown_batch()
    {
        var k = NewKit();
        var item = Item(k, "Med");
        k.Batches.CreateBatch(item, "B1", expiryDate: new DateOnly(2024, 6, 30));
        k.Batches.CreateBatch(item, "B2", expiryDate: new DateOnly(2024, 12, 31));
        Receive(k, item, D1, 10m, Money.FromRupees(50m), "B1");
        Receive(k, item, D2, 20m, Money.FromRupees(60m), "B2");
        Deliver(k, item, D3, 4m, "B1");

        var buckets = k.BatchStock.BatchOnHands(item, D4);
        Assert.Equal(6m, buckets.Single(b => b.Batch == "B1").Quantity);
        Assert.Equal(20m, buckets.Single(b => b.Batch == "B2").Quantity);
    }

    // ================================================================ DP-1 FEFO default, manual pin

    [Fact]
    public void Default_issue_order_is_fefo_when_expiry_tracked()
    {
        var k = NewKit();
        var item = Item(k, "Med");
        // B2 received first but expires LATER; B1 received later but expires SOONER.
        k.Batches.CreateBatch(item, "B2", expiryDate: new DateOnly(2024, 12, 31));
        k.Batches.CreateBatch(item, "B1", expiryDate: new DateOnly(2024, 6, 30));
        Receive(k, item, D1, 10m, Money.FromRupees(60m), "B2");
        Receive(k, item, D2, 10m, Money.FromRupees(50m), "B1");

        var plan = k.BatchStock.DefaultIssueSelection(item, k.GodownId, 5m, D3);
        // FEFO: consume the soonest-to-expire batch (B1) first.
        Assert.Single(plan);
        Assert.Equal("B1", plan[0].Batch);
        Assert.Equal(5m, plan[0].Quantity);
    }

    [Fact]
    public void Default_issue_order_is_fifo_when_no_expiry_tracked()
    {
        var k = NewKit();
        var item = Item(k, "Bolt", useExpiry: false);
        k.Batches.CreateBatch(item, "OLD");
        k.Batches.CreateBatch(item, "NEW");
        Receive(k, item, D1, 10m, Money.FromRupees(20m), "OLD"); // earlier inward
        Receive(k, item, D2, 10m, Money.FromRupees(25m), "NEW"); // later inward

        var plan = k.BatchStock.DefaultIssueSelection(item, k.GodownId, 15m, D3);
        // FIFO: OLD (earliest inward) fully, then NEW for the residual.
        Assert.Equal(2, plan.Count);
        Assert.Equal("OLD", plan[0].Batch);
        Assert.Equal(10m, plan[0].Quantity);
        Assert.Equal("NEW", plan[1].Batch);
        Assert.Equal(5m, plan[1].Quantity);
    }

    [Fact]
    public void Default_issue_order_is_fifo_for_a_non_expiry_item_even_when_its_batches_carry_expiry_dates()
    {
        // A10 finding 1 (HIGH, DP-1/RQ-5): the FEFO-vs-FIFO decision MUST follow the item's UseExpiryDates
        // switch, NOT merely whether the batch masters happen to carry an expiry date. A FIFO-mode item whose
        // batches carry expiry dates must issue in FIFO-by-inward order (earliest inward first), never FEFO.
        var k = NewKit();
        var item = Item(k, "Bolt", useExpiry: false); // item does NOT use expiry → FIFO-by-inward
        // FIRSTIN received earlier but expires LATER; LATERIN received later but expires SOONER.
        k.Batches.CreateBatch(item, "FIRSTIN", expiryDate: new DateOnly(2024, 12, 31));
        k.Batches.CreateBatch(item, "LATERIN", expiryDate: new DateOnly(2024, 6, 30));
        Receive(k, item, D1, 10m, Money.FromRupees(20m), "FIRSTIN"); // earliest inward
        Receive(k, item, D2, 10m, Money.FromRupees(25m), "LATERIN"); // later inward

        var plan = k.BatchStock.DefaultIssueSelection(item, k.GodownId, 5m, D3);
        // FIFO: earliest-in (FIRSTIN) first, IGNORING the sooner expiry on LATERIN.
        Assert.Single(plan);
        Assert.Equal("FIRSTIN", plan[0].Batch);
        Assert.Equal(5m, plan[0].Quantity);

        // The batch-wise report must also list rows in FIFO-by-inward order for a non-expiry item.
        var report = Report.BuildBatchwiseReport(k.Company, D3);
        var itemRows = report.Rows.Where(r => r.StockItemId == item).ToList();
        Assert.Equal("FIRSTIN", itemRows[0].Batch);
        Assert.Equal("LATERIN", itemRows[1].Batch);
    }

    [Fact]
    public void User_may_pin_a_specific_batch_over_the_default_order()
    {
        var k = NewKit();
        var item = Item(k, "Med");
        k.Batches.CreateBatch(item, "SOON", expiryDate: new DateOnly(2024, 6, 30));
        k.Batches.CreateBatch(item, "LATER", expiryDate: new DateOnly(2024, 12, 31));
        Receive(k, item, D1, 10m, Money.FromRupees(50m), "SOON");
        Receive(k, item, D2, 10m, Money.FromRupees(60m), "LATER");

        // Default would pick SOON first; pinning LATER overrides that.
        var plan = k.BatchStock.PinnedIssue(item, k.GodownId, "LATER", 5m, D3);
        Assert.Single(plan);
        Assert.Equal("LATER", plan[0].Batch);
    }

    // ================================================================ RQ-6 / DP-8 per-batch valuation

    [Fact]
    public void Issue_is_valued_at_the_batch_own_inward_rate_not_the_item_average()
    {
        var k = NewKit();
        var item = Item(k, "Med");
        k.Batches.CreateBatch(item, "CHEAP", expiryDate: new DateOnly(2024, 6, 30));
        k.Batches.CreateBatch(item, "DEAR", expiryDate: new DateOnly(2024, 12, 31));
        Receive(k, item, D1, 10m, Money.FromRupees(50m), "CHEAP");
        Receive(k, item, D2, 10m, Money.FromRupees(150m), "DEAR");

        // Issuing from DEAR values at ₹150 (its own rate), NOT the ₹100 item average.
        var dearValue = k.BatchStock.ValueOfIssue(item, k.GodownId, "DEAR", 4m, D3);
        Assert.Equal(Money.FromRupees(600m), dearValue);

        var cheapValue = k.BatchStock.ValueOfIssue(item, k.GodownId, "CHEAP", 4m, D3);
        Assert.Equal(Money.FromRupees(200m), cheapValue);
    }

    [Fact]
    public void Batch_master_inward_rate_is_authoritative_over_movement_rate()
    {
        var k = NewKit();
        var item = Item(k, "Med");
        // Master states an authoritative inward rate of ₹80; a later no-rate receipt does not override it.
        k.Batches.CreateBatch(item, "B1", expiryDate: new DateOnly(2024, 12, 31),
            inwardQuantity: 10m, inwardRate: Money.FromRupees(80m));
        Receive(k, item, D1, 10m, Money.FromRupees(80m), "B1");

        var bucket = k.BatchStock.BatchOnHands(item, D4).Single(b => b.Batch == "B1");
        Assert.Equal(Money.FromRupees(80m), bucket.UnitCost);
    }

    [Fact]
    public void Batchwise_value_is_per_lot_cost_and_diverges_from_average_cost_stock_summary_by_design()
    {
        // A10 finding 2 (MEDIUM, ER-4/DP-8): for an AverageCost item, the Σ of per-(item,godown,batch) closing
        // values (each batch at its OWN inward rate, DP-8) is INTENTIONALLY a different projection from the
        // item-level Stock Summary / StockValuationService closing value (item AVERAGE method). The two figures
        // MAY differ; DP-8 makes per-batch inward cost authoritative for a batch while the item method aggregates
        // for item-level reports. This test freezes that intended divergence so no future change silently claims
        // (or forces) a false reconciliation. Reconciliation is only guaranteed when the item's method equals
        // per-lot inward cost (asserted in the FIFO one-lot-remaining case below).
        var k = NewKit();
        var item = Item(k, "Avg", method: StockValuationMethod.AverageCost);
        k.Batches.CreateBatch(item, "A", expiryDate: new DateOnly(2024, 6, 30));
        k.Batches.CreateBatch(item, "B", expiryDate: new DateOnly(2024, 12, 31));
        Receive(k, item, D1, 10m, Money.FromRupees(50m), "A");  // 10 @ ₹50
        Receive(k, item, D2, 10m, Money.FromRupees(80m), "B");  // 10 @ ₹80
        Deliver(k, item, D3, 4m, "A");                          // issue 4 from A (per-lot pick)

        // Σ per-batch (DP-8): A 6@₹50 = 300 + B 10@₹80 = 800 → 1100.00.
        var batchValue = k.BatchStock.BatchOnHands(item, D4)
            .Where(b => b.Batch.Length > 0)
            .Aggregate(Money.Zero, (acc, b) => acc + b.Value);
        Assert.Equal(Money.FromRupees(1100m), batchValue);

        // Item-level AverageCost Stock Summary: 16 units at the running average → 1040.00 (a different figure).
        var stockSummary = new StockValuationService(k.Company).ClosingValue(item, D4);
        Assert.Equal(16m, stockSummary.Quantity);
        Assert.Equal(Money.FromRupees(1040m), stockSummary.Value);

        // The two projections DIFFER by design (DP-8); they are NOT expected to reconcile for an average item.
        Assert.NotEqual(batchValue, stockSummary.Value);
    }

    // ================================================================ RQ-7 warn-not-block

    [Fact]
    public void Selecting_an_expired_batch_warns_but_does_not_block()
    {
        var k = NewKit();
        var item = Item(k, "Med");
        k.Batches.CreateBatch(item, "OLD", expiryDate: new DateOnly(2024, 4, 1)); // expired well before D3
        Receive(k, item, D1, 10m, Money.FromRupees(50m), "OLD");

        var warning = k.BatchStock.ExpiryWarningFor(item, "OLD", D3);
        Assert.NotNull(warning);
        Assert.Equal(BatchExpiryKind.Expired, warning!.Kind);
        Assert.True(warning.DaysToExpiry < 0);

        // Warn-not-block: the outward from the expired batch still posts successfully.
        Deliver(k, item, D3, 4m, "OLD");
        Assert.Equal(6m, new InventoryLedger(k.Company).OnHand(item, k.GodownId, "OLD", D4));
    }

    [Fact]
    public void Near_expiry_batch_raises_a_near_expiry_warning_only()
    {
        var k = NewKit();
        var item = Item(k, "Med");
        // Expires 20 days after D3 → within the default 30-day window, not yet expired.
        k.Batches.CreateBatch(item, "SOON", expiryDate: D3.AddDays(20));
        Receive(k, item, D1, 10m, Money.FromRupees(50m), "SOON");

        var warning = k.BatchStock.ExpiryWarningFor(item, "SOON", D3);
        Assert.NotNull(warning);
        Assert.Equal(BatchExpiryKind.NearExpiry, warning!.Kind);
        Assert.Equal(20, warning.DaysToExpiry);

        // A batch expiring far out raises no warning.
        k.Batches.CreateBatch(item, "FAR", expiryDate: D3.AddDays(200));
        Receive(k, item, D1, 10m, Money.FromRupees(50m), "FAR");
        Assert.Null(k.BatchStock.ExpiryWarningFor(item, "FAR", D3));
    }

    // ================================================================ RQ-8 reports

    [Fact]
    public void Batchwise_report_shows_inwards_outwards_closing_with_mfg_and_expiry()
    {
        var k = NewKit();
        var item = Item(k, "Med");
        k.Batches.CreateBatch(item, "B1",
            manufacturingDate: new DateOnly(2024, 1, 1), expiryDate: new DateOnly(2024, 6, 30));
        Receive(k, item, D1, 10m, Money.FromRupees(50m), "B1");
        Deliver(k, item, D3, 4m, "B1");

        var report = Report.BuildBatchwiseReport(k.Company, D4);
        var row = report.Rows.Single(r => r.Batch == "B1");
        Assert.Equal(0m, row.OpeningQuantity);
        Assert.Equal(10m, row.InwardQuantity);
        Assert.Equal(4m, row.OutwardQuantity);
        Assert.Equal(6m, row.ClosingQuantity);
        Assert.Equal(new DateOnly(2024, 1, 1), row.ManufacturingDate);
        Assert.Equal(new DateOnly(2024, 6, 30), row.ExpiryDate);
        Assert.Equal(Money.FromRupees(300m), row.ClosingValue); // 6 @ ₹50
    }

    [Fact]
    public void Age_analysis_flags_past_expiry_distinctly_from_near_expiry()
    {
        var k = NewKit();
        var item = Item(k, "Med");
        k.Batches.CreateBatch(item, "EXPIRED", expiryDate: D3.AddDays(-5));  // past expiry
        k.Batches.CreateBatch(item, "SOON", expiryDate: D3.AddDays(10));     // near expiry
        k.Batches.CreateBatch(item, "FAR", expiryDate: D3.AddDays(365));     // far off
        Receive(k, item, D1, 5m, Money.FromRupees(50m), "EXPIRED");
        Receive(k, item, D1, 5m, Money.FromRupees(50m), "SOON");
        Receive(k, item, D1, 5m, Money.FromRupees(50m), "FAR");

        var age = Report.BuildBatchAgeAnalysis(k.Company, D3, withinDays: 30);
        // FAR is beyond the window; EXPIRED + SOON are listed, EXPIRED first (most overdue).
        Assert.Equal(2, age.Rows.Count);
        Assert.Equal("EXPIRED", age.Rows[0].Batch);
        Assert.Equal(BatchExpiryBucket.Expired, age.Rows[0].Bucket);
        Assert.True(age.Rows[0].IsExpired);
        Assert.Equal("SOON", age.Rows[1].Batch);
        Assert.Equal(BatchExpiryBucket.ExpiringSoon, age.Rows[1].Bucket);
        Assert.False(age.Rows[1].IsExpired);
        Assert.Single(age.ExpiredRows);
    }

    // ================================================================ ER-13 non-batch unchanged

    [Fact]
    public void Non_batch_item_has_a_single_bucket_matching_the_item_engine()
    {
        var k = NewKit();
        var item = Item(k, "Bolt", method: StockValuationMethod.Fifo, maintainBatches: false, useExpiry: false);
        Receive(k, item, D1, 100m, Money.FromRupees(10m));
        Receive(k, item, D2, 50m, Money.FromRupees(12m));
        Deliver(k, item, D3, 80m);

        // Batch-aware layer over a non-batch item: exactly one bucket (empty batch), on-hand ties to the engine.
        var buckets = k.BatchStock.BatchOnHands(item, D4);
        Assert.Single(buckets);
        Assert.Equal("", buckets[0].Batch);
        Assert.Equal(70m, buckets[0].Quantity);
        Assert.Equal(70m, new InventoryLedger(k.Company).OnHand(item, D4));

        // The batch-wise report (batch-only by default) is empty for a non-batch item.
        Assert.Empty(Report.BuildBatchwiseReport(k.Company, D4).Rows);
        // Age analysis lists nothing for a non-batch item.
        Assert.Empty(Report.BuildBatchAgeAnalysis(k.Company, D4).Rows);
    }

    // ================================================================ PR-3 hard gate

    [Fact]
    public void Pr3_buy_two_batches_sell_fefo_valued_at_batch_cost_age_flags_expiry_expired_warns_not_blocks()
    {
        var k = NewKit();
        var item = Item(k, "Vaccine"); // MaintainInBatches + UseExpiry on

        // Buy into TWO batches with different expiry and different cost.
        k.Batches.CreateBatch(item, "LOT-A",
            manufacturingDate: new DateOnly(2024, 1, 1), expiryDate: D4.AddDays(-1));  // expires SOONER (near/at-window)
        k.Batches.CreateBatch(item, "LOT-B",
            manufacturingDate: new DateOnly(2024, 3, 1), expiryDate: D4.AddDays(365)); // expires much LATER
        Receive(k, item, D1, 10m, Money.FromRupees(50m), "LOT-A");  // cheap, soon-to-expire
        Receive(k, item, D2, 10m, Money.FromRupees(80m), "LOT-B");  // dear, long-dated

        // Default issue on a sale of 6 → consumes the FEFO batch (LOT-A, soonest expiry).
        var plan = k.BatchStock.DefaultIssueSelection(item, k.GodownId, 6m, D3);
        Assert.Single(plan);
        Assert.Equal("LOT-A", plan[0].Batch);

        // Value the issue at THAT batch's cost (₹50), not the ₹65 item average → ₹300.
        Assert.Equal(Money.FromRupees(300m), plan[0].Value);
        Assert.Equal(Money.FromRupees(300m),
            k.BatchStock.ValueOfIssue(item, k.GodownId, "LOT-A", 6m, D3));

        // Post the FEFO delivery; on-hand reflects the batch draw-down.
        Deliver(k, item, D3, 6m, "LOT-A");
        Assert.Equal(4m, new InventoryLedger(k.Company).OnHand(item, k.GodownId, "LOT-A", D4));
        Assert.Equal(10m, new InventoryLedger(k.Company).OnHand(item, k.GodownId, "LOT-B", D4));

        // As of D4, LOT-A is past expiry → age report flags it distinctly (Expired) from the safe LOT-B.
        var age = Report.BuildBatchAgeAnalysis(k.Company, D4, withinDays: 30);
        var expiredRow = age.Rows.Single(r => r.Batch == "LOT-A");
        Assert.Equal(BatchExpiryBucket.Expired, expiredRow.Bucket);
        Assert.True(expiredRow.IsExpired);
        Assert.DoesNotContain(age.Rows, r => r.Batch == "LOT-B"); // long-dated batch not flagged

        // Selecting the now-expired LOT-A warns but does NOT block: the outward still posts.
        var warning = k.BatchStock.ExpiryWarningFor(item, "LOT-A", D4);
        Assert.NotNull(warning);
        Assert.Equal(BatchExpiryKind.Expired, warning!.Kind);
        Deliver(k, item, D4, 2m, "LOT-A"); // not blocked
        Assert.Equal(2m, new InventoryLedger(k.Company).OnHand(item, k.GodownId, "LOT-A", D4));
    }
}
