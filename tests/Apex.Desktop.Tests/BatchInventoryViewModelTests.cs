using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// End-to-end coverage for the Phase-6 Cluster-1 (Batches &amp; Expiry) UI surfaced in the cascade
/// (requirements RQ-1..RQ-8, RQ-52, RQ-54): the F11 company flag gate, the three item batch switches, the
/// Batch master (create + per-item uniqueness), the batch-allocation sub-screen (FEFO default + warn-not-block
/// expiry + Σ=line-qty balance), and the two batch reports (Batch-wise + Age Analysis, past-expiry flagged).
/// Drives the real shell + page view models over a throwaway .db — no UI toolkit.
/// </summary>
public sealed class BatchInventoryViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public BatchInventoryViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexBatchTests_" + Guid.NewGuid().ToString("N"));
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

    private StockGroup CreateGroup(MainWindowViewModel vm, string name)
    {
        vm.ShowStockGroupMaster();
        var m = vm.StockGroupMaster!;
        m.Name = name;
        Assert.True(m.Create());
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
        return vm.Company!.FindUnitByName(symbol)!;
    }

    /// <summary>Turns the company "Maintain Batch-wise details" flag on through the F11 (GstConfig) screen.</summary>
    private void EnableBatchFeature(MainWindowViewModel vm)
    {
        vm.ShowGstConfig();
        vm.GstConfig!.MaintainBatchwiseDetails = true;     // reacts immediately + persists
        Assert.True(vm.Company!.MaintainBatchwiseDetails);
        vm.Back();
    }

    /// <summary>Creates a batch-tracked item (Maintain in Batches + Use Expiry) through the Stock Item master.</summary>
    private StockItem CreateBatchItem(MainWindowViewModel vm, string name, StockGroup group, Unit unit)
    {
        vm.ShowStockItemMaster();
        var m = vm.StockItemMaster!;
        Assert.True(m.ShowBatchSwitches);      // gate on → switches visible
        m.Name = name;
        m.SelectedGroup = m.Groups.Single(g => g.Id == group.Id);
        m.SelectedUnit = m.Units.Single(u => u.Id == unit.Id);
        m.MaintainInBatches = true;
        m.UseExpiryDates = true;
        Assert.True(m.Create());
        var item = vm.Company!.FindStockItemByName(name)!;
        Assert.True(item.MaintainInBatches);
        Assert.True(item.UseExpiryDates);
        vm.Back();
        return item;
    }

    // ---------------------------------------------------------------- (1) F11 company flag gate (RQ-52)

    [Fact]
    public void Company_flag_off_hides_item_batch_switches_and_batch_master()
    {
        var vm = NewSeededCompany("Flag Off Co");

        // Flag off by default → the item switches are hidden and the Create menu carries no "Batch".
        vm.ShowStockItemMaster();
        Assert.False(vm.StockItemMaster!.ShowBatchSwitches);
        vm.Back();

        vm.ShowCreateMenu();
        Assert.DoesNotContain(vm.Menu.Where(x => x.IsSelectable), x => x.Label == "Batch");

        // The batch master opener is a hard no-op while the flag is off.
        vm.ShowBatchMaster();
        Assert.Null(vm.BatchMaster);
        Assert.NotEqual(Screen.BatchMaster, vm.CurrentScreen);
    }

    [Fact]
    public void Company_flag_on_surfaces_item_switches_batch_master_and_reports()
    {
        var vm = NewSeededCompany("Flag On Co");
        EnableBatchFeature(vm);

        // Item switches now visible.
        vm.ShowStockItemMaster();
        Assert.True(vm.StockItemMaster!.ShowBatchSwitches);
        vm.Back();

        // "Batch" now appears under Masters → Create (a Page) — nested under Inventory Masters.
        vm.ShowCreateMenu();
        var createLabels = vm.Menu.Where(x => x.IsSelectable).Select(x => x.Label).ToArray();
        Assert.Contains("Batch", createLabels);
        Assert.Contains(vm.Menu, x => x.IsHeader && x.Label == "Inventory Masters");

        // "Batch" appears under Reports → Inventory Reports (a Group) and drills to the two batch reports.
        vm.ShowInventoryReportsMenu();
        Assert.Contains(vm.Menu.Where(x => x.IsSelectable), x => x.Label == "Batch");
        vm.ShowInventoryBatchReportsMenu();
        Assert.Equal(GatewayMenu.InventoryBatchReports, vm.CurrentGatewayMenu);
        var batchReportLabels = vm.Menu.Where(x => x.IsSelectable).Select(x => x.Label).ToArray();
        Assert.Contains("Batch-wise", batchReportLabels);
        Assert.Contains("Age Analysis", batchReportLabels);
    }

    // ---------------------------------------------------------------- (2) item switches independence (RQ-2)

    [Fact]
    public void Use_expiry_may_be_on_without_track_mfg()
    {
        var vm = NewSeededCompany("Switch Co");
        EnableBatchFeature(vm);
        var group = CreateGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos");

        vm.ShowStockItemMaster();
        var m = vm.StockItemMaster!;
        m.Name = "Serum";
        m.SelectedGroup = m.Groups.Single(g => g.Id == group.Id);
        m.SelectedUnit = m.Units.Single(u => u.Id == unit.Id);
        m.MaintainInBatches = true;
        m.TrackManufacturingDate = false;
        m.UseExpiryDates = true;                 // Use-Expiry ON without Track-Mfg (subtlety a)
        Assert.True(m.Create());

        var item = vm.Company!.FindStockItemByName("Serum")!;
        Assert.True(item.MaintainInBatches);
        Assert.False(item.TrackManufacturingDate);
        Assert.True(item.UseExpiryDates);
    }

    // ---------------------------------------------------------------- (3) Batch master (RQ-1/RQ-4)

    [Fact]
    public void Batch_is_created_through_the_master_and_persists()
    {
        const string companyName = "Batch Master Co";
        var vm = NewSeededCompany(companyName);
        EnableBatchFeature(vm);
        var group = CreateGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos");
        var item = CreateBatchItem(vm, "Tablet-A", group, unit);

        vm.ShowBatchMaster();
        Assert.Equal(Screen.BatchMaster, vm.CurrentScreen);
        var m = vm.BatchMaster!;
        Assert.True(m.CanCreate);                             // a batch-tracked item exists
        Assert.Contains(m.Items, i => i.Id == item.Id);
        m.BatchNumber = "LOT-1";
        m.SelectedItem = m.Items.Single(i => i.Id == item.Id);
        m.ManufacturingDateText = "01-Jan-2026";
        m.ExpiryPeriodCountText = "12";                       // 12 Months from mfg → resolves to 01-Jan-2027
        m.OpeningQuantityText = "100";
        m.OpeningRateText = "5.00";
        Assert.True(m.Create());

        var batch = vm.Company!.FindBatchByNumber(item.Id, "LOT-1")!;
        Assert.Equal(new DateOnly(2027, 1, 1), batch.ResolvedExpiryDate);
        Assert.Contains(m.Existing, r => r.BatchNumber == "LOT-1" && r.Item == "Tablet-A");

        // PERSISTED: reload and the batch survives with its resolved expiry.
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        var reloaded = _storage.Load(entry);
        var rBatch = reloaded.FindBatchByNumber(item.Id, "LOT-1");
        Assert.NotNull(rBatch);
        Assert.Equal(new DateOnly(2027, 1, 1), rBatch!.ResolvedExpiryDate);
        // A reloaded company that carries a batch master keeps the batch feature on (inferred, RQ-52).
        Assert.True(reloaded.MaintainBatchwiseDetails);
    }

    [Fact]
    public void Duplicate_batch_number_within_an_item_is_rejected_with_a_friendly_message()
    {
        var vm = NewSeededCompany("Dupe Batch Co");
        EnableBatchFeature(vm);
        var group = CreateGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos");
        var item = CreateBatchItem(vm, "Tablet-B", group, unit);

        vm.ShowBatchMaster();
        var m = vm.BatchMaster!;
        m.BatchNumber = "B-100";
        m.SelectedItem = m.Items.Single(i => i.Id == item.Id);
        Assert.True(m.Create());

        m.BatchNumber = "B-100";                              // same number, same item → rejected
        m.SelectedItem = m.Items.Single(i => i.Id == item.Id);
        Assert.False(m.Create());
        Assert.Contains("already exists", m.Message!);
    }

    [Fact]
    public void Expiry_period_without_a_mfg_date_is_rejected()
    {
        var vm = NewSeededCompany("Expiry Period Co");
        EnableBatchFeature(vm);
        var group = CreateGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos");
        var item = CreateBatchItem(vm, "Tablet-C", group, unit);

        vm.ShowBatchMaster();
        var m = vm.BatchMaster!;
        m.BatchNumber = "C-1";
        m.SelectedItem = m.Items.Single(i => i.Id == item.Id);
        m.ExpiryPeriodCountText = "6";                       // period but NO mfg date → cannot resolve
        Assert.False(m.Create());
        Assert.Contains("manufacturing date", m.Message!);
        Assert.Null(vm.Company!.FindBatchByNumber(item.Id, "C-1"));
    }

    // ---------------------------------------------------------------- (4) Batch-allocation sub-screen (RQ-3/RQ-7)

    /// <summary>
    /// Seeds a batch-tracked item with two purchased batches of different expiry (via the engine directly),
    /// so the batch-allocation sub-screen has stock to default from (FEFO) and an expired batch to warn on.
    /// </summary>
    private (StockItem Item, Godown Godown) SeedTwoBatches(MainWindowViewModel vm, DateOnly asOf)
    {
        var group = CreateGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos");
        var item = CreateBatchItem(vm, "Vaccine", group, unit);
        var godown = vm.Company!.MainLocation!;

        var batchSvc = new BatchService(vm.Company!);
        // Batch NEAR carries stock and expires soon (after asOf); batch OLD is already EXPIRED before asOf.
        batchSvc.CreateBatch(item.Id, "NEAR", manufacturingDate: asOf.AddMonths(-1),
            expiryDate: asOf.AddDays(20), godownId: godown.Id, inwardQuantity: 40m,
            inwardRate: Money.FromRupees(2m));
        batchSvc.CreateBatch(item.Id, "OLD", manufacturingDate: asOf.AddMonths(-12),
            expiryDate: asOf.AddDays(-5), godownId: godown.Id, inwardQuantity: 30m,
            inwardRate: Money.FromRupees(2m));

        // Bring both batches on-hand with a receipt so BatchOnHands sees positive stock.
        var posting = new InventoryPostingService(vm.Company!);
        var grnType = vm.Company!.VoucherTypes.First(t => t.BaseType == VoucherBaseType.ReceiptNote);
        posting.Post(new InventoryVoucher(Guid.NewGuid(), grnType.Id, asOf.AddDays(-30), new[]
        {
            new InventoryAllocation(item.Id, godown.Id, 40m, StockDirection.Inward, Money.FromRupees(2m), "NEAR"),
            new InventoryAllocation(item.Id, godown.Id, 30m, StockDirection.Inward, Money.FromRupees(2m), "OLD"),
        }, number: 0));
        _storage.Save(vm.Company!);
        return (item, godown);
    }

    [Fact]
    public void Batch_allocation_defaults_fefo_and_balances_to_the_line_quantity()
    {
        var vm = NewSeededCompany("Alloc Co");
        EnableBatchFeature(vm);
        var asOf = new DateOnly(2026, 6, 1);
        var (item, godown) = SeedTwoBatches(vm, asOf);

        // Ask the shell to open the sub-screen for an OUTWARD line of 50 (item uses expiry → FEFO).
        IReadOnlyList<BatchAllocation>? committed = null;
        vm.ShowBatchAllocation(item, godown, 50m, isOutward: true, onCommitted: a => committed = a);
        Assert.Equal(Screen.BatchAllocation, vm.CurrentScreen);
        var sub = vm.BatchAllocation!;

        // FEFO default: the soonest-expiry batch (OLD, already expired but earliest expiry) is drawn first,
        // then NEAR — Σ defaults to the full 50 and the screen is balanced.
        Assert.True(sub.IsBalanced);
        var alloc = sub.Allocations;
        Assert.Equal(50m, alloc.Sum(x => x.Quantity));
        Assert.Contains(alloc, x => x.BatchNumber == "OLD");   // earliest expiry first (FEFO)

        // The expired batch line raises a non-blocking EXPIRED warning (warn-not-block, RQ-7).
        Assert.Contains(sub.Lines, l => l.SelectedBatch?.Batch?.BatchNumber == "OLD" && l.IsExpired);

        // Accepting commits the allocations back to the line (does not block on the expiry).
        Assert.True(sub.Apply());
        Assert.NotNull(committed);
        Assert.Equal(50m, committed!.Sum(x => x.Quantity));
    }

    [Fact]
    public void Batch_allocation_rejects_when_quantities_do_not_add_up()
    {
        var vm = NewSeededCompany("Unbalanced Co");
        EnableBatchFeature(vm);
        var asOf = new DateOnly(2026, 6, 1);
        var (item, godown) = SeedTwoBatches(vm, asOf);

        vm.ShowBatchAllocation(item, godown, 50m, isOutward: true);
        var sub = vm.BatchAllocation!;

        // Zero out the seeded quantities → now under-allocated → Apply fails with a friendly message.
        foreach (var line in sub.Lines) line.QuantityText = string.Empty;
        Assert.False(sub.IsBalanced);
        Assert.False(sub.Apply());
        Assert.NotNull(sub.Message);
    }

    // ------------------------------------------ (4b) sub-screen gating + keyboard entry (RQ-52 / NFR-2 / RQ-3)

    /// <summary>Opens a Delivery-Note (outward) inventory-voucher entry screen and returns its VM.</summary>
    private InventoryVoucherEntryViewModel OpenDeliveryNote(MainWindowViewModel vm)
    {
        vm.OpenInventoryVoucher(VoucherBaseType.DeliveryNote);
        Assert.Equal(Screen.InventoryVoucherEntry, vm.CurrentScreen);
        return vm.InventoryVoucherEntry!;
    }

    [Fact]
    public void Batch_button_visibility_and_alt_b_entry_follow_the_full_gate_not_just_the_line_kind()
    {
        var vm = NewSeededCompany("Gate Co");
        EnableBatchFeature(vm);
        var asOf = new DateOnly(2026, 6, 1);
        var (item, godown) = SeedTwoBatches(vm, asOf);

        var entry = OpenDeliveryNote(vm);
        var line = entry.Lines[0];

        // A fresh movement line (item not yet picked) is a Movement kind (ShowsBatch) but the sub-screen does
        // NOT yet apply — so the "⧉" affordance and the keyboard entry are both inert (RQ-52 UI-leak fix).
        Assert.True(line.ShowsBatch);
        Assert.False(line.WantsBatchAllocation);
        Assert.False(entry.RequestBatchAllocationForFirstEligibleLine());

        // Fill item + godown + a positive qty → the full gate is satisfied and the affordance turns on.
        line.SelectedItem = item;
        line.SelectedGodown = godown;
        line.QuantityText = "10";
        Assert.True(line.WantsBatchAllocation);
        Assert.True(entry.LineWantsBatchAllocation(line));

        // Alt+B entry point (the keyboard equivalent of the button) now opens the sub-screen.
        Assert.True(entry.RequestBatchAllocationForFirstEligibleLine());
        Assert.Equal(Screen.BatchAllocation, vm.CurrentScreen);
        Assert.NotNull(vm.BatchAllocation);
    }

    [Fact]
    public void Batch_button_stays_hidden_on_a_non_batch_company()
    {
        var vm = NewSeededCompany("No Batch Co");   // flag OFF (default)
        var group = CreateGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos");

        // A plain (non-batch) item on a non-batch company.
        vm.ShowStockItemMaster();
        var m = vm.StockItemMaster!;
        m.Name = "Bolt";
        m.SelectedGroup = m.Groups.Single(g => g.Id == group.Id);
        m.SelectedUnit = m.Units.Single(u => u.Id == unit.Id);
        Assert.True(m.Create());
        var item = vm.Company!.FindStockItemByName("Bolt")!;
        vm.Back();

        var entry = OpenDeliveryNote(vm);
        var line = entry.Lines[0];
        line.SelectedItem = item;
        line.SelectedGodown = vm.Company!.MainLocation!;
        line.QuantityText = "5";

        // Company flag off → no batch affordance and Alt+B is a safe no-op (RQ-52).
        Assert.False(line.WantsBatchAllocation);
        Assert.False(entry.RequestBatchAllocationForFirstEligibleLine());
        Assert.NotEqual(Screen.BatchAllocation, vm.CurrentScreen);
    }

    [Fact]
    public void Stock_journal_source_line_seeds_the_fefo_default_outward()
    {
        var vm = NewSeededCompany("SJ Outward Co");
        EnableBatchFeature(vm);
        var asOf = new DateOnly(2026, 6, 1);
        var (item, godown) = SeedTwoBatches(vm, asOf);

        // A Stock Journal's primary "Lines" grid is the SOURCE (consumption/outward) side — DP-1 must seed the
        // FEFO/FIFO default there, which the old VM-wide IsOutwardMovement missed (only Delivery/Rejection-Out).
        vm.OpenInventoryVoucher(VoucherBaseType.StockJournal);
        var entry = vm.InventoryVoucherEntry!;
        var line = entry.Lines[0];
        line.SelectedItem = item;
        line.SelectedGodown = godown;
        line.QuantityText = "50";

        Assert.True(entry.RequestBatchAllocationForFirstEligibleLine());
        var sub = vm.BatchAllocation!;
        // Seeded outward → the FEFO plan defaults to the full 50 (OLD 30 + NEAR 20) and the screen is balanced.
        Assert.True(sub.IsBalanced);
        Assert.Equal(50m, sub.Allocations.Sum(x => x.Quantity));
        Assert.Contains(sub.Allocations, x => x.BatchNumber == "OLD");
    }

    // ---------------------------------------------------------------- (5) batch reports (RQ-8)

    [Fact]
    public void Age_analysis_flags_the_past_expiry_batch_distinctly()
    {
        var vm = NewSeededCompany("Age Report Co");
        EnableBatchFeature(vm);
        var asOf = new DateOnly(2026, 6, 1);
        var (item, _) = SeedTwoBatches(vm, asOf);

        // Open the Age Analysis report; scope its as-of to the seed date via F2.
        vm.OpenReport(ReportKind.BatchAgeAnalysis);
        var report = vm.Reports!;
        report.SetAsOf(asOf);
        Assert.Equal(ReportKind.BatchAgeAnalysis, report.Kind);
        Assert.True(report.IsBatchAgeAnalysis);
        Assert.True(report.IsInventoryReport);

        // The OLD batch (expired 5 days ago) is present AND flagged distinctly (IsExpired); NEAR is near-expiry.
        var rows = report.Rows.Where(r => !r.IsHeader && !r.IsTotal).ToList();
        Assert.Contains(rows, r => r.Col2 == "OLD" && r.IsExpired);
        Assert.Contains(rows, r => r.Col2 == "NEAR" && !r.IsExpired);
        _ = item;
    }

    [Fact]
    public void Batchwise_report_lists_each_batch_with_its_closing_and_zero_tally_text()
    {
        var vm = NewSeededCompany("Batchwise Report Co");
        EnableBatchFeature(vm);
        var asOf = new DateOnly(2026, 6, 1);
        var (_, _) = SeedTwoBatches(vm, asOf);

        vm.OpenReport(ReportKind.Batchwise);
        var report = vm.Reports!;
        report.SetAsOf(asOf);
        Assert.True(report.IsBatchwise);

        var rows = report.Rows.Where(r => !r.IsHeader && !r.IsTotal).ToList();
        Assert.Contains(rows, r => r.Col2 == "NEAR");
        Assert.Contains(rows, r => r.Col2 == "OLD");

        // ER-8: nothing produced by the batch UI/reports may contain the word "Tally".
        foreach (var r in report.Rows)
        {
            Assert.DoesNotContain("Tally", r.Col1 ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Tally", r.Secondary ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain("Tally", report.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tally", report.Subtitle, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }
}
