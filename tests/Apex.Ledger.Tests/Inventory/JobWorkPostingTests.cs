using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests.Inventory;

/// <summary>
/// Phase 6 slice 8 <b>Job Work</b> engine contract (RQ-45..RQ-51, PR-10; ER-5/ER-7/ER-13). Grounds the Book1
/// pp.85–96 "Personal Computer" worked example: enabling the feature activates the four seeded types; a Job Work
/// order moves neither stock nor accounts; a principal's Material Out is a balanced transfer that keeps the RM on
/// our books at a third-party godown (a location move, RQ-46); a Material In with Allow Consumption is a transform
/// that consumes the third-party components leaving no phantom RM (RQ-49); the SAME types serve principal and
/// worker (RQ-50); and the four registers render.
/// </summary>
public sealed class JobWorkPostingTests
{
    private static readonly DateOnly Open = new(2024, 4, 1);
    private static readonly DateOnly D1 = new(2024, 4, 10);
    private static readonly DateOnly D2 = new(2024, 4, 15);
    private static readonly DateOnly D3 = new(2024, 4, 20);

    // The arithmetically-clean PC fixture (Book1 p.85): five components, per-unit rates summing to the FG rate.
    private static readonly (string Name, decimal Rate)[] Components =
    {
        ("Monitor", 5000m), ("Cabinet", 3000m), ("RAM", 1000m), ("HDD", 2000m), ("Motherboard", 3000m),
    };
    private const decimal FgRate = 14000m;    // = 5000+3000+1000+2000+3000
    private const decimal OrderQty = 10m;

    private static Guid TypeId(Company c, VoucherBaseType bt) => c.VoucherTypes.First(t => t.BaseType == bt).Id;

    private sealed class Fixture
    {
        public required Company Company;
        public required Guid MainId;
        public required Guid WorkerSiteId;
        public required Guid FgItemId;
        public required Guid[] ComponentIds;
        public required InventoryPostingService Posting;
    }

    /// <summary>A GST-off trading company with the five RM components (10 each opening at Main), a PC finished
    /// good, and a third-party "Worker Site" godown. Job Order Processing is enabled.</summary>
    private static Fixture Build()
    {
        var c = CompanyFactory.CreateSeeded("Job Work Co", Open);
        new JobWorkService(c).SetEnabled(true);

        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var main = c.MainLocation!.Id;
        var worker = masters.CreateGodown("Worker Site", thirdParty: true);

        var componentIds = new Guid[Components.Length];
        for (var i = 0; i < Components.Length; i++)
        {
            var item = masters.CreateStockItem(Components[i].Name, grp.Id, nos.Id);
            componentIds[i] = item.Id;
            masters.AddOpeningBalance(item.Id, main, OrderQty, Money.FromRupees(Components[i].Rate));
        }

        var fg = masters.CreateStockItem("Personal Computer", grp.Id, nos.Id);

        return new Fixture
        {
            Company = c, MainId = main, WorkerSiteId = worker.Id,
            FgItemId = fg.Id, ComponentIds = componentIds, Posting = new InventoryPostingService(c),
        };
    }

    private static JobWorkOrder OutOrder(Fixture f) => new(
        JobWorkDirection.Out, "DKP/789", f.FgItemId, OrderQty,
        lines: Enumerable.Range(0, Components.Length).Select(i => new JobWorkOrderLine(
            f.ComponentIds[i], JobWorkComponentTrack.PendingToIssue, OrderQty, godownId: f.MainId,
            rate: Money.FromRupees(Components[i].Rate))).ToList(),
        finishedGoodRate: Money.FromRupees(FgRate), finishedGoodGodownId: f.MainId);

    // ---------------------------------------------------------------- RQ-45

    [Fact]
    public void Enabling_job_order_processing_activates_the_four_types_and_flags()
    {
        var c = CompanyFactory.CreateSeeded("JW", Open);
        foreach (var bt in new[] { VoucherBaseType.JobWorkInOrder, VoucherBaseType.MaterialIn,
                     VoucherBaseType.JobWorkOutOrder, VoucherBaseType.MaterialOut })
            Assert.False(c.VoucherTypes.First(t => t.BaseType == bt).IsActive);

        new JobWorkService(c).SetEnabled(true);

        Assert.True(c.EnableJobOrderProcessing);
        foreach (var bt in new[] { VoucherBaseType.JobWorkInOrder, VoucherBaseType.MaterialIn,
                     VoucherBaseType.JobWorkOutOrder, VoucherBaseType.MaterialOut })
            Assert.True(c.VoucherTypes.First(t => t.BaseType == bt).IsActive);

        var matIn = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.MaterialIn);
        var matOut = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.MaterialOut);
        Assert.True(matIn.UseForJobWork);
        Assert.True(matOut.UseForJobWork);
        Assert.True(matIn.AllowConsumption);
        Assert.True(matIn.IsConsumingMaterialIn);
        Assert.False(matOut.AllowConsumption);

        // Turning it off re-hides the four types (no data to delete here).
        new JobWorkService(c).SetEnabled(false);
        Assert.False(c.EnableJobOrderProcessing);
        Assert.False(c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.MaterialIn).IsActive);
        Assert.False(c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.MaterialIn).AllowConsumption);
    }

    // ---------------------------------------------------------------- RQ-47 (orders move nothing)

    [Fact]
    public void Job_work_order_moves_neither_accounts_nor_stock()
    {
        var f = Build();
        var ledger = new InventoryLedger(f.Company);
        var beforeTb = TrialBalance.Build(f.Company, D3).TotalDebit;
        var beforeOnHand = f.ComponentIds.Select(id => ledger.OnHand(id, D3)).ToArray();

        f.Posting.Post(InventoryVoucher.JobWork(
            Guid.NewGuid(), TypeId(f.Company, VoucherBaseType.JobWorkOutOrder), D1, OutOrder(f)));

        // No accounting voucher created (job order is a pure inventory voucher).
        Assert.Empty(f.Company.Vouchers);
        Assert.Equal(beforeTb, TrialBalance.Build(f.Company, D3).TotalDebit);
        // On-hand unchanged for every component.
        for (var i = 0; i < f.ComponentIds.Length; i++)
            Assert.Equal(beforeOnHand[i], ledger.OnHand(f.ComponentIds[i], D3));

        // The order surfaces in the Out Order Book with per-component pending-to-issue = ordered.
        var book = JobWorkReports.BuildOutOrderBook(f.Company, Open, D3);
        var row = Assert.Single(book);
        Assert.Equal("DKP/789", row.OrderNo);
        Assert.Equal(Components.Length, row.Components.Count);
        Assert.All(row.Components, comp =>
        {
            Assert.Equal(JobWorkComponentTrack.PendingToIssue, comp.Track);
            Assert.Equal(OrderQty, comp.PendingQuantity);
            Assert.Equal(0m, comp.FulfilledQuantity);
        });
    }

    // ---------------------------------------------------------------- RQ-46 (Material Out = location move)

    [Fact]
    public void Material_out_is_a_balanced_transfer_that_keeps_stock_on_our_books()
    {
        var f = Build();
        var order = PostOutOrder(f);
        var ledger = new InventoryLedger(f.Company);

        PostMaterialOut(f, order);

        for (var i = 0; i < f.ComponentIds.Length; i++)
        {
            var id = f.ComponentIds[i];
            Assert.Equal(0m, ledger.OnHand(id, f.MainId, D2));          // Main drained
            Assert.Equal(OrderQty, ledger.OnHand(id, f.WorkerSiteId, D2)); // now at the worker site
            Assert.Equal(OrderQty, ledger.OnHand(id, D2));              // company net UNCHANGED (still ours)
        }

        // Material Out Register lists the movement, tagged with the order number.
        var reg = JobWorkReports.BuildMaterialOutRegister(f.Company, Open, D3);
        Assert.NotEmpty(reg);
        Assert.All(reg, r => Assert.Contains("DKP/789", r.LinkedOrderNumbers));

        // The order's pending-to-issue nets down to zero (fully dispatched).
        var row = Assert.Single(JobWorkReports.BuildOutOrderBook(f.Company, Open, D3));
        Assert.All(row.Components, comp => Assert.Equal(0m, comp.PendingQuantity));
    }

    [Fact]
    public void Material_out_that_does_not_balance_is_rejected()
    {
        var f = Build();
        var typeId = TypeId(f.Company, VoucherBaseType.MaterialOut);
        // Source 10 out of Main but only 3 into Worker Site — a transfer must conserve quantity (RQ-46).
        var ex = Assert.Throws<InvalidOperationException>(() => f.Posting.Post(InventoryVoucher.MaterialMovement(
            Guid.NewGuid(), typeId, D2,
            source: new[] { new InventoryAllocation(f.ComponentIds[0], f.MainId, 10m, StockDirection.Outward, Money.FromRupees(5000m)) },
            destination: new[] { new InventoryAllocation(f.ComponentIds[0], f.WorkerSiteId, 3m, StockDirection.Inward, Money.FromRupees(5000m)) })));
        Assert.Contains("balance", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(f.Company.InventoryVouchers, v => f.Company.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.MaterialOut);
    }

    // ---------------------------------------------------------------- RQ-49 (consume, no phantom)

    [Fact]
    public void Material_in_with_consumption_consumes_third_party_components_and_produces_valued_fg()
    {
        var f = Build();
        var order = PostOutOrder(f);
        PostMaterialOut(f, order);
        var ledger = new InventoryLedger(f.Company);

        PostMaterialInConsumption(f, order);

        // No phantom RM: every component at the Worker Site is back to zero.
        foreach (var id in f.ComponentIds)
            Assert.Equal(0m, ledger.OnHand(id, f.WorkerSiteId, D3));

        // FG +10 on our books at Main, valued = Σ consumed component cost (paisa-exact), source qty ≠ dest qty.
        Assert.Equal(OrderQty, ledger.OnHand(f.FgItemId, f.MainId, D3));
        var closing = new StockValuationService(f.Company).ClosingValue(f.FgItemId, D3);
        Assert.Equal(OrderQty, closing.Quantity);
        Assert.Equal(Money.FromRupees(FgRate * OrderQty), closing.Value); // 140,000

        // Accounts still untouched (D-4: job-charge invoice rides the accounting path, not here).
        Assert.Empty(f.Company.Vouchers);
    }

    [Fact]
    public void Material_in_consuming_material_the_worker_site_never_received_is_rejected_no_phantom()
    {
        var f = Build();
        var order = PostOutOrder(f);
        // Deliberately DO NOT dispatch the components — the Worker Site holds nothing.
        var typeId = TypeId(f.Company, VoucherBaseType.MaterialIn);
        var before = f.Company.InventoryVouchers.Count;

        var ex = Assert.Throws<InvalidOperationException>(() => f.Posting.Post(InventoryVoucher.MaterialMovement(
            Guid.NewGuid(), typeId, D3,
            source: Enumerable.Range(0, Components.Length).Select(i => new InventoryAllocation(
                f.ComponentIds[i], f.WorkerSiteId, OrderQty, StockDirection.Outward, Money.FromRupees(Components[i].Rate))).ToArray(),
            destination: new[] { new InventoryAllocation(f.FgItemId, f.MainId, OrderQty, StockDirection.Inward, Money.FromRupees(FgRate)) },
            orderLinks: new[] { order.Id })));

        Assert.Contains("negative", ex.Message, StringComparison.OrdinalIgnoreCase);
        // Nothing persisted — the rejected voucher rolled back.
        Assert.Equal(before, f.Company.InventoryVouchers.Count);
    }

    // ---------------------------------------------------------------- RQ-49 / ER-4 (FG valued from LIVE cost)

    [Fact]
    public void Consuming_material_in_values_fg_from_live_component_cost_not_the_supplied_rate()
    {
        // A10 finding #1/#2 (phantom stock value): the finished good MUST be valued from the consumed components'
        // live cost, never from a planned/supplied rate. Here we deliberately supply GARBAGE rates on the movement
        // lines (source ₹1, FG rate NULL) — the engine builder must still book the FG at Σ live consumed cost.
        var f = Build();
        var order = PostOutOrder(f);
        PostMaterialOut(f, order);   // components now at the Worker Site; company value = 140,000

        var jobWork = new JobWorkService(f.Company);
        var consume = Enumerable.Range(0, Components.Length)
            .Select(i => new InventoryAllocation(
                f.ComponentIds[i], f.WorkerSiteId, OrderQty, StockDirection.Outward, Money.FromRupees(1m)))
            .ToArray();
        var produce = new[]
        {
            // FG line carries NO rate at all (the "order had no FG rate" case) — must not value the FG at ₹0.
            new InventoryAllocation(f.FgItemId, f.MainId, OrderQty, StockDirection.Inward),
        };

        var voucher = jobWork.BuildConsumingMaterialIn(
            TypeId(f.Company, VoucherBaseType.MaterialIn), D3, consume, produce, orderLinks: new[] { order.Id });
        f.Posting.Post(voucher);

        var valuation = new StockValuationService(f.Company);
        var fgClosing = valuation.ClosingValue(f.FgItemId, D3);
        Assert.Equal(OrderQty, fgClosing.Quantity);
        // FG valued at the LIVE consumed cost (Σ 10 × component rate = 140,000), NOT 10 × ₹1 and NOT ₹0.
        Assert.Equal(Money.FromRupees(FgRate * OrderQty), fgClosing.Value);

        // Value is conserved end-to-end: the components that left the books carried exactly the value the FG absorbs,
        // so total stock value is unchanged (no phantom stock value, no masked profit).
        Assert.Equal(Money.FromRupees(FgRate * OrderQty), valuation.TotalClosingStockValue(D3));
        foreach (var id in f.ComponentIds)
            Assert.Equal(0m, valuation.ClosingValue(id, D3).Quantity);
    }

    [Fact]
    public void Material_out_transfer_is_value_neutral_even_when_the_supplied_rate_diverges()
    {
        // A10 finding #3 (transfer value leak): a Material Out is a pure LOCATION move — company totals must not
        // change even if the movement lines carry a rate that diverges from live cost. We supply ₹1 on both sides;
        // the engine builder must re-value the inward to the live issue cost so the company total is unchanged.
        var f = Build();
        var before = new StockValuationService(f.Company).TotalClosingStockValue(D2);

        var jobWork = new JobWorkService(f.Company);
        var source = Enumerable.Range(0, Components.Length)
            .Select(i => new InventoryAllocation(
                f.ComponentIds[i], f.MainId, OrderQty, StockDirection.Outward, Money.FromRupees(1m)))
            .ToArray();
        var destination = Enumerable.Range(0, Components.Length)
            .Select(i => new InventoryAllocation(
                f.ComponentIds[i], f.WorkerSiteId, OrderQty, StockDirection.Inward, Money.FromRupees(1m)))
            .ToArray();

        var voucher = jobWork.BuildMaterialOutTransfer(
            TypeId(f.Company, VoucherBaseType.MaterialOut), D2, source, destination);
        f.Posting.Post(voucher);

        var after = new StockValuationService(f.Company);
        Assert.Equal(before, after.TotalClosingStockValue(D2)); // company total UNCHANGED (stays on our books)
        // Per-item value preserved too (moved godown, same value).
        for (var i = 0; i < f.ComponentIds.Length; i++)
            Assert.Equal(Money.FromRupees(Components[i].Rate * OrderQty), after.ClosingValue(f.ComponentIds[i], D2).Value);
    }

    // ---------------------------------------------------------------- RQ-51 (register nets, no double-count)

    [Fact]
    public void Out_order_book_does_not_double_count_consumption_as_a_second_issue()
    {
        // A10 finding #4: the dispatch (Material Out) and the downstream consumption (consuming Material In) both
        // link the same Out order and both carry OUTWARD lines for the same component. Fulfilled-to-issue must count
        // only the DISPATCH — not the consumption — so an order of 10 reads fulfilled 10, never 20.
        var f = Build();
        var order = PostOutOrder(f);
        PostMaterialOut(f, order);
        PostMaterialInConsumption(f, order);

        var row = Assert.Single(JobWorkReports.BuildOutOrderBook(f.Company, Open, D3));
        Assert.All(row.Components, comp =>
        {
            Assert.Equal(OrderQty, comp.FulfilledQuantity);  // 10 dispatched — NOT 20 (dispatch + consumption)
            Assert.Equal(0m, comp.PendingQuantity);
        });
    }

    // ---------------------------------------------------------------- RQ-50 (role symmetry)

    [Fact]
    public void Same_types_serve_the_worker_side_with_no_hard_coded_branch()
    {
        // Worker side: we RECEIVE raw materials (Material In → held-for-principal third-party godown) against a
        // Job Work IN order (components Pending to Receive), then DISPATCH the finished good back (Material Out,
        // pure outward). The identical engine path accepts the mirrored lines.
        var c = CompanyFactory.CreateSeeded("Worker Co", Open);
        new JobWorkService(c).SetEnabled(true);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var main = c.MainLocation!.Id;
        var held = masters.CreateGodown("Held For Principal", thirdParty: true).Id;
        var rm = masters.CreateStockItem("Fabric", grp.Id, nos.Id);
        var fg = masters.CreateStockItem("Shirt", grp.Id, nos.Id);
        masters.AddOpeningBalance(fg.Id, main, 5m, Money.FromRupees(100m)); // FG we manufactured, ready to dispatch
        var posting = new InventoryPostingService(c);

        // Job Work IN Order — we are the worker; the RM component is Pending to Receive (mirror of the Out order).
        var inOrder = posting.Post(InventoryVoucher.JobWork(
            Guid.NewGuid(), TypeId(c, VoucherBaseType.JobWorkInOrder), D1,
            new JobWorkOrder(JobWorkDirection.In, "PRIN/1", fg.Id, 5m,
                lines: new[] { new JobWorkOrderLine(rm.Id, JobWorkComponentTrack.PendingToReceive, 5m, godownId: held) })));

        // Material In receiving the principal's RM — a pure inward into the held-for-principal godown (Open Q1).
        posting.Post(InventoryVoucher.MaterialMovement(
            Guid.NewGuid(), TypeId(c, VoucherBaseType.MaterialIn), D2,
            source: Array.Empty<InventoryAllocation>(),
            destination: new[] { new InventoryAllocation(rm.Id, held, 5m, StockDirection.Inward, Money.FromRupees(20m)) },
            orderLinks: new[] { inOrder.Id }));

        // Material Out dispatching the finished good back — a pure outward (destination = Not Applicable, note 2).
        posting.Post(InventoryVoucher.MaterialMovement(
            Guid.NewGuid(), TypeId(c, VoucherBaseType.MaterialOut), D3,
            source: new[] { new InventoryAllocation(fg.Id, main, 5m, StockDirection.Outward) },
            destination: Array.Empty<InventoryAllocation>()));

        var ledger = new InventoryLedger(c);
        Assert.Equal(5m, ledger.OnHand(rm.Id, held, D3));  // principal's RM held on our (worker) books, third-party
        Assert.Equal(0m, ledger.OnHand(fg.Id, main, D3));  // FG dispatched back
        // The In Order Book renders the worker order with its Pending-to-Receive component.
        var row = Assert.Single(JobWorkReports.BuildInOrderBook(c, Open, D3));
        Assert.Equal(JobWorkComponentTrack.PendingToReceive, Assert.Single(row.Components).Track);
        // Pending-to-receive netted down by the linked inward receipt.
        Assert.Equal(0m, Assert.Single(row.Components).PendingQuantity);
    }

    // ---------------------------------------------------------------- RQ-51 (all four registers render)

    [Fact]
    public void All_four_registers_render_over_the_fixture()
    {
        var f = Build();
        var order = PostOutOrder(f);
        PostMaterialOut(f, order);
        PostMaterialInConsumption(f, order);

        Assert.Empty(JobWorkReports.BuildInOrderBook(f.Company, Open, D3));       // no worker order here
        Assert.Single(JobWorkReports.BuildOutOrderBook(f.Company, Open, D3));
        Assert.NotEmpty(JobWorkReports.BuildMaterialInRegister(f.Company, Open, D3));
        Assert.NotEmpty(JobWorkReports.BuildMaterialOutRegister(f.Company, Open, D3));
    }

    // ---------------------------------------------------------------- content-validation guards

    [Fact]
    public void Job_work_order_type_rejects_a_stock_movement_payload()
    {
        var f = Build();
        var typeId = TypeId(f.Company, VoucherBaseType.JobWorkOutOrder);
        // A material movement filed under an order type must be rejected.
        Assert.Throws<InvalidOperationException>(() => f.Posting.Post(InventoryVoucher.MaterialMovement(
            Guid.NewGuid(), typeId, D2,
            source: new[] { new InventoryAllocation(f.ComponentIds[0], f.MainId, 1m, StockDirection.Outward) },
            destination: new[] { new InventoryAllocation(f.ComponentIds[0], f.WorkerSiteId, 1m, StockDirection.Inward) })));
    }

    [Fact]
    public void An_out_order_payload_filed_under_the_in_type_is_rejected()
    {
        var f = Build();
        var inTypeId = TypeId(f.Company, VoucherBaseType.JobWorkInOrder);
        var ex = Assert.Throws<InvalidOperationException>(() => f.Posting.Post(InventoryVoucher.JobWork(
            Guid.NewGuid(), inTypeId, D1, OutOrder(f)))); // payload Direction = Out, type = In
        Assert.Contains("Job Work order", ex.Message);
    }

    // ---------------------------------------------------------------- helpers

    private static InventoryVoucher PostOutOrder(Fixture f) => f.Posting.Post(InventoryVoucher.JobWork(
        Guid.NewGuid(), TypeId(f.Company, VoucherBaseType.JobWorkOutOrder), D1, OutOrder(f)));

    private static void PostMaterialOut(Fixture f, InventoryVoucher order) => f.Posting.Post(
        InventoryVoucher.MaterialMovement(
            Guid.NewGuid(), TypeId(f.Company, VoucherBaseType.MaterialOut), D2,
            source: Enumerable.Range(0, Components.Length).Select(i => new InventoryAllocation(
                f.ComponentIds[i], f.MainId, OrderQty, StockDirection.Outward, Money.FromRupees(Components[i].Rate))).ToArray(),
            destination: Enumerable.Range(0, Components.Length).Select(i => new InventoryAllocation(
                f.ComponentIds[i], f.WorkerSiteId, OrderQty, StockDirection.Inward, Money.FromRupees(Components[i].Rate))).ToArray(),
            orderLinks: new[] { order.Id }));

    private static void PostMaterialInConsumption(Fixture f, InventoryVoucher order) => f.Posting.Post(
        InventoryVoucher.MaterialMovement(
            Guid.NewGuid(), TypeId(f.Company, VoucherBaseType.MaterialIn), D3,
            source: Enumerable.Range(0, Components.Length).Select(i => new InventoryAllocation(
                f.ComponentIds[i], f.WorkerSiteId, OrderQty, StockDirection.Outward, Money.FromRupees(Components[i].Rate))).ToArray(),
            destination: new[] { new InventoryAllocation(f.FgItemId, f.MainId, OrderQty, StockDirection.Inward, Money.FromRupees(FgRate)) },
            orderLinks: new[] { order.Id }));
}
