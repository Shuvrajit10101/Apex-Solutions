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
/// <b>Census rows 4.9–4.16 and 9.2 — ALTERING a posted pure-stock voucher. The USER-ROUTE half.</b>
///
/// <para><b>THE GAP ALL TEN ROWS SHARED, and it was ONE missing method.</b> Listing, drill, Alt+X cancel and
/// Alt+D delete all shipped for this aggregate in PR #107. <c>InventoryPostingService</c> had <c>Post</c>,
/// <c>Cancel</c> and <c>Delete</c> and <b>no <c>Replace</c></b>, so alteration was the one verb with no engine —
/// which is the single reason every one of rows 4.9–4.16 was held at <c>PARTIAL</c>, and (with the registers'
/// unreachable rows) the surviving reason on 9.2.</para>
///
/// <para><b>KEYBOARD ROUTE UNDER TEST, end to end:</b> Gateway → Reports → Day Book → arrow to the stock row →
/// <b>Ctrl+Enter</b> opens the alteration column pre-filled → edit → <b>Ctrl+A</b> saves. Driven through the
/// REAL <see cref="MainWindow"/> tunnel key handler against a real company on a throwaway <c>.db</c>. Every test
/// here fails on today's <c>main</c>: the chord reached <c>RefuseVoucherVerbOnStockRow</c> and only printed a
/// sentence saying the verb did not exist.</para>
///
/// <para><b>FIDELITY (R7; RULING 14).</b> The chord and the save key are inherited from the accounting
/// alteration door, whose provenance is recorded in full on <c>MainWindowViewModel.RequestAlterHighlightedVoucher</c>
/// — Ctrl+Enter a deliberate widening of an attested alteration gesture, Ctrl+A the attested save.
/// <b>OURS — no source speaks:</b> that a PURE-STOCK voucher is reachable by that chord, the refusal sentences,
/// and the column titles. Nothing below may be re-labelled as fidelity to another product.</para>
/// </summary>
public sealed class InventoryVoucherAlterationRouteTests
{
    // ============================================================ harness (mirrors InventoryVoucherDayBookRouteTests)

    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexInvAlter_" + Guid.NewGuid().ToString("N"));
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

    private static List<string> RenderedText(MainWindow window) =>
        Descendants(window).OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList();

    private sealed record Kit(Guid ItemId, Guid GodownId, DateOnly On);

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

        return new Kit(item.Id, c.MainLocation!.Id, c.FinancialYearStart.AddDays(5));
    }

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
        Assert.True(entry.Accept(), entry.Message);

        return vm.Company!.InventoryVouchers.Single(v => !before.Contains(v.Id));
    }

    private static decimal OnHand(Company c, Kit k) =>
        new InventoryLedger(c).OnHand(k.ItemId, k.GodownId, c.FinancialYearStart.AddYears(1));

    private static void OpenDayBookOnStockRow(MainWindow window, MainWindowViewModel vm, Guid stockVoucherId)
    {
        vm.OpenReport(ReportKind.DayBook);
        Pump(window);
        vm.Reports!.SelectedRow = vm.Reports!.Rows.Single(r => r.DrillInventoryVoucherId == stockVoucherId);
        Pump(window);
    }

    // ============================================================ (a) the door opens, pre-filled

    /// <summary>
    /// 🔴 <b>THE DRIVING TEST FOR ALL EIGHT OF 4.9–4.16.</b> Ctrl+Enter on a Day Book stock row opens the
    /// inventory entry screen in ALTERING mode, carrying the posted voucher's own number, date and figures.
    /// On today's main the keystroke only writes "Alteration (Ctrl+Enter) is not available for a stock voucher"
    /// to the notice bar, so the very first assertion fails.
    /// </summary>
    [AvaloniaFact]
    public void CtrlEnter_on_a_Day_Book_stock_row_opens_the_alteration_screen_pre_filled()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Stock Alter Route Co");
            var delivery = PostThroughTheScreen(vm, k, VoucherBaseType.DeliveryNote, qty: 6m, rate: "80");
            OpenDayBookOnStockRow(window, vm, delivery.Id);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
            Pump(window);

            Assert.Equal(Screen.InventoryVoucherEntry, vm.CurrentScreen);
            var entry = vm.InventoryVoucherEntry!;
            Assert.True(entry.IsAltering, "The screen opened as a NEW entry, which would post a second voucher.");
            Assert.Equal(delivery.Id, entry.AlteringVoucherId);

            // Pre-filled from the POSTED voucher, not from the constructor's defaults.
            Assert.Equal(delivery.Number, entry.VoucherNumber);
            Assert.Equal(k.On, entry.Date);
            var line = Assert.Single(entry.Lines);
            Assert.Equal(k.ItemId, line.SelectedItem!.Id);
            Assert.Equal(6m, line.ParsedQuantity);
            Assert.Equal(80m, line.ParsedRate);

            // 🔴 The REALISED evidence: the cascade column says ALTERATION, so the operator can tell an amendment
            // of a posted voucher from a fresh entry. Reusing the creation label is the defect the accounting
            // door already had to fix.
            Assert.Contains(RenderedText(window),
                s => s.Contains("Alteration", StringComparison.Ordinal));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE CLUSTER PROOF — ALL EIGHT BASE KINDS, NOT ONE WITH SEVEN ASSUMED.</b> Rows 4.9–4.16 are eight
    /// census rows served by ONE shared arm, and "the shared arm reaches every member" is a claim that has to be
    /// measured per member: the four line shapes go down four different branches of
    /// <c>InventoryVoucherEntryViewModel.RehydrateFrom</c> (order lines, counted lines, the two-sided Stock
    /// Journal, and the direction-derived movement note), and each of those branches can fail on its own.
    ///
    /// <para>For every kind this asserts the whole round trip in figures: the posted voucher re-opens, its
    /// quantity is re-keyed, Ctrl+A saves, and the SAME voucher (same id, same number, same count) carries the
    /// new quantity. A kind whose rehydration silently dropped a field would fail the last assertion, because
    /// the writer would rebuild a voucher the round-trip backstop had already refused.</para>
    /// </summary>
    [AvaloniaTheory]
    [InlineData(VoucherBaseType.StockJournal)]    // 4.9
    [InlineData(VoucherBaseType.PhysicalStock)]   // 4.10
    [InlineData(VoucherBaseType.SalesOrder)]      // 4.11
    [InlineData(VoucherBaseType.PurchaseOrder)]   // 4.12
    [InlineData(VoucherBaseType.DeliveryNote)]    // 4.13
    [InlineData(VoucherBaseType.ReceiptNote)]     // 4.14
    [InlineData(VoucherBaseType.RejectionOut)]    // 4.15
    [InlineData(VoucherBaseType.RejectionIn)]     // 4.16
    public void Every_one_of_the_eight_base_kinds_alters_end_to_end(VoucherBaseType baseType)
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, $"Alter All Kinds {baseType} Co");
            var posted = PostAnyKindThroughTheScreen(vm, k, baseType, qty: 6m);
            var countBefore = vm.Company!.InventoryVouchers.Count;
            var postedNumber = posted.Number;

            OpenDayBookOnStockRow(window, vm, posted.Id);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
            Pump(window);

            Assert.Equal(Screen.InventoryVoucherEntry, vm.CurrentScreen);
            var entry = vm.InventoryVoucherEntry!;
            Assert.True(entry.IsAltering, $"{baseType} opened as a NEW entry — Ctrl+A would post a second voucher.");
            Assert.Equal(posted.Id, entry.AlteringVoucherId);
            Assert.Equal(postedNumber, entry.VoucherNumber);
            Assert.Equal(6m, entry.Lines[0].ParsedQuantity);

            // Re-key. A Stock Journal must stay balanced, so BOTH arms move together.
            entry.Lines[0].QuantityText = "2";
            if (baseType == VoucherBaseType.StockJournal) entry.DestinationLines[0].QuantityText = "2";

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);

            // A refused save leaves the altering screen up with its reason on Message — surfaced here so a
            // failure names the branch that refused instead of only showing a quantity that did not move.
            Assert.False(
                vm.CurrentScreen == Screen.InventoryVoucherEntry && vm.InventoryVoucherEntry is { IsAltering: true },
                $"{baseType}: the alteration was not saved — {vm.InventoryVoucherEntry?.Message}");
            Assert.Equal(countBefore, vm.Company!.InventoryVouchers.Count);

            var amended = vm.Company!.FindInventoryVoucher(posted.Id);
            Assert.NotNull(amended);
            Assert.Equal(postedNumber, amended!.Number);
            Assert.Equal(2m, FirstKeyedQuantity(amended));
            Assert.Contains(vm.Company!.VoucherEditLog,
                e => e.VoucherId == posted.Id && e.Verb == VoucherEditVerb.Alter);
        }
        finally { Close(window, dir); }
    }

    /// <summary>The quantity the PRIMARY (keyed) grid of a voucher of any of the eight kinds holds — the one
    /// figure whose change proves the alteration reached the book.</summary>
    private static decimal FirstKeyedQuantity(InventoryVoucher v) =>
        v.OrderLines.Count > 0 ? v.OrderLines[0].Quantity
        : v.PhysicalLines.Count > 0 ? v.PhysicalLines[0].CountedQuantity
        : v.Allocations[0].Quantity;

    /// <summary>
    /// Posts one voucher of <paramref name="baseType"/> through the REAL entry screen. A Stock Journal needs a
    /// second item on its destination arm (a journal consuming and producing the same item in the same godown is
    /// a no-op the engine would rightly question), and a Physical Stock voucher takes no rate.
    /// </summary>
    private static InventoryVoucher PostAnyKindThroughTheScreen(
        MainWindowViewModel vm, Kit k, VoucherBaseType baseType, decimal qty)
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
        if (!entry.IsPhysicalStock) line.RateText = "50";

        if (entry.IsStockJournal)
        {
            var other = new InventoryService(vm.Company!).CreateStockItem(
                "Assembly", vm.Company!.FindStockGroupByName("Goods")!.Id, vm.Company!.Units[0].Id);
            var dest = entry.DestinationLines[0];
            dest.SelectedItem = entry.StockItems.Single(i => i.Id == other.Id);
            dest.SelectedGodown = entry.Godowns.Single(g => g.Id == k.GodownId);
            dest.QuantityText = qty.ToString(CultureInfo.InvariantCulture);
            dest.RateText = "50";
        }

        Assert.True(entry.Accept(), $"{baseType}: {entry.Message}");
        return vm.Company!.InventoryVouchers.Single(v => !before.Contains(v.Id));
    }

    /// <summary>The second surface: the read-only stock drill column itself. Alt+D already reached it, so
    /// leaving Ctrl+Enter out would have left the two lifecycle verbs reachable from different screens.</summary>
    [AvaloniaFact]
    public void CtrlEnter_also_opens_the_alteration_from_the_stock_drill_column()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Stock Drill Alter Co");
            var receipt = PostThroughTheScreen(vm, k, VoucherBaseType.ReceiptNote, qty: 9m);
            OpenDayBookOnStockRow(window, vm, receipt.Id);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); // drill
            Pump(window);
            Assert.Equal(Screen.InventoryVoucherDetail, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
            Pump(window);

            Assert.Equal(Screen.InventoryVoucherEntry, vm.CurrentScreen);
            Assert.True(vm.InventoryVoucherEntry!.IsAltering);
            Assert.Equal(receipt.Id, vm.InventoryVoucherEntry!.AlteringVoucherId);
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (b) the stock consequence, through the keyboard

    /// <summary>
    /// 🔴 <b>THE WRONG-MONEY PROOF, THROUGH THE REAL KEYBOARD.</b> Opening 100, delivered 6 ⇒ on-hand 94. The
    /// delivery is altered to 2 through the screen and saved with Ctrl+A ⇒ on-hand must be <b>100 − 2 = 98</b>.
    ///
    /// <para>The figure is chosen so the two plausible wrong implementations give two other answers: posting the
    /// amendment as a SECOND voucher gives 100 − 6 − 2 = 92, and failing to apply the new figures at all leaves
    /// 94. The voucher COUNT is asserted too, because 98 alone would also be reached by deleting the original
    /// and posting a fresh one — which would lose the number and the audit trail.</para>
    /// </summary>
    [AvaloniaFact]
    public void Altering_a_delivery_through_the_screen_moves_closing_stock_by_the_difference_only()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Stock Figures Co");
            var delivery = PostThroughTheScreen(vm, k, VoucherBaseType.DeliveryNote, qty: 6m, rate: "80");
            Assert.Equal(94m, OnHand(vm.Company!, k));

            var postedNumber = delivery.Number;
            var countBefore = vm.Company!.InventoryVouchers.Count;

            OpenDayBookOnStockRow(window, vm, delivery.Id);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
            Pump(window);

            vm.InventoryVoucherEntry!.Lines[0].QuantityText = "2";
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);

            Assert.Equal(98m, OnHand(vm.Company!, k));
            Assert.Equal(countBefore, vm.Company!.InventoryVouchers.Count);

            var amended = vm.Company!.FindInventoryVoucher(delivery.Id)!;
            Assert.Equal(postedNumber, amended.Number);
            Assert.Equal(2m, amended.Allocations[0].Quantity);

            // The audit event, on the SAME verb an ordinary voucher's alteration records.
            Assert.Contains(vm.Company!.VoucherEditLog,
                e => e.VoucherId == delivery.Id && e.Verb == VoucherEditVerb.Alter);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE ALTERATION SURVIVES A RELOAD.</b> The engine mutates the in-memory aggregate and the save
    /// happens after it, so an alteration that was never persisted would leave the books and the .db disagreeing
    /// — and the next open would show the ORIGINAL figures with no sign anything was wrong. Re-opening the
    /// company from disk is the only assertion that catches it.
    /// </summary>
    [AvaloniaFact]
    public void The_altered_figures_survive_closing_and_re_opening_the_company()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Stock Persist Alter Co");
            var delivery = PostThroughTheScreen(vm, k, VoucherBaseType.DeliveryNote, qty: 6m, rate: "80");

            OpenDayBookOnStockRow(window, vm, delivery.Id);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
            Pump(window);
            vm.InventoryVoucherEntry!.Lines[0].QuantityText = "2";
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);

            var freshStorage = new CompanyStorage(dir);
            var reloaded = freshStorage.Load(
                freshStorage.ListCompanies().Single(e => e.Name == "Stock Persist Alter Co"));
            var onDisk = reloaded.FindInventoryVoucher(delivery.Id);

            Assert.NotNull(onDisk);
            Assert.Equal(2m, onDisk!.Allocations[0].Quantity);
            Assert.Equal(delivery.Number, onDisk.Number);
            Assert.Equal(98m, new InventoryLedger(reloaded).OnHand(
                k.ItemId, k.GodownId, reloaded.FinancialYearStart.AddYears(1)));
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (c) the duplicate-posting trap

    /// <summary>
    /// 🔴 <b>THE MUTATION THIS PAIR EXISTS FOR.</b> <c>Accept</c> builds with a FRESH <see cref="Guid"/> and
    /// number 0. Reached on an altering screen it would post a SECOND stock movement and leave the original
    /// standing — closing stock double-counted, and the Balance Sheet moved, with a green suite either way.
    /// It is refused in TWO places (the shell's Ctrl+A routing and <c>Accept</c> itself); this test reds if the
    /// inner one is removed, and <see cref="Altering_a_delivery_through_the_screen_moves_closing_stock_by_the_difference_only"/>
    /// reds if the outer one is.
    /// </summary>
    [AvaloniaFact]
    public void Accept_is_refused_on_an_altering_screen_so_it_cannot_post_a_second_voucher()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Stock Double Post Co");
            var delivery = PostThroughTheScreen(vm, k, VoucherBaseType.DeliveryNote, qty: 6m, rate: "80");
            var countBefore = vm.Company!.InventoryVouchers.Count;

            OpenDayBookOnStockRow(window, vm, delivery.Id);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
            Pump(window);

            var entry = vm.InventoryVoucherEntry!;
            entry.Lines[0].QuantityText = "2";

            Assert.False(entry.Accept());
            Assert.Contains("Ctrl+A", entry.Message!, StringComparison.Ordinal);
            Assert.Equal(countBefore, vm.Company!.InventoryVouchers.Count);
            Assert.Equal(94m, OnHand(vm.Company!, k)); // still the ORIGINAL 6 out
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (d) the refusals are NAMED, never silent

    /// <summary>
    /// A voucher deleted from another column between the report being drawn and the chord being pressed. The
    /// stale id must produce a NAMED sentence, not a dead key and not a crash.
    /// </summary>
    [AvaloniaFact]
    public void A_stock_row_whose_voucher_was_deleted_refuses_by_name()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Stock Stale Row Co");
            var delivery = PostThroughTheScreen(vm, k, VoucherBaseType.DeliveryNote, qty: 6m, rate: "80");
            OpenDayBookOnStockRow(window, vm, delivery.Id);

            // Deleted behind the report's back — exactly what a second column doing Alt+D would do.
            new InventoryPostingService(vm.Company!).Delete(delivery.Id);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
            Pump(window);

            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.NotEqual(Screen.InventoryVoucherEntry, vm.CurrentScreen);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>A Job Work ORDER is refused BY NAME rather than opened on the wrong screen.</b> Its finished good
    /// and component list are keyed on <c>JobWorkOrderEntryViewModel</c>, which this door does not open, so
    /// rebuilding it here would DROP the whole job-work payload. The refusal is the honest outcome and it is
    /// shown — which is the half census row 9.2 says matters: <i>"a silent no-op with no named refusal is the
    /// worst of the three failure modes."</i>
    /// </summary>
    [AvaloniaFact]
    public void A_Job_Work_order_is_refused_by_name_and_not_silently()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Job Work Refusal Co");
            var c = vm.Company!;
            var jwType = c.VoucherTypes.FirstOrDefault(t => t.BaseType == VoucherBaseType.JobWorkOutOrder);
            Assert.NotNull(jwType);

            var order = new InventoryPostingService(c).Post(InventoryVoucher.JobWork(
                Guid.NewGuid(), jwType!.Id, k.On,
                new JobWorkOrder(
                    JobWorkDirection.Out, "JW-1", k.ItemId, 5m,
                    new[] { new JobWorkOrderLine(k.ItemId, JobWorkComponentTrack.PendingToIssue, 5m) })));

            OpenDayBookOnStockRow(window, vm, order.Id);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
            Pump(window);

            Assert.False(string.IsNullOrWhiteSpace(vm.Notice));
            Assert.Contains("Job Work", vm.Notice!, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (e) census 9.2 — the registers are reachable

    /// <summary>
    /// 🔴 <b>CENSUS 9.2 — THE SILENT NO-OP, MEASURED AND CLOSED.</b> The four Job Work registers projected their
    /// rows from <c>JobWorkReports</c>, whose row records carried <b>no voucher id at all</b>. Every keyboard
    /// verb on those reports resolves through <c>ReportRow.DrillInventoryVoucherId</c>, so Enter did not drill
    /// and Ctrl+Enter, Alt+X and Alt+D were SILENT no-ops on a voucher the operator could plainly see.
    ///
    /// <para>This test fails on today's main at the first assertion: <c>DrillInventoryVoucherId</c> is
    /// <see cref="Guid.Empty"/> on every row of both Order Books.</para>
    /// </summary>
    [AvaloniaTheory]
    [InlineData(ReportKind.JobWorkOutOrderBook)]
    [InlineData(ReportKind.JobWorkInOrderBook)]
    public void A_Job_Work_Order_Book_row_carries_the_id_its_keyboard_verbs_resolve_through(ReportKind kind)
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, $"Job Work Reach {kind} Co");
            var c = vm.Company!;
            var direction = kind == ReportKind.JobWorkOutOrderBook
                ? VoucherBaseType.JobWorkOutOrder
                : VoucherBaseType.JobWorkInOrder;
            var jwType = c.VoucherTypes.FirstOrDefault(t => t.BaseType == direction);
            Assert.NotNull(jwType);

            var order = new InventoryPostingService(c).Post(InventoryVoucher.JobWork(
                Guid.NewGuid(), jwType!.Id, k.On,
                new JobWorkOrder(
                    kind == ReportKind.JobWorkOutOrderBook ? JobWorkDirection.Out : JobWorkDirection.In,
                    "JW-9", k.ItemId, 5m,
                    new[] { new JobWorkOrderLine(k.ItemId, JobWorkComponentTrack.PendingToIssue, 5m) })));

            vm.OpenReport(kind);
            Pump(window);

            Assert.NotEmpty(vm.Reports!.Rows);
            Assert.All(vm.Reports!.Rows, r => Assert.Equal(order.Id, r.DrillInventoryVoucherId));

            // And the verb really arrives: standing on the row, Alt+X now ARMS a confirmation instead of doing
            // nothing. (The alteration itself is refused by name for this family — see the test above.)
            vm.Reports!.SelectedRow = vm.Reports!.Rows[0];
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            Assert.True(vm.IsAcceptPromptOpen,
                "Alt+X on a Job Work Order Book row is still a silent no-op — the row is visible but unreachable.");
        }
        finally { Close(window, dir); }
    }

    /// <summary>The Material In/Out registers, same defect and same fix. N lines of one voucher all carry the
    /// same id, which is correct: the lifecycle verbs act on the voucher, not on the line.</summary>
    [AvaloniaFact]
    public void A_Material_register_row_carries_the_id_its_keyboard_verbs_resolve_through()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Material Reach Co");
            var c = vm.Company!;
            var type = c.VoucherTypes.FirstOrDefault(t => t.BaseType == VoucherBaseType.MaterialOut);
            Assert.NotNull(type);

            var movement = new InventoryPostingService(c).Post(InventoryVoucher.MaterialMovement(
                Guid.NewGuid(), type!.Id, k.On,
                new[] { new InventoryAllocation(k.ItemId, k.GodownId, 4m, StockDirection.Outward, Money.FromRupees(10m)) },
                new[] { new InventoryAllocation(k.ItemId, k.GodownId, 4m, StockDirection.Inward, Money.FromRupees(10m)) }));

            vm.OpenReport(ReportKind.MaterialOutRegister);
            Pump(window);

            var dataRows = vm.Reports!.Rows.Where(r => !r.IsTotal && !r.IsHeader).ToList();
            Assert.NotEmpty(dataRows);
            Assert.All(dataRows, r => Assert.Equal(movement.Id, r.DrillInventoryVoucherId));
        }
        finally { Close(window, dir); }
    }
}
