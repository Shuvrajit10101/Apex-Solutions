using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
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
/// 🔴 <b>CENSUS ROWS 5.2 / 5.7 / 5.8 — THE DAY BOOK'S THREE Ctrl+J EXCEPTION REGISTERS.</b>
///
/// <para><b>The defect.</b> <c>Optional</c>, <c>Cancelled</c> and <c>PostDated</c> were all already settable and
/// all already persisted — and all three were <b>invisible</b>. The only surface that listed a flagged voucher
/// was the Day Book itself, mixed in with every ordinary voucher of the same day. An operator who marked a
/// voucher Optional and moved on had no screen that would ever tell them it was still Optional, i.e. still
/// outside the books. A flag with no register is a liability the book cannot show you.</para>
///
/// <para><b>Fidelity (R7 / ruling 14).</b> The vendor names both the chord and the whole set: "The Exception
/// Reports available in the Day Book in TallyPrime are of <b>Optional Vouchers</b>, <b>Cancelled Vouchers</b>,
/// and <b>Post-Dated Vouchers</b>", reached with <b>Ctrl+J (Exception Reports)</b>
/// (<c>help.tallysolutions.com/tally-prime/accounting-financial-reports/day-book-tally/</c>). Exactly three rows
/// are offered, and <see cref="Ctrl_J_offers_exactly_the_vendors_three_exception_reports"/> holds it to that —
/// inventing a fourth would be as much a fidelity break as omitting one.</para>
///
/// <para>Every assertion drives the REAL chord through <see cref="MainWindow"/>'s tunnel handler and then
/// activates the picker row with Enter, because reach is the whole substance of these rows.</para>
/// </summary>
public sealed class ExceptionReportsRegisterTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "ApexExcRep_" + Guid.NewGuid().ToString("N"));

    private static readonly DateOnly Day = new(2020, 4, 15);

    /// <summary>
    /// Robert plus four hand-built ACCOUNTING vouchers on one day — one ORDINARY, one OPTIONAL, one CANCELLED and
    /// one POST-DATED — and, on the same day, one <b>PURE-STOCK</b> voucher that is POST-DATED (905) and one that
    /// is ordinary (906). Four distinct accounting states is what lets each register be asserted as a filter; a
    /// fixture with only the flagged voucher would pass a register that listed everything.
    ///
    /// <para>🔴 <b>WHY THE STOCK VOUCHERS ARE HERE (review finding F7).</b> The registers claim to read BOTH
    /// aggregates — <c>ExceptionVouchers.Matches</c> branches on <c>DayBookRow.IsInventory</c> and picks
    /// <c>FindInventoryVoucher</c> over <c>FindVoucher</c> — and NOTHING exercised the inventory branch: the
    /// fixture posted accounting vouchers only, and <see cref="ListedNumbers"/> resolved through the accounting
    /// slot alone, so a listed stock row (which carries <see cref="Guid.Empty"/> there) would have been silently
    /// DROPPED rather than caught. That two-aggregate discipline is the defect class census rows 4.9–4.16 exist
    /// to record. The Post-Dated register is the one of the three that can carry it: <c>InventoryVoucher</c> has
    /// <c>PostDated</c> and <c>Cancelled</c> but no <c>Optional</c> member at all (see
    /// <c>ExceptionVouchers.OptionalScopeNote</c>), and census 5.2 records that no stock voucher can be cancelled
    /// through any route in this product — so an inventory row on the Cancelled register would be a fixture
    /// asserting something the product cannot produce.</para>
    /// </summary>
    private (MainWindow Window, MainWindowViewModel Vm) NewWindow()
    {
        var vm = new MainWindowViewModel(new CompanyStorage(_tempDir));
        vm.LoadRobertDemo();

        var c = vm.Company!;
        var cash = c.FindLedgerByName("Cash")!;
        var rent = c.FindLedgerByName("Office Rent")!;
        var typeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Payment).Id;
        var svc = new LedgerService(c);

        Voucher Make(int number, bool cancelled = false, bool optional = false, bool postDated = false)
            => new(
                Guid.NewGuid(), typeId, Day,
                new[]
                {
                    new EntryLine(rent.Id, new Money(500m), DrCr.Debit),
                    new EntryLine(cash.Id, new Money(500m), DrCr.Credit),
                },
                number: number, narration: null, partyId: null,
                cancelled: cancelled, optional: optional, postDated: postDated);

        svc.Post(Make(901));                        // ordinary — must appear in NO register
        svc.Post(Make(902, optional: true));
        svc.Post(Make(903, cancelled: true));
        svc.Post(Make(904, postDated: true));

        SeedStockVouchers(c);

        vm.ShowGateway();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    /// <summary>One post-dated and one ordinary Receipt Note on the same day, so the Post-Dated register has a
    /// pure-stock row to find AND a pure-stock row it must leave out.</summary>
    private static void SeedStockVouchers(Company c)
    {
        var masters = new InventoryService(c);
        var group = masters.CreateStockGroup("Exception Goods");
        var unit = masters.CreateSimpleUnit("ExcNos", "Exception Numbers");
        var item = masters.CreateStockItem("Exception Widget", group.Id, unit.Id);
        var godown = c.MainLocation
            ?? throw new InvalidOperationException("the fixture company has no main location to stock");
        var receiptNote = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.ReceiptNote);
        var posting = new InventoryPostingService(c);

        InventoryVoucher Stock(int number, bool postDated) => new(
            Guid.NewGuid(), receiptNote.Id, Day,
            new[] { new InventoryAllocation(item.Id, godown.Id, 5m, StockDirection.Inward, new Money(100m)) },
            number: number, narration: null, partyId: null, cancelled: false, postDated: postDated);

        posting.Post(Stock(905, postDated: true));
        posting.Post(Stock(906, postDated: false));   // ordinary stock — must appear in NO register
    }

    private static void Key(MainWindow window, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, mods);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Opens the Day Book as a live report page, the surface Ctrl+J is bound on.</summary>
    private static void OpenDayBook(MainWindowViewModel vm)
    {
        vm.OpenReport(ReportKind.DayBook);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsDayBookReport, "the Day Book did not open; every assertion below would be vacuous");
    }

    /// <summary>Arrow down the open picker to <paramref name="label"/> and activate it with Enter.</summary>
    private static void ArrowToAndEnter(MainWindow window, MainWindowViewModel vm, string label)
    {
        for (var i = 0; i < 10; i++)
        {
            if (vm.Columns[vm.ActiveColumnIndex].Selected?.Label == label)
            {
                Key(window, PhysicalKey.Enter);
                return;
            }
            Key(window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"'{label}' was not reachable by arrow navigation on the Exception Reports picker.");
    }

    /// <summary>
    /// The voucher numbers a register actually lists — resolved through BOTH drill slots (review finding F7).
    ///
    /// <para>🔴 It used to read <c>DrillVoucherId</c> alone. A pure-stock row carries <see cref="Guid.Empty"/>
    /// there and its id in <c>DrillInventoryVoucherId</c>, so a listed stock voucher was silently dropped from
    /// this array instead of being compared — which is how a register could claim to read both aggregates while
    /// no assertion ever saw one. A row that carries NEITHER id (the empty-state note, the Optional scope note)
    /// is not a voucher row and is correctly skipped; a row that carries both would be a bug and throws here.</para>
    /// </summary>
    private static int[] ListedNumbers(MainWindowViewModel vm)
    {
        var numbers = new List<int>();
        foreach (var r in vm.Reports!.Rows)
        {
            var accounting = r.DrillVoucherId != Guid.Empty;
            var inventory = r.DrillInventoryVoucherId != Guid.Empty;
            Assert.False(accounting && inventory,
                "a report row carries an id in BOTH drill slots — the two aggregates share one id space and a " +
                "row must say which one it means (see ReportRow.DrillInventoryVoucherId).");

            if (accounting)
                numbers.Add(vm.Company!.FindVoucher(r.DrillVoucherId)!.Number);
            else if (inventory)
                numbers.Add(vm.Company!.FindInventoryVoucher(r.DrillInventoryVoucherId)!.Number);
        }
        numbers.Sort();
        return numbers.ToArray();
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// 🔴 <b>THE SET IS THE VENDOR'S, EXACTLY.</b> Three rows, named as the vendor names them. This fails both
    /// ways — a missing register and an invented fourth one — which is the point.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_J_offers_exactly_the_vendors_three_exception_reports()
    {
        var (window, vm) = NewWindow();
        try
        {
            OpenDayBook(vm);
            Key(window, PhysicalKey.J, RawInputModifiers.Control);

            Assert.Equal(Screen.ExceptionReportsPicker, vm.CurrentScreen);
            var labels = vm.Columns[^1].Items.Where(i => i.IsSelectable).Select(i => i.Label).ToArray();
            Assert.Equal(
                new[] { "Optional Vouchers", "Cancelled Vouchers", "Post-Dated Vouchers" },
                labels);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Each register lists the vouchers carrying its OWN flag and nothing else. The fixture puts one ordinary,
    /// one optional, one cancelled and one post-dated ACCOUNTING voucher plus one post-dated and one ordinary
    /// PURE-STOCK voucher on the same day, so a register that forgot to filter — or filtered on the wrong flag,
    /// or read only one of the two aggregates — shows the wrong numbers rather than merely the wrong count.
    ///
    /// <para><b>Post-Dated expects TWO numbers, one from each aggregate</b> (finding F7): 904 is the accounting
    /// voucher, 905 the Receipt Note. A register that resolved every row through <c>Company.FindVoucher</c> would
    /// list only 904; one that listed every stock row regardless of the flag would also pick up 906.</para>
    /// </summary>
    [AvaloniaTheory]
    [InlineData("Optional Vouchers", new[] { 902 })]
    [InlineData("Cancelled Vouchers", new[] { 903 })]
    [InlineData("Post-Dated Vouchers", new[] { 904, 905 })]
    public void Each_register_lists_only_the_vouchers_carrying_its_own_flag(string label, int[] expected)
    {
        var (window, vm) = NewWindow();
        try
        {
            OpenDayBook(vm);
            Key(window, PhysicalKey.J, RawInputModifiers.Control);
            ArrowToAndEnter(window, vm, label);

            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.Equal(label, vm.Reports!.Title);
            Assert.Equal(expected, ListedNumbers(vm));
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>REVIEW FINDING F7, STATED AS ITS OWN ASSERTION.</b> The claim that the registers read both
    /// aggregates is worth a test that names the aggregate rather than only a number: the post-dated Receipt Note
    /// must appear on the Post-Dated register carrying its id in the <b>PURE-STOCK</b> drill slot, with the
    /// accounting slot empty. A row that put a stock id in the accounting slot would drill to nothing (six shell
    /// routes hand that slot straight to <c>Company.FindVoucher</c>), which is the dead-key defect
    /// <c>ReportRow.DrillInventoryVoucherId</c>'s own remarks record.
    /// </summary>
    [AvaloniaFact]
    public void The_post_dated_register_carries_a_stock_voucher_in_the_pure_stock_drill_slot()
    {
        var (window, vm) = NewWindow();
        try
        {
            OpenDayBook(vm);
            Key(window, PhysicalKey.J, RawInputModifiers.Control);
            ArrowToAndEnter(window, vm, "Post-Dated Vouchers");

            var stockRows = vm.Reports!.Rows.Where(r => r.DrillInventoryVoucherId != Guid.Empty).ToArray();
            var stockRow = Assert.Single(stockRows);
            Assert.Equal(Guid.Empty, stockRow.DrillVoucherId);

            var stock = vm.Company!.FindInventoryVoucher(stockRow.DrillInventoryVoucherId)!;
            Assert.Equal(905, stock.Number);
            Assert.True(stock.PostDated, "the register listed a stock voucher that is not post-dated.");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The register's rows carry the Day Book's drill ids, so Enter opens the voucher exactly as it does on the
    /// book. Without this a register would be a dead-end list — the shape census 11.10 records as "a shipped
    /// report nobody can reach past".
    /// </summary>
    [AvaloniaFact]
    public void Enter_on_a_register_row_opens_that_vouchers_detail()
    {
        var (window, vm) = NewWindow();
        try
        {
            OpenDayBook(vm);
            Key(window, PhysicalKey.J, RawInputModifiers.Control);
            ArrowToAndEnter(window, vm, "Cancelled Vouchers");

            var row = vm.Reports!.Rows.First(r => r.DrillVoucherId != Guid.Empty);
            var voucherId = row.DrillVoucherId;
            vm.Reports.SelectedRow = row;
            Key(window, PhysicalKey.Enter);

            Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen);
            Assert.NotNull(vm.VoucherDetail);
            Assert.Equal(903, vm.Company!.FindVoucher(voucherId)!.Number);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE CHORD MUST NOT BE STOLEN FROM THE SCREEN THAT ALREADY OWNED IT.</b> Ctrl+J was already bound on
    /// the Chart of Accounts (census 2.13, "Show Unused"), and the new arm is a SECOND arm on the same chord. The
    /// two contexts are disjoint by construction, and this asserts that rather than assuming it: on the Chart of
    /// Accounts, Ctrl+J must still open the old panel and must NOT open the Day Book picker.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_J_still_opens_the_chart_of_accounts_exception_panel()
    {
        var (window, vm) = NewWindow();
        try
        {
            vm.ShowChartOfAccounts();
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.IsChartOfAccountsScreen, "the Chart of Accounts did not open; this would assert nothing");

            Key(window, PhysicalKey.J, RawInputModifiers.Control);

            Assert.Equal(Screen.ExceptionReports, vm.CurrentScreen);
            Assert.NotNull(vm.ExceptionReports);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>EXACTLY ONE Ctrl+J BADGE, AND IT FIRES THE DOOR ITS CONTEXT USES.</b>
    ///
    /// <para>This test earned its place: the first cut of this slice added the Day Book badge as its own
    /// <c>ButtonBar.Add</c> block while census 2.13's Chart-of-Accounts badge was still being added
    /// unconditionally below it — so on the Day Book the bar carried <b>two</b> Ctrl+J rows. The shell's
    /// <c>Fire()</c> and hint lookup both take the FIRST key match, which is precisely the shadowing trap
    /// <c>BuildButtonBar</c>'s own comments record for Alt+I and Alt+A. The <see cref="Assert.Single{T}"/> clause
    /// below is what caught it, and it is why the count — not merely the presence — is asserted.</para>
    ///
    /// <para>The row is always present and context-sensitive: ENABLED on the Day Book (where it opens the three
    /// registers) and DIMMED on a report that is not the Day Book, per this shell's IV-31 rule that an enabled
    /// badge firing nothing is a defect.</para>
    /// </summary>
    [AvaloniaFact]
    public void Exactly_one_Ctrl_J_badge_is_emitted_and_it_is_enabled_only_where_the_chord_bites()
    {
        var (window, vm) = NewWindow();
        try
        {
            OpenDayBook(vm);
            var onDayBook = Assert.Single(vm.ButtonBar.Where(b => b.Key == "Ctrl+J"));
            Assert.Equal("Exception Reports", onDayBook.Caption);
            Assert.True(onDayBook.Enabled, "the Ctrl+J badge is dimmed on the Day Book, where the chord bites.");

            vm.OpenReport(ReportKind.TrialBalance);
            Dispatcher.UIThread.RunJobs();
            var onTrialBalance = Assert.Single(vm.ButtonBar.Where(b => b.Key == "Ctrl+J"));
            Assert.False(onTrialBalance.Enabled,
                "the Ctrl+J badge is enabled on the Trial Balance, where the chord fires nothing (defect IV-31).");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Escape pops the picker and lands back on the live Day Book — the picker is appended BESIDE the report
    /// (the Alt+A shape), not in place of it, so the operator never loses the book they came from.
    ///
    /// <para>🔴 <b>REVIEW FINDING F4 — THIS TEST WAS VACUOUS AND IS THE REASON THE TWO "BEFORE" CLAUSES ARE
    /// HERE.</b> It used to press Escape and then assert <c>IsDayBookReport</c> and <c>Reports.Kind ==
    /// DayBook</c>. Both of those ALREADY HOLD WHILE THE PICKER IS OPEN — the picker leaves <see cref="Reports"/>
    /// bound beneath it, which is the whole design — so the test would have passed with Escape wired to nothing
    /// at all. What actually distinguishes before from after is the COLUMN and the SCREEN: the picker column is
    /// gone and <c>CurrentScreen</c> is back to <c>Screen.Report</c>. Both are asserted false first, so the test
    /// can no longer pass on a no-op.</para>
    /// </summary>
    [AvaloniaFact]
    public void Escape_from_the_picker_returns_to_the_live_day_book()
    {
        var (window, vm) = NewWindow();
        try
        {
            OpenDayBook(vm);
            var columnsBefore = vm.Columns.Count;
            Key(window, PhysicalKey.J, RawInputModifiers.Control);
            Assert.Equal(Screen.ExceptionReportsPicker, vm.CurrentScreen);
            Assert.Equal(columnsBefore + 1, vm.Columns.Count);

            Key(window, PhysicalKey.Escape);

            // The two clauses that are FALSE before the Escape — without these the test passes on a no-op.
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.Equal(columnsBefore, vm.Columns.Count);

            // …and the book underneath really is the one the operator came from.
            Assert.True(vm.IsDayBookReport);
            Assert.Equal(ReportKind.DayBook, vm.Reports!.Kind);
        }
        finally { window.Close(); }
    }

    // ===================================================== F1: no Day-Book verb fires on the row behind the picker

    /// <summary>
    /// 🔴 <b>REVIEW FINDING F1 (SEV1) — Alt+I MUST NOT INSERT AGAINST A ROW THE OPERATOR CANNOT SEE.</b>
    ///
    /// <para><b>The defect.</b> <c>RequestInsertVoucherAtHighlight</c> guards on <c>IsDayBookReport</c> and then
    /// excluded exactly one screen by name, <c>Screen.AddVoucherPicker</c>. The Exception Reports picker this
    /// track added ALSO leaves <see cref="Reports"/> bound — that is what makes Escape return to the same live
    /// book — so <c>IsDayBookReport</c> stays TRUE beneath it and the exclusion list was never extended. With the
    /// Ctrl+J menu column holding focus, Alt+I read <c>Reports.SelectedRow</c> from BEHIND the column and opened
    /// an Insert picker anchored on it. An insert RENUMBERS every voucher after its anchor, so the operator could
    /// renumber the series off a row they never saw.</para>
    ///
    /// <para><b>The control half is what makes this non-vacuous:</b> the same keystroke on the same book with no
    /// picker open must still open the Insert picker. Without it, a test asserting "nothing happened" would pass
    /// on an Alt+I that was broken everywhere.</para>
    /// </summary>
    [AvaloniaFact]
    public void Alt_I_does_not_fire_on_the_day_book_row_hidden_behind_the_exception_picker()
    {
        var (window, vm) = NewWindow();
        try
        {
            // ---- control: Alt+I on the bare Day Book DOES open the Insert picker.
            OpenDayBook(vm);
            vm.Reports!.SelectedRow = vm.Reports.Rows.First(r => r.DrillVoucherId != Guid.Empty);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(VoucherAlterationRequest.Opened, vm.RequestInsertVoucherAtHighlight());
            Assert.Equal(Screen.AddVoucherPicker, vm.CurrentScreen);

            // ---- the defect: the same verb with the Exception Reports picker on top.
            OpenDayBook(vm);
            vm.Reports!.SelectedRow = vm.Reports.Rows.First(r => r.DrillVoucherId != Guid.Empty);
            Dispatcher.UIThread.RunJobs();
            var columnsBefore = vm.Columns.Count;
            Key(window, PhysicalKey.J, RawInputModifiers.Control);
            Assert.Equal(Screen.ExceptionReportsPicker, vm.CurrentScreen);

            Assert.Equal(VoucherAlterationRequest.NoVoucherHere, vm.RequestInsertVoucherAtHighlight());
            Assert.Equal(Screen.ExceptionReportsPicker, vm.CurrentScreen);
            Assert.Equal(columnsBefore + 1, vm.Columns.Count);   // no second column was stacked

            // The real keystroke agrees with the door — the chord is not claimed where the door refuses.
            Key(window, PhysicalKey.I, RawInputModifiers.Alt);
            Assert.Equal(Screen.ExceptionReportsPicker, vm.CurrentScreen);
            Assert.Equal(columnsBefore + 1, vm.Columns.Count);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The Alt+A half of finding F1, same shape and same fix: the Add-Voucher picker reads the highlighted row
    /// for its seed date, so it must refuse under the Exception Reports column too.
    /// </summary>
    [AvaloniaFact]
    public void Alt_A_does_not_open_an_add_picker_over_the_exception_picker()
    {
        var (window, vm) = NewWindow();
        try
        {
            OpenDayBook(vm);
            Key(window, PhysicalKey.J, RawInputModifiers.Control);
            var columns = vm.Columns.Count;

            Key(window, PhysicalKey.A, RawInputModifiers.Alt);

            Assert.Equal(Screen.ExceptionReportsPicker, vm.CurrentScreen);
            Assert.Equal(columns, vm.Columns.Count);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>REVIEW FINDING F3 — Ctrl+J OVER THE Alt+A PICKER MUST NOT STACK A SECOND MENU COLUMN.</b>
    ///
    /// <para>The door's "already open" guard named only its OWN screen, so with the Alt+A Add-Voucher picker on
    /// top — a different screen, with <see cref="Reports"/> still bound beneath — Ctrl+J appended an Exception
    /// Reports column over it, burying a half-made voucher-type choice under an unrelated menu. The key arm's
    /// red-flagged comment claimed it was "guarded on the SAME condition OpenExceptionReportsPicker refuses on";
    /// it was guarded on one of the two. Both the door and the arm now refuse under ANY Day-Book picker column.</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_J_does_not_stack_an_exception_picker_over_the_add_voucher_picker()
    {
        var (window, vm) = NewWindow();
        try
        {
            OpenDayBook(vm);
            Key(window, PhysicalKey.A, RawInputModifiers.Alt);
            Assert.Equal(Screen.AddVoucherPicker, vm.CurrentScreen);
            var columns = vm.Columns.Count;

            Key(window, PhysicalKey.J, RawInputModifiers.Control);

            Assert.Equal(Screen.AddVoucherPicker, vm.CurrentScreen);
            Assert.Equal(columns, vm.Columns.Count);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The badges follow the doors (IV-31): with a picker column on top, Alt+I, Alt+A and Ctrl+J all refuse, so
    /// all three must be DIMMED rather than advertising verbs that now fire nothing. Asserted enabled first on
    /// the bare Day Book, so the test cannot pass on a bar that dims them everywhere.
    /// </summary>
    [AvaloniaFact]
    public void The_day_book_badges_are_dimmed_while_a_picker_column_is_on_top()
    {
        var (window, vm) = NewWindow();
        try
        {
            OpenDayBook(vm);
            foreach (var key in new[] { "Alt+I", "Alt+A", "Ctrl+J" })
                Assert.True(Assert.Single(vm.ButtonBar.Where(b => b.Key == key)).Enabled,
                    $"{key} is dimmed on the bare Day Book, where it bites — the rest of this test would be vacuous.");

            Key(window, PhysicalKey.J, RawInputModifiers.Control);
            Assert.Equal(Screen.ExceptionReportsPicker, vm.CurrentScreen);

            foreach (var key in new[] { "Alt+I", "Alt+A", "Ctrl+J" })
                Assert.False(Assert.Single(vm.ButtonBar.Where(b => b.Key == key)).Enabled,
                    $"{key} is advertised as ENABLED over a picker column, where its door refuses (defect IV-31).");
        }
        finally { window.Close(); }
    }

    // ===================================================== F2: the Optional scope limit reaches the screen

    /// <summary>
    /// 🔴 <b>REVIEW FINDING F2 (SEV1) — THE OPTIONAL REGISTER'S SCOPE LIMIT IS A DEAD KNOB UNLESS IT IS A ROW.</b>
    ///
    /// <para><b>The defect.</b> <c>ExceptionVouchers.OptionalScopeNote</c> carries a red-flag comment insisting
    /// the limit must be surfaced — a stock/order voucher cannot be marked Optional in this product at all, so an
    /// Optional register that says nothing reads as "no stock voucher is Optional", which is a different and
    /// untrue statement. The builder surfaced it by calling <c>Footnote()</c>, which appends to
    /// <c>PayrollFootnotes</c>; the only panel bound to that collection is inside a Grid gated on
    /// <c>IsPayrollMatrix</c>, and <c>IsPayrollMatrix</c> does not list the exception registers. The sentence was
    /// therefore written into a panel this report kind can never render. It is now a trailing
    /// <c>ReportRow</c> — the same mechanism the empty state already uses.</para>
    ///
    /// <para>Asserted on <c>Reports.Rows</c>, which is what the grid renders and what both projectors read, so it
    /// also travels into the print and the export. The other two registers must NOT carry it — the limit is real
    /// only for Optional, and a note repeated on a register it does not apply to is its own defect.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_optional_register_states_its_accounting_only_scope_on_the_register_itself()
    {
        var (window, vm) = NewWindow();
        try
        {
            OpenDayBook(vm);
            Key(window, PhysicalKey.J, RawInputModifiers.Control);
            ArrowToAndEnter(window, vm, "Optional Vouchers");

            Assert.Contains(vm.Reports!.Rows, r => r.Particulars == ExceptionVouchers.OptionalScopeNote);

            // It is NOT on the other two, whose flags both aggregates can carry.
            foreach (var other in new[] { "Cancelled Vouchers", "Post-Dated Vouchers" })
            {
                OpenDayBook(vm);
                Key(window, PhysicalKey.J, RawInputModifiers.Control);
                ArrowToAndEnter(window, vm, other);
                Assert.DoesNotContain(vm.Reports!.Rows, r => r.Particulars == ExceptionVouchers.OptionalScopeNote);
            }
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE EMPTY REGISTER IS WHERE FINDING F2 BIT HARDEST.</b> With nothing marked Optional the only
    /// visible sentence was "No voucher in this period is marked Optional" — precisely the reading
    /// <c>OptionalScopeNote</c>'s own comment says must be prevented, because a stock voucher could not have been
    /// marked one in the first place. Both sentences must be on the screen together.
    /// </summary>
    [AvaloniaFact]
    public void An_empty_optional_register_still_states_the_scope_limit_beside_its_empty_note()
    {
        var (window, vm) = NewWindow();
        try
        {
            // A window with no vouchers at all in it, so the register is genuinely empty.
            var empty = Day.AddYears(5);
            vm.OpenExceptionRegister(ReportKind.OptionalVouchersRegister, empty, empty.AddDays(1));
            Dispatcher.UIThread.RunJobs();
            var register = vm.Reports!;

            Assert.DoesNotContain(register.Rows, r => r.DrillVoucherId != Guid.Empty);
            Assert.Contains(register.Rows,
                r => r.Particulars == ExceptionVouchers.EmptyNoteFor(ExceptionVoucherKind.Optional));
            Assert.Contains(register.Rows, r => r.Particulars == ExceptionVouchers.OptionalScopeNote);
        }
        finally { window.Close(); }
    }
}
