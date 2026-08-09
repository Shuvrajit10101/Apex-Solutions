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

    /// <summary>Posts a one-line stock voucher of <paramref name="baseType"/> for Widget via the entry VM.
    /// <paramref name="party"/> names a ledger to pick in the party combo — the shipped capture path for both an
    /// order and (since Phase 10.10 / WF-8) a movement note; <c>null</c> leaves it on "(none)".</summary>
    private void Post(Kit k, VoucherBaseType baseType, decimal qty, string? rate = null, string? party = null)
    {
        k.Vm.OpenInventoryVoucher(baseType);
        var entry = k.Vm.InventoryVoucherEntry!;
        if (party is not null)
            entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Name == party);
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

    // ---------------------------------------------------------------- (4) Reorder Status lists every level-carrying item

    /// <summary>
    /// 🔴 <b>RENAMED AND INVERTED BY DECISION — NOT A REGRESSION.</b> (Phase 10.10 / WF-7, register row IV-10.)
    /// The pre-10.10 <c>Reorder_status_flags_only_the_item_below_its_reorder_level</c> ended on
    /// <c>Assert.DoesNotContain(rows, r =&gt; r.Col1 == "Gadget")</c>, which encoded the engine's invented
    /// closing-stock listing filter — a rule that appears in no TallyPrime source. <b>That filter and HARD GATE
    /// PR-8 (the "MOQ floor at zero shortfall") were RETIRED BY USER DECISION</b>: TallyPrime lists every item
    /// that resolves a reorder level ("By default, all stock items from the selected stock group or category
    /// display… press F8 (Reorder Only)"), and <b>Tally-Prime-Book p.164</b> shows an already-covered item still
    /// on screen with an EMPTY "Order to be Placed" column — listed, ordering nothing. Gadget appearing with a
    /// nil shortfall is therefore the CORRECT behaviour. <b>Do not restore the <c>DoesNotContain</c>.</b>
    /// <para>Narrowing the list is the operator's F8, not the engine's, and F8 is covered separately by
    /// <c>ReorderLevelsViewModelTests.F8_reorder_only_filter_hides_rows_with_nothing_to_order</c>.</para>
    /// </summary>
    [Fact]
    public void Reorder_status_lists_every_level_carrying_item_with_the_short_one_flagged()
    {
        var k = NewKit("Reorder Co");
        k.Vm.OpenReport(ReportKind.ReorderStatus);
        var rows = k.Vm.Reports!.Rows;

        // Columns (slice 6): Item | Closing | Reorder Level | Pending POs | SOs Due | Shortfall | Order to be Placed.
        // Widget: 105 on hand (100 opening + 40 GRN − 30 delivery, then counted to 105) against a level of 150,
        // so it is short by 45; its MOQ of 20 does not floor a shortfall already above it.
        var widget = rows.Single(r => r.Col1 == "Widget");
        Assert.Equal("105", widget.Col2);   // closing
        Assert.Equal("150", widget.Col3);   // reorder level
        Assert.Equal("0", widget.Col4);     // pending POs (none)
        Assert.Equal("0", widget.Col5);     // sales orders due (none)
        Assert.Equal("45", widget.Col6);    // shortfall = 150 − 105
        Assert.Equal("45", widget.Col7);    // order to be placed = max(shortfall 45, MOQ 20)
        // Gadget: 200 on hand against a level of 50 — comfortably covered, and LISTED all the same with every
        // order column nil. This positive assertion is what replaced the retired DoesNotContain.
        var gadget = rows.Single(r => r.Col1 == "Gadget");
        Assert.Equal("200", gadget.Col2);   // closing (opening only — no movement was posted for Gadget)
        Assert.Equal("50", gadget.Col3);    // reorder level
        Assert.Equal("0", gadget.Col4);     // pending POs
        Assert.Equal("0", gadget.Col5);     // sales orders due
        Assert.Equal("0", gadget.Col6);     // no shortfall — nett available 200 is above the level of 50
        Assert.Equal("0", gadget.Col7);     // and so nothing to order: PR-8's MOQ floor at nil shortfall is retired
    }

    // ------------------------------------------------ (4b) the party a movement note carries (WF-8 root cause)

    /// <summary>
    /// 🔴 <b>THE ROOT-CAUSE HALF OF WF-8, AND THE ONLY TEST ANYWHERE THAT TOUCHES IT.</b> Until Phase 10.10 a
    /// movement note in this product <b>could not name a party at all</b>:
    /// <c>InventoryVoucherEntryViewModel.BuildMovementNote</c> — the only <c>new InventoryVoucher(</c> in the
    /// Desktop project — omitted <c>partyId</c>, and the picker was bound <c>IsVisible="{Binding IsOrder}"</c>,
    /// which is false for a note. <c>OrderFulfilment</c> keys its cohort on <b>(PartyId, StockItemId)</b>, so a
    /// customer's order sat in <c>(Ashok, Widget)</c> while his delivery note sat in <c>(null, Widget)</c>, the
    /// lookup missed, and the Order Register reported the whole ordered quantity outstanding for ever —
    /// byte-identical to the pre-WF-8 defect the engine work exists to delete.
    /// <para><b>Why this test had to be added rather than left to the Ledger suite.</b> Measured, not assumed:
    /// <c>grep -rn ShowsParty tests/</c> and <c>grep -rn SelectedParty tests/Apex.Desktop.Tests/</c> both
    /// returned <b>nothing</b> before this test, and deleting <c>partyId: SelectedParty?.Ledger?.Id</c> from
    /// <c>BuildMovementNote</c> (or flipping the binding back to <c>IsOrder</c>) left <b>all four projects
    /// green</b>. The Ledger suite's <c>ShellNote</c> helper is a hand-copied MIRROR of that method in a
    /// different project, and a mirror cannot by construction detect drift in the thing it mirrors — the same
    /// failure mode <c>OrderFulfilmentTests</c> records one level down, where 18 tests were green while the
    /// feature was a no-op on every real book.</para>
    /// <para>It deliberately locks the shell fix and the engine figure <b>together</b>, in one test rather than
    /// two files that can drift: the picker's per-type visibility, the party actually reaching the <c>.db</c>
    /// (read back off a reloaded company, not off the in-memory object), and the Order Register row that party
    /// makes correct.</para>
    /// </summary>
    [Fact]
    public void A_movement_note_captures_its_party_and_retires_the_order_that_customer_raised()
    {
        const string companyName = "Note Party Co";
        var k = NewKit(companyName);
        var c = k.Vm.Company!;
        var ashok = new Apex.Ledger.Domain.Ledger(
            Guid.NewGuid(), "Ashok Traders", c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true);
        c.AddLedger(ashok);
        _storage.Save(c);

        // (a) WHICH SCREENS OFFER THE PICKER. Orders always did; the four movement notes are the WF-8 fix, each
        // corpus-grounded on ShowsParty. Stock Journal (an internal transfer) and Physical Stock (a count) name
        // no counterparty, so showing one there would invent a field.
        foreach (var withParty in new[]
                 {
                     VoucherBaseType.PurchaseOrder, VoucherBaseType.SalesOrder, VoucherBaseType.ReceiptNote,
                     VoucherBaseType.DeliveryNote, VoucherBaseType.RejectionIn, VoucherBaseType.RejectionOut,
                 })
        {
            k.Vm.OpenInventoryVoucher(withParty);
            Assert.True(k.Vm.InventoryVoucherEntry!.ShowsParty, $"{withParty} must offer the party picker");
        }
        foreach (var withoutParty in new[] { VoucherBaseType.StockJournal, VoucherBaseType.PhysicalStock })
        {
            k.Vm.OpenInventoryVoucher(withoutParty);
            Assert.False(k.Vm.InventoryVoucherEntry!.ShowsParty, $"{withoutParty} names no counterparty");
        }

        // (b) Ashok's sales order and the delivery note that ships it — BOTH entered through the real screen.
        // 60.125 is odd-valued on purpose: a round quantity would also match the 30 and 40 the kit already
        // posted blank, and could not distinguish this note from those.
        Post(k, VoucherBaseType.SalesOrder, 60.125m, party: "Ashok Traders");
        Post(k, VoucherBaseType.DeliveryNote, 60.125m, party: "Ashok Traders");

        // (c) The party reached the DATABASE, not merely the in-memory voucher — reloaded from the .db.
        var reloaded = _storage.Load(_storage.ListCompanies().Single(e => e.Name == companyName));
        var note = reloaded.InventoryVouchers.Single(v =>
            reloaded.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.DeliveryNote
            && v.Allocations[0].Quantity == 60.125m);
        Assert.Equal(ashok.Id, note.PartyId);
        // The kit's own blank delivery note is untouched — "(none)" stays a real, reachable shape.
        Assert.Null(reloaded.InventoryVouchers.Single(v =>
            reloaded.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.DeliveryNote
            && v.Allocations[0].Quantity == 30m).PartyId);

        // (d) …and the figure it exists to make right: the order is RETIRED, not reported outstanding for ever.
        var from = reloaded.InventoryVouchers.Min(v => v.Date);
        var to = reloaded.InventoryVouchers.Max(v => v.Date);
        var order = Apex.Ledger.Reports.InventoryRegisters.BuildOrders(reloaded, from, to)
            .Single(r => r.OrderedQuantity == 60.125m);
        Assert.Equal(ashok.Id, order.PartyId);
        Assert.Equal(60.125m, order.FulfilledQuantity);
        Assert.Equal(0m, order.OutstandingQuantity);
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
