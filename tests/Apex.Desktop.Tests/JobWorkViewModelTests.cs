using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// End-to-end coverage for the Phase-6 slice-8 (Job Work) UI surfaced in the cascade (requirements
/// RQ-45..RQ-51, RQ-53, RQ-54, ER-13): the F11 "Enable Job Order Processing" gate (which activates the four
/// seeded voucher types + stamps their per-type flags), the Job Work In/Out Order entry, the Material In/Out
/// movement entry (order auto-fill, balanced transfer, Allow-Consumption transform), and the four Job Work
/// registers. Drives the real shell + page view models over a throwaway .db — no UI toolkit.
/// </summary>
public sealed class JobWorkViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public JobWorkViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexJobWorkTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- helpers

    private MainWindowViewModel NewSeededCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    private void EnableJobWork(MainWindowViewModel vm)
    {
        vm.ShowGstConfig();
        vm.GstConfig!.EnableJobOrderProcessing = true;
        Assert.True(vm.Company!.EnableJobOrderProcessing);
        vm.Back();
    }

    private StockGroup CreateGroup(MainWindowViewModel vm, string name)
    {
        vm.ShowStockGroupMaster();
        var m = vm.StockGroupMaster!;
        m.Name = name;
        Assert.True(m.Create());
        vm.Back();
        return vm.Company!.FindStockGroupByName(name)!;
    }

    private Unit CreateUnit(MainWindowViewModel vm, string symbol)
    {
        vm.ShowUnitMaster();
        var m = vm.UnitMaster!;
        m.IsCompound = false;
        m.Symbol = symbol;
        m.FormalName = symbol;
        m.DecimalPlacesText = "0";
        Assert.True(m.Create());
        vm.Back();
        return vm.Company!.FindUnitByName(symbol)!;
    }

    private Godown CreateThirdPartyGodown(MainWindowViewModel vm, string name)
    {
        vm.ShowGodownMaster();
        var m = vm.GodownMaster!;
        m.Name = name;
        m.ThirdParty = true;
        Assert.True(m.Create());
        vm.Back();
        var g = vm.Company!.FindGodownByName(name)!;
        Assert.True(g.ThirdParty);
        return g;
    }

    private StockItem CreateItem(MainWindowViewModel vm, string name, StockGroup group, Unit unit,
        decimal? openingQty = null, decimal openingRate = 0m)
    {
        vm.ShowStockItemMaster();
        var m = vm.StockItemMaster!;
        m.Name = name;
        m.SelectedGroup = m.Groups.Single(g => g.Id == group.Id);
        m.SelectedUnit = m.Units.Single(u => u.Id == unit.Id);
        if (openingQty is { } q)
        {
            m.OpeningGodown = m.Godowns.First(g => g.IsMainLocation);
            m.OpeningQuantityText = q.ToString(System.Globalization.CultureInfo.InvariantCulture);
            m.OpeningRateText = openingRate.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }
        Assert.True(m.Create());
        vm.Back();
        return vm.Company!.FindStockItemByName(name)!;
    }

    // ---------------------------------------------------------------- (1) F11 gate (RQ-45/RQ-52/ER-13)

    [Fact]
    public void Feature_off_hides_job_work_vouchers_and_reports_and_openers_are_no_ops()
    {
        var vm = NewSeededCompany("JW Off Co");

        // The four seeded Job-Work types stay inactive (ER-13).
        Assert.All(
            vm.Company!.VoucherTypes.Where(t => t.BaseType is VoucherBaseType.JobWorkInOrder
                or VoucherBaseType.JobWorkOutOrder or VoucherBaseType.MaterialIn or VoucherBaseType.MaterialOut),
            t => Assert.False(t.IsActive));

        // Other Vouchers menu carries no Job Work items; Inventory Reports carries no Job Work registers.
        vm.ShowOtherVouchersMenu();
        Assert.DoesNotContain(vm.Menu.Where(x => x.IsSelectable), x => x.Label == "Job Work In Order");
        Assert.DoesNotContain(vm.Menu.Where(x => x.IsSelectable), x => x.Label == "Material Out");
        vm.ShowInventoryReportsMenu();
        Assert.DoesNotContain(vm.Menu.Where(x => x.IsSelectable), x => x.Label == "Job Work In Order Book");
        Assert.DoesNotContain(vm.Menu.Where(x => x.IsSelectable), x => x.Label == "Material In Register");

        // The openers are hard no-ops while the feature is off.
        vm.OpenJobWorkOrder(JobWorkDirection.Out);
        Assert.Null(vm.JobWorkOrderEntry);
        vm.OpenMaterialMovement(VoucherBaseType.MaterialOut);
        Assert.Null(vm.MaterialMovementEntry);
    }

    [Fact]
    public void Enabling_activates_the_four_types_and_stamps_flags_and_surfaces_menus()
    {
        var vm = NewSeededCompany("JW On Co");
        EnableJobWork(vm);

        var types = vm.Company!.VoucherTypes;
        Assert.True(types.Single(t => t.BaseType == VoucherBaseType.JobWorkInOrder).IsActive);
        Assert.True(types.Single(t => t.BaseType == VoucherBaseType.JobWorkOutOrder).IsActive);
        var matIn = types.Single(t => t.BaseType == VoucherBaseType.MaterialIn);
        var matOut = types.Single(t => t.BaseType == VoucherBaseType.MaterialOut);
        Assert.True(matIn.IsActive);
        Assert.True(matOut.IsActive);
        Assert.True(matIn.UseForJobWork);
        Assert.True(matOut.UseForJobWork);
        Assert.True(matIn.AllowConsumption);         // Material In consumes on receipt (RQ-49)
        Assert.False(matOut.AllowConsumption);

        // The persisted flag survives a reload (round-trip).
        var entry = _storage.ListCompanies().Single(e => e.Name == "JW On Co");
        var reloaded = _storage.Load(entry);
        Assert.True(reloaded.EnableJobOrderProcessing);

        // Menus surface the four vouchers + four registers.
        vm.ShowOtherVouchersMenu();
        foreach (var label in new[] { "Job Work In Order", "Job Work Out Order", "Material In", "Material Out" })
            Assert.Contains(vm.Menu.Where(x => x.IsSelectable), x => x.Label == label);
        vm.ShowInventoryReportsMenu();
        foreach (var label in new[] { "Job Work In Order Book", "Job Work Out Order Book",
                     "Material In Register", "Material Out Register" })
            Assert.Contains(vm.Menu.Where(x => x.IsSelectable), x => x.Label == label);
    }

    [Fact]
    public void Disabling_rehides_the_types_again()
    {
        var vm = NewSeededCompany("JW Toggle Co");
        EnableJobWork(vm);
        vm.ShowGstConfig();
        vm.GstConfig!.EnableJobOrderProcessing = false;
        vm.Back();

        Assert.False(vm.Company!.EnableJobOrderProcessing);
        Assert.All(
            vm.Company.VoucherTypes.Where(t => t.BaseType is VoucherBaseType.JobWorkInOrder
                or VoucherBaseType.JobWorkOutOrder or VoucherBaseType.MaterialIn or VoucherBaseType.MaterialOut),
            t => Assert.False(t.IsActive));
    }

    // ---------------------------------------------------------------- (2) full principal Job Work flow

    /// <summary>Seeds a principal-side fixture: two raw components with opening stock at Main + a finished good
    /// + a third-party "Worker Site" godown. Returns the shell with the feature enabled.</summary>
    private MainWindowViewModel SeedPrincipalFixture(
        out StockItem compA, out StockItem compB, out StockItem fg, out Godown worker)
    {
        var vm = NewSeededCompany("Principal Co");
        EnableJobWork(vm);
        var raw = CreateGroup(vm, "Raw Materials");
        var fgGroup = CreateGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos");
        compA = CreateItem(vm, "CompA", raw, unit, openingQty: 100m, openingRate: 50m);
        compB = CreateItem(vm, "CompB", raw, unit, openingQty: 100m, openingRate: 30m);
        fg = CreateItem(vm, "PC", fgGroup, unit);
        worker = CreateThirdPartyGodown(vm, "Worker Site");
        return vm;
    }

    private void PostOutOrder(MainWindowViewModel vm, StockItem fg, StockItem compA, StockItem compB,
        Godown main, string orderNo)
    {
        vm.OpenJobWorkOrder(JobWorkDirection.Out);
        var e = vm.JobWorkOrderEntry!;
        e.FinishedGood = e.StockItems.Single(i => i.Id == fg.Id);
        e.FinishedGoodQtyText = "10";
        e.OrderNo = orderNo;

        var l0 = e.Lines[0];
        l0.SelectedItem = l0.StockItems.Single(i => i.Id == compA.Id);
        l0.SelectedGodown = l0.Godowns.Single(g => g.Id == main.Id);
        l0.QuantityText = "20";
        var l1 = e.Lines.Last(l => l.IsBlank);
        l1.SelectedItem = l1.StockItems.Single(i => i.Id == compB.Id);
        l1.SelectedGodown = l1.Godowns.Single(g => g.Id == main.Id);
        l1.QuantityText = "10";

        Assert.True(e.CanAccept, e.Message);
        Assert.True(e.Accept(), e.Message);
    }

    [Fact]
    public void Out_order_posts_without_stock_or_accounts_and_shows_in_the_out_order_book()
    {
        var vm = SeedPrincipalFixture(out var compA, out var compB, out var fg, out _);
        var main = vm.Company!.Godowns.First(g => g.IsMainLocation);
        var tbBefore = vm.Company.Vouchers.Count;

        PostOutOrder(vm, fg, compA, compB, main, "DKP/789");

        // No accounting voucher; on-hand unchanged for both components (RQ-47).
        Assert.Equal(tbBefore, vm.Company.Vouchers.Count);
        var ledger = new InventoryLedger(vm.Company);
        var asOf = vm.Company.BooksBeginFrom;
        Assert.Equal(100m, ledger.OnHand(compA.Id, asOf));
        Assert.Equal(100m, ledger.OnHand(compB.Id, asOf));

        // Out Order Book renders the order with per-component pending-to-issue = ordered.
        vm.OpenReport(ReportKind.JobWorkOutOrderBook);
        var rows = vm.Reports!.Rows;
        Assert.Contains(rows, r => r.IsHeader && r.Col2 == "DKP/789");
        Assert.Contains(rows, r => r.Col4 == "CompA" && r.Col6 == r.Col8);   // ordered == pending (nothing issued yet)
    }

    [Fact]
    public void Material_out_transfer_keeps_net_on_hand_and_nets_pending_down()
    {
        var vm = SeedPrincipalFixture(out var compA, out var compB, out var fg, out var worker);
        var main = vm.Company!.Godowns.First(g => g.IsMainLocation);
        PostOutOrder(vm, fg, compA, compB, main, "DKP/789");

        // Material Out — a balanced transfer Main → Worker Site, auto-filled from the order.
        vm.OpenMaterialMovement(VoucherBaseType.MaterialOut);
        var mo = vm.MaterialMovementEntry!;
        mo.SourceGodown = mo.Godowns.Single(g => g.Id == main.Id);
        mo.SelectedOrder = mo.Orders.Single(o => o.Order?.OrderNo == "DKP/789");
        mo.DestinationGodown = mo.Godowns.Single(g => g.Id == worker.Id);   // re-fills destination at the third party
        Assert.True(mo.CanAccept, mo.Message);
        Assert.True(mo.Accept(), mo.Message);

        var ledger = new InventoryLedger(vm.Company);
        var asOf = vm.Company.BooksBeginFrom;
        // Company net on-hand unchanged (stock stays on our books, RQ-46); per-godown shifted.
        Assert.Equal(100m, ledger.OnHand(compA.Id, asOf));
        Assert.Equal(80m, ledger.OnHand(compA.Id, main.Id, asOf));
        Assert.Equal(20m, ledger.OnHand(compA.Id, worker.Id, asOf));

        // Out Order Book: pending-to-issue now nets to zero (fully dispatched).
        vm.OpenReport(ReportKind.JobWorkOutOrderBook);
        var compRows = vm.Reports!.Rows.Where(r => !r.IsHeader && !r.IsTotal && r.Col4 == "CompA").ToList();
        Assert.NotEmpty(compRows);
        Assert.All(compRows, r => Assert.Equal("0", r.Col8.Trim()));

        // Material Out Register renders the dispatch rows.
        vm.OpenReport(ReportKind.MaterialOutRegister);
        Assert.Contains(vm.Reports!.Rows, r => r.Col4 == "CompA");
    }

    [Fact]
    public void Material_in_with_consumption_produces_fg_and_leaves_no_phantom_raw_material()
    {
        var vm = SeedPrincipalFixture(out var compA, out var compB, out var fg, out var worker);
        var main = vm.Company!.Godowns.First(g => g.IsMainLocation);
        PostOutOrder(vm, fg, compA, compB, main, "DKP/789");

        // Dispatch the components to the worker (balanced transfer).
        vm.OpenMaterialMovement(VoucherBaseType.MaterialOut);
        var mo = vm.MaterialMovementEntry!;
        mo.SourceGodown = mo.Godowns.Single(g => g.Id == main.Id);
        mo.SelectedOrder = mo.Orders.Single(o => o.Order?.OrderNo == "DKP/789");
        mo.DestinationGodown = mo.Godowns.Single(g => g.Id == worker.Id);
        Assert.True(mo.Accept(), mo.Message);

        // Material In (Allow Consumption): consume the components at Worker Site, receive 10 PC at Main.
        vm.OpenMaterialMovement(VoucherBaseType.MaterialIn);
        var mi = vm.MaterialMovementEntry!;
        Assert.True(mi.AllowConsumption);
        mi.DestinationGodown = mi.Godowns.Single(g => g.Id == main.Id);
        mi.SourceGodown = mi.Godowns.Single(g => g.Id == worker.Id);
        mi.SelectedOrder = mi.Orders.Single(o => o.Order?.OrderNo == "DKP/789");
        Assert.True(mi.CanAccept, mi.Message);
        Assert.True(mi.Accept(), mi.Message);

        var ledger = new InventoryLedger(vm.Company);
        var asOf = vm.Company.BooksBeginFrom;
        // No phantom raw material left at the worker's site (RQ-49).
        Assert.Equal(0m, ledger.OnHand(compA.Id, worker.Id, asOf));
        Assert.Equal(0m, ledger.OnHand(compB.Id, worker.Id, asOf));
        // Finished good produced on our books at Main.
        Assert.Equal(10m, ledger.OnHand(fg.Id, main.Id, asOf));

        // ER-4 / RQ-49 (A10 finding #1/#2): the finished good is valued from the LIVE consumed component cost
        // (CompA 20 × ₹50 + CompB 10 × ₹30 = ₹1,300), NOT from the order's finished-good rate (which is unset here,
        // so the old path valued the FG at ₹0 and silently dropped ₹1,300 of stock value with no counter-entry).
        var valuation = new StockValuationService(vm.Company);
        var fgClosing = valuation.ClosingValue(fg.Id, asOf);
        Assert.Equal(10m, fgClosing.Quantity);
        Assert.Equal(Money.FromRupees(1300m), fgClosing.Value);
        // Total stock value is conserved (no phantom): components lost exactly what the FG absorbed.
        // CompA 80×50 + CompB 90×30 + FG 1300 = 4000 + 2700 + 1300 = 8000 = opening 100×50 + 100×30.
        Assert.Equal(Money.FromRupees(8000m), valuation.TotalClosingStockValue(asOf));

        // Material In Register renders the movement.
        vm.OpenReport(ReportKind.MaterialInRegister);
        Assert.Contains(vm.Reports!.Rows, r => r.Col4 == "PC");
    }

    // ---------------------------------------------------------------- (3) worker-side symmetry (RQ-50)

    [Fact]
    public void In_order_uses_the_same_screen_with_pending_to_receive_components()
    {
        var vm = NewSeededCompany("Worker Co");
        EnableJobWork(vm);
        var raw = CreateGroup(vm, "Raw Materials");
        var fgGroup = CreateGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos");
        var comp = CreateItem(vm, "RM", raw, unit);
        var fg = CreateItem(vm, "Widget", fgGroup, unit);
        var main = vm.Company!.Godowns.First(g => g.IsMainLocation);

        vm.OpenJobWorkOrder(JobWorkDirection.In);
        var e = vm.JobWorkOrderEntry!;
        Assert.Equal(JobWorkDirection.In, e.Direction);
        e.FinishedGood = e.StockItems.Single(i => i.Id == fg.Id);
        e.FinishedGoodQtyText = "5";
        e.OrderNo = "IN/1";
        var l0 = e.Lines[0];
        l0.SelectedItem = l0.StockItems.Single(i => i.Id == comp.Id);
        l0.SelectedGodown = l0.Godowns.Single(g => g.Id == main.Id);
        l0.QuantityText = "5";
        // The In order defaults its components to Pending to Receive (RQ-47/RQ-50).
        Assert.Equal(JobWorkComponentTrack.PendingToReceive, l0.SelectedTrack!.Track);
        Assert.True(e.Accept(), e.Message);

        vm.OpenReport(ReportKind.JobWorkInOrderBook);
        Assert.Contains(vm.Reports!.Rows, r => r.IsHeader && r.Col2 == "IN/1");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
