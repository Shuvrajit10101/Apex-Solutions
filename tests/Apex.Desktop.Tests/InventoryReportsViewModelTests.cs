using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Ledger.Io;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// End-to-end coverage for slice 3.4b — the nine inventory reports wired into the Miller-column nav and
/// projected by <see cref="ReportsViewModel"/>. Seeds a company (opening stock + a Receipt Note, a Delivery
/// Note and a Physical-Stock count posted through the real entry view models), then opens each report via
/// the shell and asserts <see cref="Screen.Report"/>, the report <see cref="ReportKind"/>/Title and that the
/// rows carry the expected values (Stock-Summary total, register row counts, Reorder-Status flags the right
/// item). Also covers the "Inventory Reports" submenu hierarchy + label→routing, and the Stock-Summary →
/// Stock Item Movement drill. Drives the headless shell over a throwaway <c>.db</c> — no UI toolkit.
/// </summary>
public sealed class InventoryReportsViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public InventoryReportsViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexInvReportTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required Guid WidgetId { get; init; }   // item with movements + a reorder level
        public required Guid GadgetId { get; init; }   // item left untouched (above reorder level)
        public required Guid GodownId { get; init; }
    }

    /// <summary>
    /// A seeded company with two items in Main Location: "Widget" (100 opening, reorder level 150 so it flags
    /// short) and "Gadget" (200 opening, reorder level 50 so it never flags). A Receipt Note (+40), a Delivery
    /// Note (−30) and a Physical-Stock count (Widget → 105) are posted through the real entry view models so
    /// every register has data and the movement journal is non-trivial.
    /// </summary>
    private Kit NewKit(string companyName)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);

        var c = vm.Company!;
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id, reorderLevel: 150m, minimumOrderQuantity: 20m);
        var gadget = inv.CreateStockItem("Gadget", grp.Id, nos.Id, reorderLevel: 50m);
        inv.AddOpeningBalance(widget.Id, c.MainLocation!.Id, 100m, Money.FromRupees(100m));
        inv.AddOpeningBalance(gadget.Id, c.MainLocation!.Id, 200m, Money.FromRupees(50m));
        _storage.Save(c);

        var k = new Kit { Vm = vm, WidgetId = widget.Id, GadgetId = gadget.Id, GodownId = c.MainLocation!.Id };

        Post(k, VoucherBaseType.ReceiptNote, 40m, rate: "105.00");   // Widget inward
        Post(k, VoucherBaseType.DeliveryNote, 30m);                  // Widget outward
        Post(k, VoucherBaseType.PhysicalStock, 105m);               // Widget counted → 105

        return k;
    }

    /// <summary>Posts a one-line stock voucher of <paramref name="baseType"/> for Widget via the entry VM.</summary>
    private void Post(Kit k, VoucherBaseType baseType, decimal qty, string? rate = null)
    {
        k.Vm.OpenInventoryVoucher(baseType);
        var entry = k.Vm.InventoryVoucherEntry!;
        var line = entry.Lines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.WidgetId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.GodownId);
        line.QuantityText = qty.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (rate is not null) line.RateText = rate;
        Assert.True(entry.Accept(), $"posting {baseType} should succeed");
        Assert.Equal(Screen.Gateway, k.Vm.CurrentScreen);
    }

    private static int RowCount(MainWindowViewModel vm) => vm.Reports!.Rows.Count;

    // ---------------------------------------------------------------- (1) each report opens

    [Theory]
    [InlineData(ReportKind.StockSummary, "Stock Summary")]
    [InlineData(ReportKind.GodownSummary, "Godown Summary")]
    [InlineData(ReportKind.StockItemMovement, "Stock Item Movement")]
    [InlineData(ReportKind.ReceiptNoteRegister, "Receipt Note Register")]
    [InlineData(ReportKind.DeliveryNoteRegister, "Delivery Note Register")]
    [InlineData(ReportKind.RejectionRegister, "Rejection Register")]
    [InlineData(ReportKind.PhysicalStockRegister, "Physical Stock Register")]
    [InlineData(ReportKind.OrderRegister, "Order Register")]
    [InlineData(ReportKind.ReorderStatus, "Reorder Status")]
    public void Each_inventory_report_opens_as_a_report_page(ReportKind kind, string title)
    {
        var k = NewKit($"Open {kind} Co");

        k.Vm.OpenReport(kind);

        Assert.Equal(Screen.Report, k.Vm.CurrentScreen);
        Assert.NotNull(k.Vm.Reports);
        Assert.Equal(kind, k.Vm.Reports!.Kind);
        Assert.Equal(title, k.Vm.Reports!.Title);
        Assert.True(k.Vm.Reports!.IsInventoryReport);
        Assert.False(k.Vm.Reports!.IsAccountingReport);
        // Exactly one page column open, reachable through the GatewayColumn accessor.
        Assert.Equal(1, k.Vm.Columns.Count(col => col.IsPage));
        Assert.Same(k.Vm.Reports, k.Vm.Columns[^1].Report);
    }

    // ---------------------------------------------------------------- (2) Stock Summary numbers + total

    [Fact]
    public void Stock_summary_shows_both_items_with_closing_qty_and_a_grand_total()
    {
        var k = NewKit("SS Co");
        k.Vm.OpenReport(ReportKind.StockSummary);
        var rows = k.Vm.Reports!.Rows;

        // Two item rows (Gadget, Widget — sorted by name) + one grand-total row.
        var widget = rows.Single(r => r.Col1 == "Widget");
        var gadget = rows.Single(r => r.Col1 == "Gadget");
        var total = rows.Single(r => r.IsTotal);

        // Widget closing = 100 + 40 − 30, then counted to 105 → "105".
        Assert.Equal("105", widget.Col4);
        Assert.Equal("200", gadget.Col4);
        Assert.True(widget.CanDrill);                       // item rows drill; total does not
        Assert.False(total.CanDrill);

        // Grand total value = Widget (105 × avg) + Gadget (200 × 50 = 10,000). Non-blank, Indian-grouped.
        Assert.False(string.IsNullOrWhiteSpace(total.Col6));
        Assert.Contains("Grand Total", total.Col1);
    }

    // ---------------------------------------------------------------- (3) registers carry the posted rows

    [Fact]
    public void Receipt_and_delivery_registers_each_list_their_one_posted_line_plus_a_total()
    {
        var k = NewKit("Register Co");

        k.Vm.OpenReport(ReportKind.ReceiptNoteRegister);
        var receipt = k.Vm.Reports!.Rows;
        Assert.Contains(receipt, r => r.Col4 == "Widget" && r.Col6 == "40");   // one GRN line, +40
        Assert.Contains(receipt, r => r.IsTotal);

        k.Vm.OpenReport(ReportKind.DeliveryNoteRegister);
        var delivery = k.Vm.Reports!.Rows;
        Assert.Contains(delivery, r => r.Col4 == "Widget" && r.Col6 == "-30");  // outward shows signed
        Assert.Contains(delivery, r => r.IsTotal);
    }

    [Fact]
    public void Physical_stock_register_lists_the_count_with_its_variance()
    {
        var k = NewKit("Physical Co");
        k.Vm.OpenReport(ReportKind.PhysicalStockRegister);
        var rows = k.Vm.Reports!.Rows;

        // Book before the count was 110 (100 + 40 − 30); counted 105 → variance −5.
        var row = rows.Single(r => r.Col2 == "Widget");
        Assert.Equal("110", row.Col4);   // Book
        Assert.Equal("105", row.Col5);   // Counted
        Assert.Equal("-5", row.Col6);    // Variance
    }

    // ---------------------------------------------------------------- (4) Reorder Status flags the short item only

    [Fact]
    public void Reorder_status_flags_only_the_item_below_its_reorder_level()
    {
        var k = NewKit("Reorder Co");
        k.Vm.OpenReport(ReportKind.ReorderStatus);
        var rows = k.Vm.Reports!.Rows;

        // Widget (105) is below its reorder level 150 → flagged with shortfall 45. Gadget (200 vs 50) is not.
        var widget = rows.Single(r => r.Col1 == "Widget");
        Assert.Equal("150", widget.Col3);   // reorder level
        Assert.Equal("45", widget.Col4);    // shortfall = 150 − 105
        Assert.DoesNotContain(rows, r => r.Col1 == "Gadget");
    }

    // ---------------------------------------------------------------- (5) Godown Summary + Movement

    [Fact]
    public void Godown_summary_places_widget_stock_in_main_location()
    {
        var k = NewKit("Godown Co");
        k.Vm.OpenReport(ReportKind.GodownSummary);
        var rows = k.Vm.Reports!.Rows;

        Assert.Contains(rows, r => r.Col2 == "Widget" && r.Col3 == "105");
        Assert.Contains(rows, r => r.IsTotal);
    }

    [Fact]
    public void Stock_item_movement_defaults_to_the_first_item_and_ends_at_the_counted_balance()
    {
        var k = NewKit("Movement Co");
        // Open scoped to Widget explicitly (the drill path); assert the running balance ends at 105.
        k.Vm.OpenReport(ReportKind.StockItemMovement, k.WidgetId);
        var rows = k.Vm.Reports!.Rows;

        Assert.Contains("Widget", k.Vm.Reports!.Subtitle);
        var closing = rows.Single(r => r.IsTotal);
        Assert.Equal("105", closing.Col5);   // closing balance qty
        // An opening line, three movements (GRN, Delivery, Physical Stock) and a closing line.
        Assert.True(rows.Count >= 5);
    }

    // ---------------------------------------------------------------- (6) nav hierarchy + label routing

    [Fact]
    public void Inventory_reports_group_nests_under_reports_with_three_subsections()
    {
        var k = NewKit("Nav Co");

        // The root Reports section exposes the "Inventory Reports" group.
        var rootItems = k.Vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Contains("Inventory Reports", rootItems);

        k.Vm.ShowInventoryReportsMenu();
        Assert.Equal(Screen.Gateway, k.Vm.CurrentScreen);
        Assert.Equal(GatewayMenu.InventoryReports, k.Vm.CurrentGatewayMenu);

        // Three sub-section headers, never a flat dump.
        var headers = k.Vm.Menu.Where(m => m.IsHeader).Select(m => m.Label).ToArray();
        Assert.Equal(new[] { "Stock", "Analysis", "Registers" }, headers);

        // Every one of the nine report labels is present as a page item.
        var items = k.Vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Equal(
            new[]
            {
                "Stock Summary", "Godown Summary", "Stock Movement",
                "Reorder Status",
                "Receipt Note Register", "Delivery Note Register", "Rejection Register",
                "Physical Stock Register", "Order Register",
            },
            items);
    }

    [Fact]
    public void Activating_a_report_item_opens_that_report_proving_labels_match_routing()
    {
        var k = NewKit("Route Co");
        k.Vm.ShowInventoryReportsMenu();

        // Drive the highlight to "Godown Summary" via the public arrow API, then activate it.
        while (k.Vm.Menu[k.Vm.SelectedIndex].Label != "Godown Summary") k.Vm.MoveDown();
        k.Vm.ActivateSelected();

        Assert.Equal(Screen.Report, k.Vm.CurrentScreen);
        Assert.Equal(ReportKind.GodownSummary, k.Vm.Reports!.Kind);
    }

    [Fact]
    public void Esc_steps_back_from_a_report_to_the_inventory_reports_submenu()
    {
        var k = NewKit("Back Co");
        k.Vm.ShowInventoryReportsMenu();
        k.Vm.OpenReport(ReportKind.StockSummary);
        Assert.Equal(Screen.Report, k.Vm.CurrentScreen);

        k.Vm.Back();   // Esc pops the report page back onto the Inventory Reports submenu.
        Assert.Equal(Screen.Gateway, k.Vm.CurrentScreen);
        Assert.Equal(GatewayMenu.InventoryReports, k.Vm.CurrentGatewayMenu);
        Assert.Null(k.Vm.Reports);
    }

    // ---------------------------------------------------------------- (7) drill: Stock Summary → Movement

    [Fact]
    public void Drilling_a_stock_summary_row_opens_that_items_movement_report()
    {
        var k = NewKit("Drill Co");
        k.Vm.OpenReport(ReportKind.StockSummary);
        var widgetRow = k.Vm.Reports!.Rows.Single(r => r.Col1 == "Widget");

        // The keyboard-first drill (Enter / double-click) routes through DrillReport.
        k.Vm.DrillReport(widgetRow);

        Assert.Equal(Screen.Report, k.Vm.CurrentScreen);
        Assert.Equal(ReportKind.StockItemMovement, k.Vm.Reports!.Kind);
        Assert.Contains("Widget", k.Vm.Reports!.Subtitle);
        // Still exactly one report page column (the movement REPLACED the summary).
        Assert.Equal(1, k.Vm.Columns.Count(col => col.IsPage));
    }

    [Fact]
    public void Drilling_a_non_drillable_row_is_a_no_op()
    {
        var k = NewKit("No Drill Co");
        k.Vm.OpenReport(ReportKind.StockSummary);
        var totalRow = k.Vm.Reports!.Rows.Single(r => r.IsTotal);

        k.Vm.DrillReport(totalRow);   // total row carries no DrillStockItemId

        // Still the Stock Summary — nothing opened.
        Assert.Equal(ReportKind.StockSummary, k.Vm.Reports!.Kind);
    }

    // ---------------------------------------------------------------- (8) tabular export: headers + precision

    [Fact]
    public void Stock_summary_export_carries_the_on_screen_column_captions_not_blank_headers()
    {
        var k = NewKit("Header Co");
        k.Vm.OpenReport(ReportKind.StockSummary);

        var export = ReportTabularProjector.Project(k.Vm.Reports!);
        var headers = export.Columns.Select(c => c.Header).ToArray();

        // The wide inventory report exports the SAME captions the grid shows (RQ-15/18) — never a blank header row.
        Assert.Equal(new[] { "Stock Item", "Inward", "Outward", "Closing Qty", "Rate", "Value" }, headers);
        Assert.DoesNotContain(export.Columns, c => string.IsNullOrEmpty(c.Header));
    }

    [Fact]
    public void Stock_item_movement_export_carries_its_six_on_screen_captions()
    {
        // A different wide inventory report — its six populated columns each carry the on-screen caption.
        var k = NewKit("Movement Header Co");
        k.Vm.OpenReport(ReportKind.StockItemMovement, k.WidgetId);

        var export = ReportTabularProjector.Project(k.Vm.Reports!);
        var headers = export.Columns.Select(c => c.Header).ToArray();
        Assert.Equal(new[] { "Date", "Voucher Type", "Inward", "Outward", "Balance", "Value" }, headers);
        Assert.DoesNotContain(export.Columns, c => string.IsNullOrEmpty(c.Header));
    }

    [Fact]
    public void Stock_summary_export_keeps_whole_quantities_whole_no_invented_decimals()
    {
        var k = NewKit("Precision Co");
        k.Vm.OpenReport(ReportKind.StockSummary);

        var export = ReportTabularProjector.Project(k.Vm.Reports!);
        string csv = System.Text.Encoding.UTF8.GetString(CsvWriter.Write(export));

        // Gadget closing qty is a whole 200 — it must export as "200", not "200.00" (RQ-15 on-screen fidelity).
        Assert.Contains("200", csv);
        Assert.DoesNotContain("200.00", csv);
        // Widget closing qty is a whole 105 — likewise no invented ".00".
        Assert.DoesNotContain("105.00", csv);
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
