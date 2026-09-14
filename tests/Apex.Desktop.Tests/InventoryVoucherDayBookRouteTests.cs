using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Census rows 4.9–4.16 — the USER ROUTES to a posted pure-stock voucher.</b> The engine half lives in
/// <c>Apex.Ledger.Tests.InventoryVoucherLifecycleTests</c>; what these prove is the half an operator touches,
/// driven through the REAL <see cref="MainWindow"/> tunnel key handler against a real company on a throwaway
/// <c>.db</c> — never by asserting that a view-model flag is set.
///
/// <para><b>THE DEFECT ALL EIGHT ROWS SHARED.</b> <c>DayBook.Build</c> iterated the accounting aggregate alone,
/// so a posted Stock Journal, Physical Stock, Delivery/Receipt Note, order or Rejection was invisible in the one
/// report an operator opens to find a transaction. Because the Day Book is the surface Alt+X and Alt+D act on,
/// there was no route to cancel or delete one: a mis-keyed stock movement was permanent, and the only remedy in
/// the product was to post an equal and opposite one. An Indian business hits that daily.</para>
///
/// <para><b>KEYBOARD ROUTE UNDER TEST, end to end:</b> Gateway → Reports → Day Book → arrow to the stock row →
/// <b>Enter</b> opens its read-only detail column; <b>Alt+X</b> cancels and <b>Alt+D</b> deletes, each behind the
/// one Y/N confirmation channel.</para>
///
/// <para><b>FIDELITY (R7; RULING 14 — the vendor's own published documentation).</b>
/// <i>help.tallysolutions.com/tally-prime/accounting-reports-tally/day-book-tally/</i>: the Day Book's "All
/// Vouchers" view <i>"displays Day Book for all the vouchers, irrespective of the type of voucher"</i>, and its
/// "Inventory Entries Only" view covers <i>"Delivery Note, Physical Stock Voucher, and others"</i> — so the
/// vendor's Day Book demonstrably carries this aggregate. The same page attests <i>"Press Alt+D to delete"</i>
/// and <i>"Press Alt+X to cancel"</i> on this report. <b>The PROMPT WORDING, the retained number and the
/// CANCELLED badge are OURS and unverified-by-design</b>, exactly as they are on the accounting side; no
/// assertion below may be re-labelled as fidelity to another product.</para>
/// </summary>
public sealed class InventoryVoucherDayBookRouteTests
{
    // ============================================================ harness

    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexInvDayBook_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        return (window, vm, tempDir);
    }

    private static void Pump(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Close(Window window, string dir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    /// <summary>Every string actually REALISED into the visual tree — the only evidence that survives a template
    /// that silently failed to bind.</summary>
    private static List<string> RenderedText(MainWindow window) =>
        Descendants(window).OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList();

    private sealed record Kit(string CompanyName, Guid ItemId, Guid GodownId, DateOnly On);

    /// <summary>
    /// A company with one "Widget" (Nos) item and 100 units opening, created through the real Create-Company
    /// route. Masters are made with the master service (they are not what is under test) and persisted.
    /// </summary>
    private static Kit Seed(MainWindowViewModel vm, string name, decimal opening = 100m)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);

        var c = vm.Company!;
        var masters = new InventoryService(c);
        var group = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", group.Id, nos.Id);
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, opening, Money.FromRupees(100m));

        return new Kit(name, item.Id, c.MainLocation!.Id, c.FinancialYearStart.AddDays(5));
    }

    /// <summary>Posts a stock voucher through the REAL entry screen and Accept, so it is genuinely posted and
    /// persisted rather than hand-built into the aggregate.</summary>
    private static InventoryVoucher PostThroughTheScreen(
        MainWindowViewModel vm, Kit k, VoucherBaseType baseType, decimal qty, string rate = "50")
    {
        var before = vm.Company!.InventoryVouchers.Select(v => v.Id).ToHashSet();

        vm.OpenInventoryVoucher(baseType);
        Assert.Equal(Screen.InventoryVoucherEntry, vm.CurrentScreen);
        var entry = vm.InventoryVoucherEntry!;
        entry.Date = k.On;
        var line = entry.Lines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.GodownId);
        line.QuantityText = qty.ToString(CultureInfo.InvariantCulture);
        line.RateText = rate;
        Assert.True(entry.Accept());

        return vm.Company!.InventoryVouchers.Single(v => !before.Contains(v.Id));
    }

    private static decimal OnHand(Company c, Kit k) =>
        new InventoryLedger(c).OnHand(k.ItemId, k.GodownId, c.FinancialYearStart.AddYears(1));

    /// <summary>Opens the Day Book and highlights the row standing for <paramref name="stockVoucherId"/> —
    /// resolved through the PURE-STOCK slot, which is the slot the whole slice turns on.</summary>
    private static ReportRow OpenDayBookOnStockRow(MainWindow window, MainWindowViewModel vm, Guid stockVoucherId)
    {
        vm.OpenReport(ReportKind.DayBook);
        Pump(window);
        var row = vm.Reports!.Rows.Single(r => r.DrillInventoryVoucherId == stockVoucherId);
        vm.Reports!.SelectedRow = row;
        Pump(window);
        return row;
    }

    // ============================================================ (a) the listing

    /// <summary>
    /// 🔴 <b>THE DRIVING TEST FOR ALL EIGHT ROWS.</b> A stock voucher posted through the real entry screen
    /// appears in the Day Book at all, is drillable, and — the part that makes every consumer correct — carries
    /// its id in the PURE-STOCK slot and leaves the accounting slot empty. On today's main the row does not
    /// exist, so the first assertion is the one that fails.
    /// </summary>
    [AvaloniaFact]
    public void A_posted_stock_voucher_reaches_the_Day_Book_in_its_own_drill_slot()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Stock Day Book Co");
            var receipt = PostThroughTheScreen(vm, k, VoucherBaseType.ReceiptNote, qty: 10m);

            var row = OpenDayBookOnStockRow(window, vm, receipt.Id);

            Assert.True(row.CanDrill, "A Day Book row nobody can open is only half a listing.");
            Assert.Equal(Guid.Empty, row.DrillVoucherId);
            Assert.False(row.IsCancelled);

            // The row carries what the report shows: the type name, and the movement's value (10 x 50 = 500) in
            // the amount column — the SAME derivation the inventory registers use, so the two reports cannot
            // state different figures for one voucher.
            Assert.Contains("Receipt Note", row.Particulars, StringComparison.Ordinal);
            Assert.Equal(IndianFormat.Amount(Money.FromRupees(500m)), row.Amount);
            Assert.Equal(Screen.Report, vm.CurrentScreen);

            // 🔴 The REALISED half is asserted where it can be: the Day Book's ItemsControl VIRTUALISES in the
            // headless host, so no test in this suite reads its row text — the realised-tree evidence for this
            // slice is the drill pane (Enter_drills_… below) and the CANCELLED badge, both of which paint.
            Assert.Contains(RenderedText(window), s => s.Contains("Day Book", StringComparison.Ordinal));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE SLOT SEPARATION IS LOAD-BEARING, and this is what would catch collapsing the two.</b> Six
    /// existing routes hand <c>DrillVoucherId</c> straight to <c>Company.FindVoucher</c>, which returns null for
    /// a pure-stock id — so a single shared slot would turn each of them into a SILENT no-op on these rows. The
    /// accounting row must keep its own slot filled and the stock slot empty, and vice versa.
    /// </summary>
    [AvaloniaFact]
    public void The_two_aggregates_never_share_a_drill_slot()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Slot Separation Co");
            var receipt = PostThroughTheScreen(vm, k, VoucherBaseType.ReceiptNote, qty: 4m);

            var c = vm.Company!;
            var cash = c.FindLedgerByName("Cash")!;
            var accounting = new LedgerService(c).Post(new Voucher(
                Guid.NewGuid(), c.FindVoucherTypeByName("Receipt")!.Id, k.On,
                new[]
                {
                    new EntryLine(cash.Id, Money.FromRupees(900m), DrCr.Debit),
                    new EntryLine(cash.Id, Money.FromRupees(900m), DrCr.Credit),
                }));

            vm.OpenReport(ReportKind.DayBook);
            Pump(window);

            var stockRow = vm.Reports!.Rows.Single(r => r.DrillInventoryVoucherId == receipt.Id);
            Assert.Equal(Guid.Empty, stockRow.DrillVoucherId);

            var accountingRow = vm.Reports!.Rows.Single(r => r.DrillVoucherId == accounting.Id);
            Assert.Equal(Guid.Empty, accountingRow.DrillInventoryVoucherId);

            // Both really are in the one list — the report an operator opens shows the day's whole trade.
            Assert.Contains(vm.Reports!.Rows, r => r.DrillInventoryVoucherId == receipt.Id);
            Assert.Contains(vm.Reports!.Rows, r => r.DrillVoucherId == accounting.Id);
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (b) the drill

    /// <summary>
    /// <b>Enter opens the read-only detail column</b>, the Day Book PERSISTS beneath it (the cascade drill, not a
    /// page replacement), and the pane's lines are realised into the visual tree — item, direction and quantity.
    /// Asserting the view model alone would pass over a template that never bound.
    /// </summary>
    [AvaloniaFact]
    public void Enter_drills_the_stock_row_into_a_read_only_detail_column_that_renders_its_lines()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Stock Drill Co");
            var receipt = PostThroughTheScreen(vm, k, VoucherBaseType.ReceiptNote, qty: 7m, rate: "20");
            OpenDayBookOnStockRow(window, vm, receipt.Id);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(Screen.InventoryVoucherDetail, vm.CurrentScreen);
            var pane = vm.InventoryVoucherDetail;
            Assert.NotNull(pane);
            Assert.Equal(receipt.Id, pane!.VoucherId);
            Assert.False(pane.IsCancelled);

            var text = RenderedText(window);
            Assert.Contains(text, s => s.Contains("Widget", StringComparison.Ordinal));
            Assert.Contains(text, s => s.Contains("Inward", StringComparison.Ordinal));
            // The drilled-from report is still on screen — this is a cascade column, not a replacement.
            Assert.NotNull(vm.Reports);
        }
        finally { Close(window, dir); }
    }

    /// <summary>A Physical Stock count states a counted quantity and carries NO rate, so the pane leaves Rate and
    /// Value empty rather than printing 0.00 — a zero there would read as "this stock is worth nothing", which is
    /// a different and false claim.</summary>
    [AvaloniaFact]
    public void A_physical_count_drills_and_states_a_count_rather_than_a_movement()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Physical Drill Co");

            vm.OpenInventoryVoucher(VoucherBaseType.PhysicalStock);
            var entry = vm.InventoryVoucherEntry!;
            entry.Date = k.On;
            entry.Lines[0].SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
            entry.Lines[0].SelectedGodown = entry.Godowns.Single(g => g.Id == k.GodownId);
            entry.Lines[0].QuantityText = "90";
            Assert.True(entry.Accept());

            var count = vm.Company!.InventoryVouchers.Single();
            OpenDayBookOnStockRow(window, vm, count.Id);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(Screen.InventoryVoucherDetail, vm.CurrentScreen);
            var line = vm.InventoryVoucherDetail!.Rows.Single(r => !r.IsHeader);
            Assert.Equal("Counted", line.Col2);
            Assert.Equal(string.Empty, line.Col4);   // no rate
            Assert.Equal(string.Empty, line.Col5);   // and therefore no value
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (c) Alt+X — cancel

    /// <summary>
    /// Alt+X on a highlighted stock row raises ONE confirmation naming the document, and — the half that matters
    /// most — cancels NOTHING until it is answered. A destructive verb that acted on the keystroke would pass a
    /// weaker test that only checked the flag afterwards.
    /// </summary>
    [AvaloniaFact]
    public void AltX_on_a_stock_row_raises_one_confirmation_and_cancels_nothing_yet()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Stock Cancel Prompt Co");
            var receipt = PostThroughTheScreen(vm, k, VoucherBaseType.ReceiptNote, qty: 10m);
            var stockBefore = OnHand(vm.Company!, k);
            OpenDayBookOnStockRow(window, vm, receipt.Id);

            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);

            Assert.True(vm.IsAcceptPromptOpen);
            Assert.Contains("Cancel", vm.AcceptPromptText, StringComparison.Ordinal);
            Assert.Contains("Receipt Note", vm.AcceptPromptText, StringComparison.Ordinal);  // names the document
            Assert.Contains("(Y/N)", vm.AcceptPromptText, StringComparison.Ordinal);

            Assert.False(vm.Company!.FindInventoryVoucher(receipt.Id)!.Cancelled);
            Assert.Equal(stockBefore, OnHand(vm.Company!, k));          // nothing moved yet
            Assert.Empty(vm.Company!.VoucherEditLog);                   // and nothing was logged yet
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE VERB ITSELF, AND ITS STOCK CONSEQUENCE.</b> "Y" cancels: the voucher is flagged, it KEEPS its
    /// number, <b>closing stock moves back</b>, the audit entry is written, the row stays in the Day Book flagged,
    /// and all of it is PERSISTED. Asserting the flag alone would pass over an engine that greyed the row and
    /// left the stock on the shelf — which moves the Balance Sheet.
    /// </summary>
    [AvaloniaFact]
    public void Y_cancels_the_stock_voucher_moves_the_stock_back_logs_it_and_persists()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Stock Cancel Y Co");
            var receipt = PostThroughTheScreen(vm, k, VoucherBaseType.ReceiptNote, qty: 10m);
            var numberBefore = receipt.Number;
            Assert.Equal(110m, OnHand(vm.Company!, k));                 // 100 opening + 10 received

            OpenDayBookOnStockRow(window, vm, receipt.Id);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);

            var c = vm.Company!;
            var cancelled = c.FindInventoryVoucher(receipt.Id)!;
            Assert.True(cancelled.Cancelled);
            Assert.Equal(numberBefore, cancelled.Number);               // the number is KEPT
            Assert.Equal(100m, OnHand(c, k));                           // and the stock went back
            Assert.False(vm.IsAcceptPromptOpen);

            // The audit trail: a cancelled stock movement that leaves no record is worse than one that cannot be
            // cancelled at all.
            var logged = Assert.Single(c.VoucherEditLog);
            Assert.Equal(receipt.Id, logged.VoucherId);
            Assert.Equal(VoucherEditVerb.Cancel, logged.Verb);

            // The live report rebuilt itself where the operator is standing: still listed, still drillable, flagged.
            var row = vm.Reports!.Rows.Single(r => r.DrillInventoryVoucherId == receipt.Id);
            Assert.True(row.IsCancelled);

            // …and it survives a reload, which is the only proof the snapshot was written.
            var storage = new CompanyStorage(dir);
            var reopened = storage.Load(storage.ListCompanies().Single(e => e.Name == k.CompanyName));
            Assert.True(reopened.FindInventoryVoucher(receipt.Id)!.Cancelled);
            Assert.Equal(100m, new InventoryLedger(reopened).OnHand(
                k.ItemId, k.GodownId, reopened.FinancialYearStart.AddYears(1)));
            Assert.Single(reopened.VoucherEditLog);
        }
        finally { Close(window, dir); }
    }

    /// <summary>"N" answers the question with a no: the voucher stays posted and the stock stays where it is.</summary>
    [AvaloniaFact]
    public void N_leaves_the_stock_voucher_posted_and_the_stock_where_it_is()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Stock Cancel N Co");
            var receipt = PostThroughTheScreen(vm, k, VoucherBaseType.ReceiptNote, qty: 10m);
            OpenDayBookOnStockRow(window, vm, receipt.Id);

            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.None);
            Pump(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.False(vm.Company!.FindInventoryVoucher(receipt.Id)!.Cancelled);
            Assert.Equal(110m, OnHand(vm.Company!, k));
            Assert.Empty(vm.Company!.VoucherEditLog);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE ARMED CANCELLATION DIES WITH ITS PROMPT.</b> Escape dismisses the question; a later plain "Y"
    /// answering some UNRELATED Accept confirmation must not then void the stock movement. This is the exact
    /// defect the accounting arm's own comment records, one aggregate over, and it is the reason the disarm line
    /// exists in <c>ResetMasterAcceptPrompt</c>.
    /// </summary>
    [AvaloniaFact]
    public void An_escaped_cancellation_cannot_be_executed_by_a_later_unrelated_Y()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Stock Disarm Co");
            var receipt = PostThroughTheScreen(vm, k, VoucherBaseType.ReceiptNote, qty: 10m);
            OpenDayBookOnStockRow(window, vm, receipt.Id);

            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            Assert.True(vm.IsAcceptPromptOpen);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);
            Assert.False(vm.IsAcceptPromptOpen);

            // A bare Y anywhere afterwards must do nothing to the voucher.
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);

            Assert.False(vm.Company!.FindInventoryVoucher(receipt.Id)!.Cancelled);
            Assert.Equal(110m, OnHand(vm.Company!, k));
        }
        finally { Close(window, dir); }
    }

    /// <summary>An already-cancelled voucher is REFUSED by name rather than silently re-armed: a prompt whose
    /// answer changes nothing trains an operator to answer prompts without reading them.</summary>
    [AvaloniaFact]
    public void AltX_on_an_already_cancelled_stock_voucher_says_so_and_raises_no_prompt()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Stock Recancel Co");
            var receipt = PostThroughTheScreen(vm, k, VoucherBaseType.ReceiptNote, qty: 10m);
            OpenDayBookOnStockRow(window, vm, receipt.Id);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);
            Assert.True(vm.Company!.FindInventoryVoucher(receipt.Id)!.Cancelled);

            vm.Reports!.SelectedRow = vm.Reports!.Rows.Single(r => r.DrillInventoryVoucherId == receipt.Id);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.Contains("already cancelled", vm.Notice, StringComparison.OrdinalIgnoreCase);
            Assert.Single(vm.Company!.VoucherEditLog);          // and no second log line for a non-event
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE CANCELLED FACT IS PAINTED, IN WORDS, IN THE REALISED VISUAL TREE.</b> A view-model flag
    /// assertion would pass over a badge no template shows, and colour alone is never the only carrier of a fact
    /// this material. Drilled BEFORE the cancel as well as after, so the assertion cannot be satisfied by a badge
    /// that is simply always on.
    /// </summary>
    [AvaloniaFact]
    public void A_cancelled_stock_voucher_paints_the_CANCELLED_badge_in_the_visual_tree()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Stock Badge Co");
            var receipt = PostThroughTheScreen(vm, k, VoucherBaseType.ReceiptNote, qty: 10m);

            // Live: no badge.
            OpenDayBookOnStockRow(window, vm, receipt.Id);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.InventoryVoucherDetail, vm.CurrentScreen);
            Assert.DoesNotContain(RenderedText(window), s => s.Contains("CANCELLED", StringComparison.Ordinal));

            // Back to the Day Book and cancel there. ⚠️ Alt+X is gated on IsLiveReportPage, so it does NOT fire
            // from a drill column — a pre-existing asymmetry with Alt+D that applies identically to the ACCOUNTING
            // detail column, reported rather than widened for this aggregate alone. See OpenInventoryVoucherDetail.
            vm.Back();
            Pump(window);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);
            Assert.True(vm.Company!.FindInventoryVoucher(receipt.Id)!.Cancelled);
            Assert.Equal(100m, OnHand(vm.Company!, k));

            // Cancelled: the badge paints, in the realised tree, as text.
            vm.Reports!.SelectedRow = vm.Reports!.Rows.Single(r => r.DrillInventoryVoucherId == receipt.Id);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.InventoryVoucherDetail, vm.CurrentScreen);
            Assert.True(vm.InventoryVoucherDetail!.IsCancelled);
            Assert.Contains(
                Descendants(window).OfType<TextBlock>().Where(t => t.IsEffectivelyVisible),
                t => (t.Text ?? string.Empty).Contains("CANCELLED", StringComparison.Ordinal));
        }
        finally { Close(window, dir); }
    }

    /// <summary>⚠️ <b>The Alt+X asymmetry, PINNED rather than left as prose.</b> Alt+X does not fire from the
    /// pure-stock detail column, exactly as it does not fire from the accounting one — both are gated on
    /// <c>IsLiveReportPage</c>. This test exists so that widening Alt+X to the drill columns (a small, separate
    /// change the user is owed as a decision) goes red HERE and is made deliberately, rather than being discovered
    /// by an operator.</summary>
    [AvaloniaFact]
    public void AltX_does_not_reach_either_detail_column_today_and_this_pins_that()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Stock AltX Gate Co");
            var receipt = PostThroughTheScreen(vm, k, VoucherBaseType.ReceiptNote, qty: 10m);
            OpenDayBookOnStockRow(window, vm, receipt.Id);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.InventoryVoucherDetail, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.False(vm.Company!.FindInventoryVoucher(receipt.Id)!.Cancelled);
            Assert.Equal(110m, OnHand(vm.Company!, k));
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (d) Alt+D — delete

    /// <summary>
    /// <b>THE DELETE VERB AND ITS STOCK CONSEQUENCE.</b> Alt+D then "Y" removes the voucher, un-moves the stock,
    /// writes the audit entry that is now the ONLY evidence the movement ever existed, drops the row from the Day
    /// Book, and persists. Nothing happens on the keystroke alone.
    /// </summary>
    [AvaloniaFact]
    public void AltD_then_Y_deletes_the_stock_voucher_un_moves_the_stock_and_persists()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Stock Delete Co");
            var receipt = PostThroughTheScreen(vm, k, VoucherBaseType.ReceiptNote, qty: 6m);
            Assert.Equal(106m, OnHand(vm.Company!, k));
            OpenDayBookOnStockRow(window, vm, receipt.Id);

            window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Alt);
            Pump(window);
            Assert.True(vm.IsAcceptPromptOpen);
            Assert.Contains("Delete", vm.AcceptPromptText, StringComparison.Ordinal);
            Assert.Contains("Receipt Note", vm.AcceptPromptText, StringComparison.Ordinal);
            Assert.NotNull(vm.Company!.FindInventoryVoucher(receipt.Id));       // nothing yet

            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);

            var c = vm.Company!;
            Assert.Null(c.FindInventoryVoucher(receipt.Id));
            Assert.Equal(100m, OnHand(c, k));                                   // the stock went back
            Assert.DoesNotContain(vm.Reports!.Rows, r => r.DrillInventoryVoucherId == receipt.Id);

            var logged = Assert.Single(c.VoucherEditLog);
            Assert.Equal(VoucherEditVerb.Delete, logged.Verb);
            Assert.Equal(receipt.Id, logged.VoucherId);

            var storage = new CompanyStorage(dir);
            var reopened = storage.Load(storage.ListCompanies().Single(e => e.Name == k.CompanyName));
            Assert.Null(reopened.FindInventoryVoucher(receipt.Id));
            Assert.Equal(100m, new InventoryLedger(reopened).OnHand(
                k.ItemId, k.GodownId, reopened.FinancialYearStart.AddYears(1)));
        }
        finally { Close(window, dir); }
    }

    /// <summary>"N" on the deletion leaves the voucher on the books, stock and all.</summary>
    [AvaloniaFact]
    public void N_leaves_the_stock_voucher_on_the_books()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Stock Delete N Co");
            var receipt = PostThroughTheScreen(vm, k, VoucherBaseType.ReceiptNote, qty: 6m);
            OpenDayBookOnStockRow(window, vm, receipt.Id);

            window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Alt);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.None);
            Pump(window);

            Assert.NotNull(vm.Company!.FindInventoryVoucher(receipt.Id));
            Assert.Equal(106m, OnHand(vm.Company!, k));
            Assert.Empty(vm.Company!.VoucherEditLog);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>DELETING FROM INSIDE THE DRILL COLUMN CLOSES IT.</b> A pane left showing a document that no longer
    /// exists is the stale-pane defect already filed on the accounting side; the verb must reach the shell from
    /// this surface too, and the column must pop rather than linger.
    /// </summary>
    [AvaloniaFact]
    public void AltD_inside_the_detail_column_deletes_and_pops_the_now_empty_column()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Stock Pane Delete Co");
            var receipt = PostThroughTheScreen(vm, k, VoucherBaseType.ReceiptNote, qty: 6m);
            OpenDayBookOnStockRow(window, vm, receipt.Id);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.InventoryVoucherDetail, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Alt);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);

            Assert.Null(vm.Company!.FindInventoryVoucher(receipt.Id));
            Assert.NotEqual(Screen.InventoryVoucherDetail, vm.CurrentScreen);
            Assert.Null(vm.InventoryVoucherDetail);
            Assert.Equal(100m, OnHand(vm.Company!, k));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE VERB ACTS ON THE VOUCHER THE CONFIRMATION NAMED</b>, not on whatever is highlighted when "Y"
    /// lands. Arming on one stock voucher and then moving the highlight to another must still delete the first.
    /// </summary>
    [AvaloniaFact]
    public void The_armed_deletion_acts_on_the_voucher_it_named_not_on_the_new_highlight()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Stock Armed Target Co");
            var first = PostThroughTheScreen(vm, k, VoucherBaseType.ReceiptNote, qty: 6m);
            var second = PostThroughTheScreen(vm, k, VoucherBaseType.ReceiptNote, qty: 3m);
            OpenDayBookOnStockRow(window, vm, first.Id);

            window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Alt);
            Pump(window);

            // Move the highlight AFTER arming — the confirmation named the first voucher.
            vm.Reports!.SelectedRow = vm.Reports!.Rows.Single(r => r.DrillInventoryVoucherId == second.Id);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);

            Assert.Null(vm.Company!.FindInventoryVoucher(first.Id));
            Assert.NotNull(vm.Company!.FindInventoryVoucher(second.Id));
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (e) the verbs that are NOT built

    /// <summary>
    /// 🔴 <b>A VERB WITH NO STOCK IMPLEMENTATION MUST SAY SO — IT MAY NOT BE A SILENT KEY.</b> Listing these
    /// vouchers in the Day Book created a dead key that did not exist before it: Ctrl+Enter (alter) and Alt+2
    /// (duplicate) both resolve through the ACCOUNTING slot, which is empty here, so both were quiet no-ops on a
    /// row the operator can plainly see. "Honestly unavailable" is not a property a silent key can have.
    ///
    /// <para>Alteration really is unavailable: <c>VoucherEntryViewModel.ForAlter</c> refuses every
    /// inventory-aggregate voucher (pinned for all twelve base kinds by <c>VoucherAlterRefusalTests</c>) because
    /// no counterpart of <c>Replace</c> exists for this aggregate. That remains an open, named divergence; what
    /// ships here is the sentence, and it points at the two routes that DO work.</para>
    /// </summary>
    [AvaloniaTheory]
    [InlineData(PhysicalKey.Enter, true)]     // Ctrl+Enter — alter
    [InlineData(PhysicalKey.Digit2, false)]   // Alt+2      — duplicate
    public void An_unbuilt_verb_on_a_stock_row_names_the_limit_instead_of_doing_nothing(
        PhysicalKey key, bool control)
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, $"Stock Unbuilt {key} Co");
            var receipt = PostThroughTheScreen(vm, k, VoucherBaseType.ReceiptNote, qty: 6m);
            OpenDayBookOnStockRow(window, vm, receipt.Id);

            window.KeyPressQwerty(key, control ? RawInputModifiers.Control : RawInputModifiers.Alt);
            Pump(window);

            Assert.False(string.IsNullOrWhiteSpace(vm.Notice));
            Assert.Contains("stock voucher", vm.Notice, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Alt+X", vm.Notice, StringComparison.Ordinal);

            // Nothing was opened and nothing was changed — the keystroke reported a limit, it did not act.
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.NotNull(vm.Company!.FindInventoryVoucher(receipt.Id));
            Assert.False(vm.Company!.FindInventoryVoucher(receipt.Id)!.Cancelled);
        }
        finally { Close(window, dir); }
    }

    /// <summary>The refusal must not spread to the ACCOUNTING rows sharing the report: Ctrl+Enter on a normal
    /// voucher row still opens its alteration screen.</summary>
    [AvaloniaFact]
    public void The_stock_refusal_does_not_touch_an_accounting_row_in_the_same_Day_Book()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Mixed Alter Co");
            PostThroughTheScreen(vm, k, VoucherBaseType.ReceiptNote, qty: 6m);

            var c = vm.Company!;
            var cash = c.FindLedgerByName("Cash")!;
            var accounting = new LedgerService(c).Post(new Voucher(
                Guid.NewGuid(), c.FindVoucherTypeByName("Receipt")!.Id, k.On,
                new[]
                {
                    new EntryLine(cash.Id, Money.FromRupees(500m), DrCr.Debit),
                    new EntryLine(cash.Id, Money.FromRupees(500m), DrCr.Credit),
                }));

            vm.OpenReport(ReportKind.DayBook);
            Pump(window);
            vm.Reports!.SelectedRow = vm.Reports!.Rows.Single(r => r.DrillVoucherId == accounting.Id);
            Pump(window);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
            Pump(window);

            Assert.DoesNotContain("stock voucher", vm.Notice ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
        }
        finally { Close(window, dir); }
    }
}
