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
/// 🔴 <b>CENSUS ROWS 4.9–4.16 — THE SURVIVING CLAUSE: "NOT EXERCISED END-TO-END THROUGH THE UI".</b>
///
/// <para><b>What was already closed, and what was not.</b> Listing, drill, <c>Alt+X</c> cancel and <c>Alt+D</c>
/// delete shipped for the pure-stock aggregate in PR #107 (wave 29, track U2); <b>alteration</b> shipped and was
/// mutation-proven for all eight base kinds. What rows <b>4.9, 4.11, 4.12, 4.15 and 4.16</b> still carry is
/// narrower and entirely procedural: their kinds were proven through a <i>shared, type-agnostic</i> code path
/// and a per-kind <i>alteration</i> theory, but the <b>whole operator cycle</b> was exercised end-to-end on
/// <b>ReceiptNote</b> (4.14) and <b>PhysicalStock</b> (4.10) only. 4.10 is the row the census calls "taken
/// furthest", and it is the one row of the eight that promoted.
///
/// <para>"The code path is shared, therefore every kind works" is the exact claim this project has been bitten by
/// before — three slices shipped green and held 34, 42 and 17 defects. The four line shapes go down four
/// different branches (order lines · counted lines · the two-sided Stock Journal · the direction-derived movement
/// note), and the reports, the drill pane, the stock consequence and the audit trail each read a different one of
/// them. So the cycle is run <b>per kind</b>, as a theory, over <b>all eight</b> — the three already-exercised
/// kinds included, because a theory that skips its own controls cannot tell a shared-path failure from a
/// per-kind one.</para></para>
///
/// <para><b>THE CYCLE UNDER TEST, through the REAL <see cref="MainWindow"/> tunnel key handler against a real
/// company on a throwaway <c>.db</c>:</b> Gateway → post through the real entry screen → Reports → Day Book →
/// arrow to the stock row → <b>Enter</b> drills a read-only column that RENDERS the lines → back →
/// <b>Ctrl+Enter</b> opens the alteration pre-filled → re-key → <b>Ctrl+A</b> saves → <b>reload the company from
/// storage and re-read the figure off the reloaded book</b>. Then, on their own fixtures: <b>Alt+X</b> + Y and
/// <b>Alt+D</b> + Y.</para>
///
/// <para>🔴 <b>EVERY ROW HERE MUTATES A POSTED VOUCHER, AND A PAST WAVE PUT A FALSIFIED AUDIT LOG ON MAIN.</b> So
/// the audit trail is not asserted by its verb alone. For each kind:
/// <list type="bullet">
///   <item>the logged <c>BeforeSnapshot</c> is compared <b>byte-for-byte</b> against a snapshot this test took of
///         the voucher itself before the verb ran — a log line that merely exists, or that snapshots the
///         AFTER-state, fails;</item>
///   <item>the <b>after</b>-state is read back off the <b>reloaded</b> book and asserted to differ. The log
///         deliberately carries no after column (<c>VoucherEditLogEntry</c> says so, and says why: the
///         after-state is on the book, and a later entry's before IS the earlier entry's after). So
///         <c>The_edit_log_reconstructs_before_AND_after_over_a_chain_of_alterations</c> runs <b>two</b>
///         successive alterations per kind and proves that chain, which is what "records before and after"
///         actually means in this design;</item>
///   <item>a cancellation leaves the number <b>CONSUMED</b> — measured where an operator meets it, as the number
///         the NEXT voucher of that type is given — and the voucher <b>visible as cancelled</b>, with the
///         CANCELLED badge asserted in the realised visual tree;</item>
///   <item><b>nothing silently renumbers neighbours.</b> Three vouchers are posted and the verb is applied to the
///         MIDDLE one; the other two must come back off the reloaded book with the same ids AND the same
///         numbers.</item>
/// </list></para>
///
/// <para><b>FIDELITY (R7; ruling 14 — the vendor's own published documentation).</b> The grounding for the Day
/// Book carrying this aggregate and for the two lifecycle chords is recorded in full on
/// <c>InventoryVoucherDayBookRouteTests</c> (<i>help.tallysolutions.com</i>, Day Book: the "All Vouchers" view
/// <i>"displays Day Book for all the vouchers, irrespective of the type of voucher"</i>, its "Inventory Entries
/// Only" view covers <i>"Delivery Note, Physical Stock Voucher, and others"</i>, and the same page attests
/// <i>"Press Alt+D to delete"</i> and <i>"Press Alt+X to cancel"</i>); the alteration chord's provenance is on
/// <c>MainWindowViewModel.RequestAlterHighlightedVoucher</c> and on
/// <c>InventoryVoucherAlterationRouteTests</c>. <b>Nothing is re-grounded here and nothing new is claimed as
/// fidelity</b> — this file adds COVERAGE of capabilities already grounded, which is precisely what the five
/// rows' surviving clause asks for. <b>OURS, unverified by design:</b> the prompt wording, the retained number,
/// the CANCELLED badge and the column titles.</para>
/// </summary>
public sealed class InventoryVoucherKindEndToEndTests
{
    // ============================================================ harness (mirrors the two U2 suites)

    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexInvKindE2E_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
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
    /// which silently failed to bind.</summary>
    private static List<string> RenderedText(MainWindow window) =>
        Descendants(window).OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList();

    private sealed record Kit(string CompanyName, string TempDir, Guid ItemId, Guid OtherItemId, Guid GodownId);

    /// <summary>
    /// A company with a "Widget" (Nos) item at 1,000 opening and a second "Assembly" item for the Stock Journal's
    /// destination arm. Both are created ONCE: a journal consuming and producing the same item in the same godown
    /// is a no-op the engine would rightly question, and creating "Assembly" per post would collide on the second
    /// voucher — which is why the neighbour tests below need it here rather than in the post helper.
    /// </summary>
    private static Kit Seed(MainWindowViewModel vm, string tempDir, string name)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);

        var c = vm.Company!;
        var masters = new InventoryService(c);
        var group = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var widget = masters.CreateStockItem("Widget", group.Id, nos.Id);
        var assembly = masters.CreateStockItem("Assembly", group.Id, nos.Id);
        masters.AddOpeningBalance(widget.Id, c.MainLocation!.Id, 1000m, Money.FromRupees(100m));

        return new Kit(name, tempDir, widget.Id, assembly.Id, c.MainLocation!.Id);
    }

    /// <summary>Posts ONE voucher of <paramref name="baseType"/> through the REAL entry screen and its Accept, so
    /// it is genuinely posted and persisted rather than hand-built into the aggregate. A Physical Stock voucher
    /// takes no rate (a count is not a movement); a Stock Journal needs its destination arm.</summary>
    private static InventoryVoucher PostThroughTheScreen(
        MainWindowViewModel vm, Kit k, VoucherBaseType baseType, decimal qty)
    {
        var before = vm.Company!.InventoryVouchers.Select(v => v.Id).ToHashSet();

        vm.OpenInventoryVoucher(baseType);
        Assert.Equal(Screen.InventoryVoucherEntry, vm.CurrentScreen);
        var entry = vm.InventoryVoucherEntry!;

        var line = entry.Lines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.GodownId);
        line.QuantityText = qty.ToString(CultureInfo.InvariantCulture);
        if (!entry.IsPhysicalStock) line.RateText = "50";

        if (entry.IsStockJournal)
        {
            var dest = entry.DestinationLines[0];
            dest.SelectedItem = entry.StockItems.Single(i => i.Id == k.OtherItemId);
            dest.SelectedGodown = entry.Godowns.Single(g => g.Id == k.GodownId);
            dest.QuantityText = qty.ToString(CultureInfo.InvariantCulture);
            dest.RateText = "50";
        }

        Assert.True(entry.Accept(), $"{baseType}: {entry.Message}");
        return vm.Company!.InventoryVouchers.Single(v => !before.Contains(v.Id));
    }

    /// <summary>Opens the Day Book and highlights the row standing for <paramref name="stockVoucherId"/>, resolved
    /// through the PURE-STOCK drill slot — the slot every consumer of this aggregate turns on.</summary>
    private static ReportRow OpenDayBookOnStockRow(MainWindow window, MainWindowViewModel vm, Guid stockVoucherId)
    {
        vm.OpenReport(ReportKind.DayBook);
        Pump(window);
        var row = vm.Reports!.Rows.SingleOrDefault(r => r.DrillInventoryVoucherId == stockVoucherId);
        Assert.NotNull(row);
        vm.Reports!.SelectedRow = row;
        Pump(window);
        return row!;
    }

    private static Company Reopen(Kit k)
    {
        var storage = new CompanyStorage(k.TempDir);
        return storage.Load(storage.ListCompanies().Single(e => e.Name == k.CompanyName));
    }

    /// <summary>The quantity the PRIMARY (keyed) grid of a voucher of any of the eight kinds holds — the one
    /// figure whose change proves an alteration reached the book. Four kinds, four collections.</summary>
    private static decimal FirstKeyedQuantity(InventoryVoucher v) =>
        v.OrderLines.Count > 0 ? v.OrderLines[0].Quantity
        : v.PhysicalLines.Count > 0 ? v.PhysicalLines[0].CountedQuantity
        : v.Allocations[0].Quantity;

    private static decimal OnHand(Company c, Kit k) =>
        new InventoryLedger(c).OnHand(k.ItemId, k.GodownId, c.FinancialYearStart.AddYears(1));

    /// <summary>Whether posting this kind MOVES stock. Sales/Purchase Orders are commitments and move nothing
    /// (phase3 DP-5); the other six do. Asserted rather than assumed, so a "stock went back" check on a kind that
    /// never moved any cannot pass vacuously.</summary>
    private static bool MovesStock(VoucherBaseType baseType) =>
        baseType is not (VoucherBaseType.SalesOrder or VoucherBaseType.PurchaseOrder);

    /// <summary>The eight base kinds of census rows 4.9–4.16, with the row each one answers to. The five that
    /// carry the "not exercised end-to-end through the UI" clause are 4.9, 4.11, 4.12, 4.15 and 4.16; 4.10, 4.13
    /// and 4.14 are the already-exercised controls and are included deliberately.</summary>
    public static TheoryData<VoucherBaseType> AllEightKinds => new()
    {
        VoucherBaseType.StockJournal,    // 4.9  — carries the clause
        VoucherBaseType.PhysicalStock,   // 4.10 — control (already COMPLETE)
        VoucherBaseType.SalesOrder,      // 4.11 — carries the clause
        VoucherBaseType.PurchaseOrder,   // 4.12 — carries the clause
        VoucherBaseType.DeliveryNote,    // 4.13 — control (held on T1-8)
        VoucherBaseType.ReceiptNote,     // 4.14 — control (the kind U2 exercised)
        VoucherBaseType.RejectionOut,    // 4.15 — carries the clause
        VoucherBaseType.RejectionIn,     // 4.16 — carries the clause
    };

    // ============================================================ (1) the whole cycle, per kind

    /// <summary>
    /// 🔴 <b>THE DRIVING THEORY FOR THE FIVE ROWS' SURVIVING CLAUSE.</b> One voucher of each kind walks the entire
    /// operator cycle through the real key handler, and every figure is read back off the <b>reloaded</b> company:
    /// post → the Day Book carries the row in its own drill slot → <b>Enter</b> drills a column that RENDERS the
    /// stock item (not an empty template) → back → <b>Ctrl+Enter</b> opens the alteration pre-filled from the
    /// POSTED voucher → re-key 6 → 2 → <b>Ctrl+A</b> → reload → the SAME voucher (same id, same number, same
    /// count) carries 2.
    ///
    /// <para><b>The figures are chosen so the plausible wrong implementations give different answers.</b> Posting
    /// the amendment as a SECOND voucher changes the count; losing the original's identity changes the number;
    /// failing to apply the new figures leaves 6; and reading the screen instead of the book would pass even if
    /// nothing were ever written, which is why the last read goes through <c>Reopen</c>.</para>
    ///
    /// <para><b>Mutation:</b> making <c>InventoryVoucherEntryViewModel.ForAlter</c> refuse every inventory voucher
    /// reddens every arm at the <c>IsAltering</c> assertion by name.</para>
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(AllEightKinds))]
    public void Every_kind_walks_the_whole_UI_cycle_end_to_end(VoucherBaseType baseType)
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, dir, $"Kind Cycle {baseType} Co");
            var posted = PostThroughTheScreen(vm, k, baseType, qty: 6m);
            var postedId = posted.Id;
            var postedNumber = posted.Number;
            var countAfterPost = vm.Company!.InventoryVouchers.Count;

            Assert.True(postedNumber > 0, $"{baseType} posted unnumbered, so every later identity check is moot.");
            Assert.Equal(6m, FirstKeyedQuantity(posted));

            // ---- (a) it is IN the Day Book, in the pure-stock slot, and drillable.
            var row = OpenDayBookOnStockRow(window, vm, postedId);
            Assert.True(row.CanDrill, "A Day Book row nobody can open is only half a listing.");
            Assert.Equal(Guid.Empty, row.DrillVoucherId);       // the accounting slot stays empty
            Assert.False(row.IsCancelled);

            // ---- (b) bare Enter drills, and the column RENDERS the voucher rather than an empty template.
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.InventoryVoucherDetail, vm.CurrentScreen);
            Assert.Equal(postedId, vm.InventoryVoucherDetail!.VoucherId);
            Assert.Contains(RenderedText(window), s => s.Contains("Widget", StringComparison.Ordinal));

            // ---- (c) Ctrl+Enter opens the ALTERATION, pre-filled from the POSTED voucher.
            vm.Back();
            Pump(window);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            vm.Reports!.SelectedRow = vm.Reports!.Rows.Single(r => r.DrillInventoryVoucherId == postedId);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
            Pump(window);

            Assert.Equal(Screen.InventoryVoucherEntry, vm.CurrentScreen);
            var entry = vm.InventoryVoucherEntry!;
            Assert.True(entry.IsAltering,
                $"{baseType} opened as a NEW entry — Ctrl+A would post a second voucher.");
            Assert.Equal(postedId, entry.AlteringVoucherId);
            Assert.Equal(postedNumber, entry.VoucherNumber);
            Assert.Equal(6m, entry.Lines[0].ParsedQuantity);
            Assert.Contains(RenderedText(window), s => s.Contains("Alteration", StringComparison.Ordinal));

            // ---- (d) re-key and save with Ctrl+A. A Stock Journal must stay balanced, so both arms move.
            entry.Lines[0].QuantityText = "2";
            if (baseType == VoucherBaseType.StockJournal) entry.DestinationLines[0].QuantityText = "2";
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);

            // A refused save leaves the altering screen up with its reason on Message — surfaced so a failure
            // names the branch that refused rather than only showing a quantity that did not move.
            Assert.False(
                vm.CurrentScreen == Screen.InventoryVoucherEntry
                    && vm.InventoryVoucherEntry is { IsAltering: true },
                $"{baseType}: the alteration was not saved — {vm.InventoryVoucherEntry?.Message}");

            // ---- (e) RELOAD AND RE-READ. Everything above could pass against an in-memory aggregate that never
            // reached the .db; this is the only assertion that cannot.
            var reopened = Reopen(k);
            Assert.Equal(countAfterPost, reopened.InventoryVouchers.Count);
            var amended = reopened.FindInventoryVoucher(postedId);
            Assert.NotNull(amended);
            Assert.Equal(postedNumber, amended!.Number);
            Assert.Equal(2m, FirstKeyedQuantity(amended));
            Assert.Contains(reopened.VoucherEditLog,
                e => e.VoucherId == postedId && e.Verb == VoucherEditVerb.Alter);
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (2) Alt+X, per kind

    /// <summary>
    /// 🔴 <b>CANCELLATION, PER KIND — AND THE THREE CLAIMS THE BRIEF SINGLES OUT.</b> <c>Alt+X</c> then <c>Y</c>
    /// from the Day Book row, and then, all off the <b>reloaded</b> book:
    /// <list type="number">
    ///   <item><b>the number is CONSUMED.</b> Measured where an operator meets it rather than on a service call:
    ///   the NEXT voucher of the same type posted through the real screen gets <b>2</b>. A cancelled voucher that
    ///   released its number would hand 1 out twice, and the two documents would be indistinguishable in every
    ///   report.</item>
    ///   <item><b>the voucher is VISIBLE AS CANCELLED</b> — still listed, still drillable, <c>IsCancelled</c>, and
    ///   the CANCELLED badge asserted in the REALISED visual tree. "Flagged in the model" is not visibility; a
    ///   greyed row with no badge is how an operator re-pays a cancelled bill.</item>
    ///   <item><b>the audit entry is REAL.</b> Its <c>BeforeSnapshot</c> is compared byte-for-byte with a snapshot
    ///   this test took of the voucher before the chord — so an entry that snapshotted the AFTER-state (which
    ///   would read as "it was always cancelled") fails, and so does a fabricated one.</item>
    /// </list>
    ///
    /// <para>And the stock consequence: on-hand returns to exactly what it was before the post. For the six kinds
    /// that move stock the post is asserted to have MOVED it first, so this cannot pass vacuously on a kind where
    /// nothing ever moved.</para>
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(AllEightKinds))]
    public void Every_kind_cancels_keeping_its_number_consumed_and_shows_as_cancelled(VoucherBaseType baseType)
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, dir, $"Kind Cancel {baseType} Co");
            var onHandBeforePost = OnHand(vm.Company!, k);

            var posted = PostThroughTheScreen(vm, k, baseType, qty: 6m);
            var postedId = posted.Id;
            var postedNumber = posted.Number;
            Assert.Equal(1, postedNumber);
            var onHandAfterPost = OnHand(vm.Company!, k);
            if (MovesStock(baseType))
                Assert.NotEqual(onHandBeforePost, onHandAfterPost);
            else
                Assert.Equal(onHandBeforePost, onHandAfterPost);

            // The before-state, taken by this test from the voucher itself, to compare the log against.
            var snapshotBeforeCancel = VoucherSnapshot.Of(posted);

            OpenDayBookOnStockRow(window, vm, postedId);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            Assert.True(vm.IsAcceptPromptOpen, $"{baseType}: Alt+X raised no confirmation.");
            Assert.Contains("(Y/N)", vm.AcceptPromptText, StringComparison.Ordinal);
            Assert.False(vm.Company!.FindInventoryVoucher(postedId)!.Cancelled);  // nothing yet

            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);
            Assert.False(vm.IsAcceptPromptOpen);

            // ---- the live report rebuilt itself where the operator is standing: still listed, flagged.
            var liveRow = vm.Reports!.Rows.Single(r => r.DrillInventoryVoucherId == postedId);
            Assert.True(liveRow.IsCancelled);

            // ---- (2) VISIBLE as cancelled, in the realised tree.
            vm.Reports!.SelectedRow = liveRow;
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.InventoryVoucherDetail, vm.CurrentScreen);
            Assert.True(vm.InventoryVoucherDetail!.IsCancelled);
            Assert.Contains(
                Descendants(window).OfType<TextBlock>().Where(t => t.IsEffectivelyVisible),
                t => (t.Text ?? string.Empty).Contains("CANCELLED", StringComparison.Ordinal));
            vm.Back();
            Pump(window);

            // ---- (1) the number stays CONSUMED: the NEXT voucher of this type gets 2, through the real screen.
            var next = PostThroughTheScreen(vm, k, baseType, qty: 3m);
            Assert.Equal(2, next.Number);

            // ---- off the reloaded book: flagged, same number, stock back where it started.
            var reopened = Reopen(k);
            var cancelled = reopened.FindInventoryVoucher(postedId);
            Assert.NotNull(cancelled);
            Assert.True(cancelled!.Cancelled, $"{baseType}: the cancellation did not survive the reload.");
            Assert.Equal(postedNumber, cancelled.Number);

            // ---- (3) the audit entry, byte-for-byte against the pre-chord state.
            var logged = Assert.Single(reopened.VoucherEditLog.Where(e => e.VoucherId == postedId));
            Assert.Equal(VoucherEditVerb.Cancel, logged.Verb);
            Assert.Equal(snapshotBeforeCancel, logged.BeforeSnapshot);
            Assert.NotEqual(VoucherSnapshot.Of(cancelled), logged.BeforeSnapshot);  // …and the AFTER differs
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (3) Alt+D, per kind

    /// <summary>
    /// <b>DELETION, PER KIND.</b> <c>Alt+D</c> then <c>Y</c> from the Day Book row: the voucher leaves the book,
    /// its row leaves the report, the stock goes back, and the audit entry survives the reload carrying the
    /// deleted voucher's own pre-delete snapshot — the one entry whose subject no longer exists, which is why
    /// <c>VoucherEditLogEntry.VoucherId</c> is deliberately not a foreign key.
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(AllEightKinds))]
    public void Every_kind_deletes_from_the_Day_Book_and_leaves_a_real_audit_entry(VoucherBaseType baseType)
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, dir, $"Kind Delete {baseType} Co");
            var onHandBeforePost = OnHand(vm.Company!, k);

            var posted = PostThroughTheScreen(vm, k, baseType, qty: 6m);
            var postedId = posted.Id;
            var snapshotBeforeDelete = VoucherSnapshot.Of(posted);
            if (MovesStock(baseType)) Assert.NotEqual(onHandBeforePost, OnHand(vm.Company!, k));

            OpenDayBookOnStockRow(window, vm, postedId);
            window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Alt);
            Pump(window);
            Assert.True(vm.IsAcceptPromptOpen, $"{baseType}: Alt+D raised no confirmation.");
            Assert.Contains("Delete", vm.AcceptPromptText, StringComparison.Ordinal);
            Assert.NotNull(vm.Company!.FindInventoryVoucher(postedId));           // nothing yet

            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);

            Assert.Null(vm.Company!.FindInventoryVoucher(postedId));
            Assert.DoesNotContain(vm.Reports!.Rows, r => r.DrillInventoryVoucherId == postedId);

            var reopened = Reopen(k);
            Assert.Null(reopened.FindInventoryVoucher(postedId));
            Assert.Equal(onHandBeforePost, OnHand(reopened, k));

            var logged = Assert.Single(reopened.VoucherEditLog.Where(e => e.VoucherId == postedId));
            Assert.Equal(VoucherEditVerb.Delete, logged.Verb);
            Assert.Equal(snapshotBeforeDelete, logged.BeforeSnapshot);
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (4) neighbours are never renumbered

    /// <summary>
    /// 🔴 <b>NOTHING SILENTLY RENUMBERS NEIGHBOURS.</b> Three vouchers of one kind are posted (1, 2, 3) and the
    /// verbs are applied to the <b>MIDDLE</b> one — altered, then cancelled, then deleted. After each, the OTHER
    /// TWO must come back off the reloaded book with the same ids <b>and</b> the same numbers.
    ///
    /// <para><b>Why the middle one.</b> A renumbering defect that walks the list and re-stamps <c>max + 1</c>, or
    /// that closes the gap a deletion leaves, shows up only on a voucher that has a neighbour on BOTH sides. A
    /// one-voucher fixture cannot see it, and a two-voucher fixture only sees half of it. This is the invariant an
    /// operator's document numbers hang on: a renumbered neighbour silently re-points every bill reference,
    /// tracking number and bills-pending row that named it.</para>
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(AllEightKinds))]
    public void No_verb_on_one_voucher_renumbers_its_neighbours(VoucherBaseType baseType)
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, dir, $"Kind Neighbours {baseType} Co");
            var first = PostThroughTheScreen(vm, k, baseType, qty: 6m);
            var middle = PostThroughTheScreen(vm, k, baseType, qty: 6m);
            var last = PostThroughTheScreen(vm, k, baseType, qty: 6m);

            Assert.Equal(new[] { 1, 2, 3 }, new[] { first.Number, middle.Number, last.Number });
            var firstId = first.Id;
            var lastId = last.Id;
            var middleId = middle.Id;

            void AssertNeighboursUntouched(string after)
            {
                var c = Reopen(k);
                var f = c.FindInventoryVoucher(firstId);
                var l = c.FindInventoryVoucher(lastId);
                Assert.NotNull(f);
                Assert.NotNull(l);
                Assert.Equal(1, f!.Number);
                Assert.Equal(3, l!.Number);
                Assert.Equal(6m, FirstKeyedQuantity(f));
                Assert.Equal(6m, FirstKeyedQuantity(l));
                Assert.False(f.Cancelled, $"{baseType}: {after} cancelled a NEIGHBOUR.");
                Assert.False(l.Cancelled, $"{baseType}: {after} cancelled a NEIGHBOUR.");
            }

            // ---- alter the middle one.
            OpenDayBookOnStockRow(window, vm, middleId);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
            Pump(window);
            var entry = vm.InventoryVoucherEntry!;
            Assert.True(entry.IsAltering, $"{baseType}: Ctrl+Enter did not open the alteration.");
            entry.Lines[0].QuantityText = "2";
            if (baseType == VoucherBaseType.StockJournal) entry.DestinationLines[0].QuantityText = "2";
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);
            Assert.Equal(2m, FirstKeyedQuantity(Reopen(k).FindInventoryVoucher(middleId)!));
            Assert.Equal(2, Reopen(k).FindInventoryVoucher(middleId)!.Number);
            AssertNeighboursUntouched("the alteration");

            // ---- cancel the middle one.
            OpenDayBookOnStockRow(window, vm, middleId);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);
            Assert.True(Reopen(k).FindInventoryVoucher(middleId)!.Cancelled);
            Assert.Equal(2, Reopen(k).FindInventoryVoucher(middleId)!.Number);
            AssertNeighboursUntouched("the cancellation");

            // ---- delete the middle one. The gap it leaves must stay a gap.
            OpenDayBookOnStockRow(window, vm, middleId);
            window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Alt);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);
            Assert.Null(Reopen(k).FindInventoryVoucher(middleId));
            AssertNeighboursUntouched("the deletion");
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (5) the edit log's before AND after

    /// <summary>
    /// 🔴 <b>"THE EDIT LOG RECORDS BEFORE AND AFTER" — PROVED THE ONLY WAY THIS DESIGN CAN PROVE IT.</b>
    /// <c>VoucherEditLogEntry</c> deliberately carries <b>no after column</b>, and says why in as many words: the
    /// after-state of an alteration is ON THE BOOK, and where a later entry exists for the same voucher its own
    /// <c>BeforeSnapshot</c> IS the earlier entry's after-state — so the chain
    /// <c>entry₁.Before → entry₂.Before → … → the live voucher</c> reconstructs every intermediate state.
    ///
    /// <para>That claim is an assertion about a CHAIN, so it needs two edits to test, and until now no test made
    /// a second one. Here, per kind: post at 6, alter to 4, alter to 2, then off the <b>reloaded</b> book —</para>
    /// <list type="bullet">
    ///   <item><c>entry₁.Before</c> == this test's own snapshot of the 6-state;</item>
    ///   <item><c>entry₂.Before</c> == this test's own snapshot of the 4-state — i.e. entry₁'s AFTER;</item>
    ///   <item>the live voucher carries 2 — entry₂'s after;</item>
    ///   <item>and the three states are pairwise DIFFERENT, so a log that snapshotted the same thing twice (or
    ///   the after-state both times) cannot pass.</item>
    /// </list>
    ///
    /// <para>The order is asserted too: <c>RecordedAt</c> must be non-decreasing and the entries must appear in
    /// the order the edits happened, because the chain is only reconstructible if the log is append-only in
    /// sequence.</para>
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(AllEightKinds))]
    public void The_edit_log_reconstructs_before_AND_after_over_a_chain_of_alterations(VoucherBaseType baseType)
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, dir, $"Kind Log Chain {baseType} Co");
            var posted = PostThroughTheScreen(vm, k, baseType, qty: 6m);
            var id = posted.Id;
            var number = posted.Number;
            var stateAt6 = VoucherSnapshot.Of(posted);

            Alter(window, vm, id, baseType, "4");
            var stateAt4 = VoucherSnapshot.Of(vm.Company!.FindInventoryVoucher(id)!);

            Alter(window, vm, id, baseType, "2");

            var reopened = Reopen(k);
            var live = reopened.FindInventoryVoucher(id);
            Assert.NotNull(live);
            Assert.Equal(number, live!.Number);
            Assert.Equal(2m, FirstKeyedQuantity(live));
            var stateAt2 = VoucherSnapshot.Of(live);

            // The three states are genuinely different, or the chain assertions below would be tautologies.
            Assert.NotEqual(stateAt6, stateAt4);
            Assert.NotEqual(stateAt4, stateAt2);
            Assert.NotEqual(stateAt6, stateAt2);

            var entries = reopened.VoucherEditLog
                .Where(e => e.VoucherId == id && e.Verb == VoucherEditVerb.Alter)
                .ToList();
            Assert.Equal(2, entries.Count);

            Assert.Equal(stateAt6, entries[0].BeforeSnapshot);   // before₁
            Assert.Equal(stateAt4, entries[1].BeforeSnapshot);   // before₂ == after₁
            Assert.True(entries[0].RecordedAt <= entries[1].RecordedAt,
                $"{baseType}: the edit log is out of sequence, so the chain cannot be walked.");
            Assert.NotEqual(entries[0].Id, entries[1].Id);
        }
        finally { Close(window, dir); }
    }

    /// <summary>Opens the posted voucher's alteration from the Day Book by <c>Ctrl+Enter</c>, re-keys the primary
    /// quantity to <paramref name="quantity"/> and saves with <c>Ctrl+A</c> — all through the real key handler.</summary>
    private static void Alter(
        MainWindow window, MainWindowViewModel vm, Guid voucherId, VoucherBaseType baseType, string quantity)
    {
        OpenDayBookOnStockRow(window, vm, voucherId);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
        Pump(window);

        var entry = vm.InventoryVoucherEntry!;
        Assert.True(entry.IsAltering, $"{baseType}: Ctrl+Enter did not open the alteration.");
        entry.Lines[0].QuantityText = quantity;
        if (baseType == VoucherBaseType.StockJournal) entry.DestinationLines[0].QuantityText = quantity;

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Pump(window);
        Assert.False(
            vm.CurrentScreen == Screen.InventoryVoucherEntry && vm.InventoryVoucherEntry is { IsAltering: true },
            $"{baseType}: the alteration to {quantity} was not saved — {vm.InventoryVoucherEntry?.Message}");
    }
}
