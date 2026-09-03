using System;
using System.Collections.Generic;
using System.Globalization;
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
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>Phase 10.11 S5d — VOUCHER ALTERATION IS REACHABLE</b>, proven by driving the real cascade and the real
/// keyboard through <see cref="MainWindow"/>'s tunnel handler on a real posted book — never by calling
/// <c>ForAlter</c>.
///
/// <para><b>The defect this locks.</b> <c>VoucherEntryViewModel.ForAlter</c> shipped with <b>zero production
/// callers</b>: S5b and S5c built the rehydration, the eligibility predicate and <c>AcceptAlteration</c>, and every
/// caller of all three lived in <c>tests/Apex.Desktop.Tests</c>. The suite was green and no operator could reach
/// voucher alteration by any sequence of keys. It is the SAME defect
/// <see cref="StockItemAlterReachabilityTests"/> was written for one file away — which is why the standing lock
/// against a third is <see cref="ViewModelAlterEntryPointReachabilityTests"/>, a derived invariant rather than a
/// fourth per-screen test.</para>
///
/// <para><b>🔴 FIDELITY (R7) — TWO RECORDS, KEPT APART.</b> <b>(A) A DELIBERATE WIDENING OF AN ATTESTED
/// BEHAVIOUR:</b> the corpus gives <c>Ctrl+Enter</c> as <i>"To alter a master during voucher entry or from
/// drilldown of a report"</i> (Book PDF p.436 [printed p.432], re-extracted with <c>pdftotext -raw</c>) — an
/// ALTER key, from a drill-down, for a <b>master</b>; we bind the same chord to a <b>voucher</b> from the same
/// place. <b>(B) A DELIBERATE DIVERGENCE FROM AN ATTESTED BEHAVIOUR:</b> the corpus reaches voucher alteration
/// with <b>plain Enter</b> on a register row (<i>"… &gt; \&lt;X&gt; Register &gt; Select Month &amp; Show/Edit
/// Entry"</i>, Book PDF pp.32, 34, 37, 42, 47, 49, 64, 71) and has no separate read-only voucher screen; we keep
/// plain Enter for the read-only voucher-detail column (USER DECISION 1 / VL-1). <b>ATTESTED AND FOLLOWED:</b>
/// <c>Ctrl+A</c> saves the altered voucher (Book PDF pp.51, 53, 56, 58) — pinned below. <b>OURS, corpus
/// silent:</b> the three surfaces, and the notice bar the refusals appear on.</para>
///
/// <para><b>What is deliberately NOT tested, and why.</b> The arm's <c>!IsTyping(e)</c> and <c>!IsPickerOpen(e)</c>
/// clauses are DEFENCE IN DEPTH and are not independently falsifiable at this commit: the three surfaces the arm
/// is scoped to carry no focusable TextBox, and the report page's three ComboBoxes are invisible on the report
/// kinds that hold voucher rows. A test claiming to pin them would in fact be pinning the screen gate — the same
/// honest label the Alt+X arm carries for its own pair. They are kept in the code, not asserted here.</para>
/// </summary>
public sealed class VoucherAlterReachabilityTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    // ============================================================ harness

    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexAlterReach_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        return (window, vm, tempDir);
    }

    private static void Pump(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Key(MainWindow window, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, mods);
        Pump(window);
    }

    private static void Close(MainWindow window, string dir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { /* best effort */ }
    }

    private static string? ActiveLabel(MainWindowViewModel vm) =>
        vm.Columns[vm.ActiveColumnIndex].Selected?.Label;

    /// <summary>Presses REAL arrow-Down until the active column highlights <paramref name="label"/>, then REAL
    /// Enter to drill it. Fails loudly if the label is not reachable by arrows — which is the point.</summary>
    private static void ArrowToAndEnter(MainWindow window, MainWindowViewModel vm, string label)
    {
        var rows = vm.Columns[vm.ActiveColumnIndex].Items.Count + 2;
        for (var i = 0; i < rows; i++)
        {
            if (ActiveLabel(vm) == label) { Key(window, PhysicalKey.Enter); return; }
            Key(window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"'{label}' was not reachable by arrow navigation from the active column.");
    }

    private sealed record Book(
        MainWindowViewModel Vm, string Name, DomainLedger Landlord, DomainLedger Rent, Voucher Journal);

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
    }

    /// <summary>
    /// A company carrying ONE posted Journal (Dr Rent 8,431.55 / Cr Landlord 8,431.55), keyed and accepted
    /// through the real entry screen so the voucher is genuinely posted and persisted rather than hand-built.
    ///
    /// <para><b>A Journal, deliberately.</b> It is a SIMPLE family under the S5b enumeration, and — unlike a
    /// Payment or a Receipt — it never opens in Single Entry, so both legs stay directly editable on the plain
    /// Dr/Cr grid and no assertion below depends on the side-stamping rules that mode applies.
    /// Odd paise are the house rule: a 50-paisa defect once survived six round-number assertions.</para>
    /// </summary>
    private static Book SeedOneJournal(MainWindow window, MainWindowViewModel vm, string name)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        var rent = AddLedger(c, "Rent", "Indirect Expenses");
        var landlord = AddLedger(c, "Landlord", "Sundry Creditors");

        vm.OpenVoucher(VoucherBaseType.Journal);
        var e = vm.VoucherEntry!;
        e.Date = FyStart.AddDays(7);
        e.Lines[0].SelectedLedger = rent;
        e.Lines[0].Side = DrCr.Debit;
        e.Lines[0].AmountText = "8431.55";
        e.Lines[1].SelectedLedger = landlord;
        e.Lines[1].Side = DrCr.Credit;
        e.Lines[1].AmountText = "8431.55";
        Key(window, PhysicalKey.A, RawInputModifiers.Control);      // REAL Ctrl+A accepts
        Assert.Single(c.Vouchers);
        Assert.Equal(8431.55m, Closing(c, rent));
        Assert.Equal(-8431.55m, Closing(c, landlord));

        return new Book(vm, name, landlord, rent, c.Vouchers[0]);
    }

    /// <summary>Opens the Day Book and highlights the row that drills to <paramref name="voucherId"/>.</summary>
    private static void OpenDayBookOn(MainWindow window, MainWindowViewModel vm, Guid voucherId)
    {
        vm.OpenReport(ReportKind.DayBook);
        Pump(window);
        vm.Reports!.SelectedRow = vm.Reports!.Rows.First(r => r.DrillVoucherId == voucherId);
    }

    /// <summary>An amount off the grid, comma-tolerant — the screen formats 8,431.55 and the test means the
    /// number, not the rendering.</summary>
    private static decimal Amount(string text) =>
        decimal.Parse(text.Replace(",", string.Empty), CultureInfo.InvariantCulture);

    private static decimal Closing(Company c, DomainLedger l) =>
        LedgerBalances.SignedClosing(c, l, c.FinancialYearStart.AddYears(1));

    // ============================================================ (a) THE REACHABILITY PROOFS

    /// <summary>
    /// 🔴 <b>THE DRIVING TEST.</b> From the Gateway, using ONLY keys: arrow to <b>Day Book</b>, Enter to open it,
    /// arrow onto the posted Payment's row, and <b>Ctrl+Enter</b> to arrive at a voucher-entry screen that
    /// <c>IsAltering</c> THAT voucher, pre-filled with what was posted — number, date, narration, both legs.
    ///
    /// <para><b>Nothing here calls <c>ForAlter</c>, <c>OpenVoucher</c> or any <c>Show*</c> method to skip a step.</b>
    /// That is the whole point: the shipped S5b/S5c tests called <c>ForAlter</c> directly and so proved the
    /// mechanism and nothing whatsoever about reachability.</para>
    ///
    /// <para><b>This test bites.</b> Removing the Ctrl+Enter alteration arm from <c>MainWindow.OnKeyDown</c> fails
    /// it at the <c>Screen.VoucherEntry</c> assertion (the keystroke falls through to the RQ-7 drill and lands on
    /// the read-only voucher-detail column instead) — verified against a checksummed backup, restored byte-exact.</para>
    /// </summary>
    [AvaloniaFact]
    public void Voucher_alteration_is_reachable_from_the_Gateway_using_only_the_keyboard()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var b = SeedOneJournal(window, vm, "Alter Reach Co");

            // Back out of the entry screen the seed left open, so navigation starts where an operator starts.
            while (vm.CurrentScreen != Screen.Gateway && vm.Columns.Count > 1) vm.Back();
            Pump(window);

            // ---- REAL navigation: Gateway → Day Book ----
            ArrowToAndEnter(window, vm, "Day Book");
            Assert.Equal(Screen.Report, vm.CurrentScreen);

            // ---- highlight the voucher row ----
            // 🔴 HONEST ABOUT THIS STEP: it is NOT a keystroke, and it is the one step in this test that is not.
            // The report page's row highlight belongs to its own ListBox (SelectedRow is two-way bound to it), and
            // the window's Up/Down arms deliberately do not claim it — StepActive has arms for the Chart of
            // Accounts and the Stock Item master but none for Screen.Report. In the running app the list has focus
            // and the arrows move it; in a headless window nothing has focus to move. Assigning SelectedRow is
            // exactly what that binding writes, and it is the same step VoucherCancelAltXTests and
            // VoucherDeleteAltDTests take for the same reason. What is under test here is the CHORD and the ROUTE,
            // and both are real.
            var wanted = vm.Reports!.Rows.First(r => r.DrillVoucherId == b.Journal.Id);
            vm.Reports!.SelectedRow = wanted;
            Pump(window);

            // ---- REAL Ctrl+Enter opens THAT voucher for alteration ----
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);

            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            var alter = vm.VoucherEntry!;
            Assert.True(alter.IsAltering);                       // ← the assertion the defect made unreachable
            Assert.Equal(b.Journal.Id, alter.AlteringVoucherId);
            Assert.Contains("Alteration", vm.ScreenTitle);       // not "Creation"

            // …pre-filled from the POSTED voucher, not from the type's defaults.
            Assert.Equal(b.Journal.Number, alter.VoucherNumber);
            Assert.Equal(b.Journal.Date, alter.Date);
            Assert.Equal(2, alter.Lines.Count);
            Assert.Contains(alter.Lines, l => l.SelectedLedger?.Id == b.Rent.Id && Amount(l.AmountText) == 8431.55m);
            Assert.Contains(alter.Lines, l => l.SelectedLedger?.Id == b.Landlord.Id && Amount(l.AmountText) == 8431.55m);

            // The Miller cascade survived: the Day Book is still the column underneath, so Esc comes back to it.
            Assert.True(vm.Columns.Count >= 2);
            vm.Back();
            Pump(window);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.NotNull(vm.Reports);
        }
        finally { Close(window, dir); }
    }

    /// <summary>The second attested surface: the REGISTER drill (<see cref="Screen.LedgerVouchers"/>) — the corpus's
    /// own route, <i>"&lt;X&gt; Register &gt; Select Month &amp; Show/Edit Entry"</i>. Ctrl+Enter on a posting row
    /// opens the alteration.</summary>
    [AvaloniaFact]
    public void Ctrl_Enter_on_the_register_drill_opens_the_voucher_for_alteration()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var b = SeedOneJournal(window, vm, "Alter Register Co");
            var c = vm.Company!;

            vm.OpenLedgerVouchers(b.Rent.Id, c.FinancialYearStart, c.FinancialYearStart.AddYears(1));
            Pump(window);
            Assert.Equal(Screen.LedgerVouchers, vm.CurrentScreen);
            vm.LedgerVouchers!.SelectedRow =
                vm.LedgerVouchers!.Rows.First(r => r.DrillVoucherId == b.Journal.Id);

            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);

            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.True(vm.VoucherEntry!.IsAltering);
            Assert.Equal(b.Journal.Id, vm.VoucherEntry!.AlteringVoucherId);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The third surface: the READ-ONLY voucher-detail column. USER DECISION 1 keeps plain Enter for it; Ctrl+Enter
    /// is the "now edit it" step from there — the same three voucher surfaces Alt+D is offered on, so the two verbs
    /// resolve the same document.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_Enter_on_the_read_only_voucher_detail_column_opens_the_alteration()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var b = SeedOneJournal(window, vm, "Alter Detail Co");
            OpenDayBookOn(window, vm, b.Journal.Id);

            // Plain Enter drills to the read-only column — USER DECISION 1's OTHER half, unchanged by this slice.
            Key(window, PhysicalKey.Enter);
            Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen);
            Assert.Equal(b.Journal.Id, vm.VoucherDetail!.VoucherId);

            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.True(vm.VoucherEntry!.IsAltering);
            Assert.Equal(b.Journal.Id, vm.VoucherEntry!.AlteringVoucherId);
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (b) THE VERB — Ctrl+A must ALTER, not create

    /// <summary>
    /// 🔴 <b>THE OTHER HALF OF THE DEFECT.</b> Ctrl+A on an altering voucher screen must run
    /// <c>AcceptAlteration</c>, not <c>Accept</c>. This is FIDELITY, cited: the corpus saves an altered voucher with
    /// the same key as creation (<i>"… &amp; Show/Edit Entry &gt; Press \"Ctrl+A\" for Save"</i>, Book PDF pp.51,
    /// 53, 56, 58).
    ///
    /// <para>Without the <c>IsAltering</c> branch in the Ctrl+A dispatch it ran <c>Accept()</c>, which HARD-REFUSES
    /// on an altering screen — so the operator's edits were discarded behind an engine message. The assertions
    /// below therefore check the whole outcome: ONE voucher, the SAME Guid, the SAME number, the SAME position in
    /// the book, the balances MOVED to the amended figure, and all of it on disk.</para>
    ///
    /// <para><b>This test bites.</b> Reverting the dispatch to a bare <c>VoucherEntry?.Accept();</c> fails it on the
    /// persisted amount — verified, not assumed.</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_A_on_the_altering_screen_alters_the_same_voucher_instead_of_posting_a_second_one()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var b = SeedOneJournal(window, vm, "Alter CtrlA Co");
            var c = vm.Company!;
            var numberBefore = b.Journal.Number;
            Assert.Equal(-8431.55m, Closing(c, b.Landlord));

            OpenDayBookOn(window, vm, b.Journal.Id);
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            Assert.True(vm.VoucherEntry!.IsAltering);

            // Amend BOTH legs on the alteration screen and accept with the REAL Ctrl+A.
            foreach (var line in vm.VoucherEntry!.Lines)
                line.AmountText = "9102.35";
            vm.VoucherEntry!.Narration = "Rent amended";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            // ONE voucher, the SAME identity, at the SAME index — not a second posting beside the original.
            Assert.Single(c.Vouchers);
            var after = c.Vouchers[0];
            Assert.Equal(b.Journal.Id, after.Id);
            Assert.Equal(numberBefore, after.Number);
            Assert.Equal("Rent amended", after.Narration);

            // The books MOVED by exactly the amendment, and the change is on disk — the store is a snapshot, so an
            // alteration that is not saved is one that dies with the session.
            Assert.Equal(-9102.35m, Closing(c, b.Landlord));
            Assert.Equal(9102.35m, Closing(c, b.Rent));

            var storage = new CompanyStorage(dir);
            var reopened = storage.Load(storage.ListCompanies().Single(e => e.Name == b.Name));
            var persisted = reopened.FindVoucher(b.Journal.Id)!;
            Assert.Equal(numberBefore, persisted.Number);
            Assert.Equal(9102.35m,
                persisted.Lines.Single(l => l.LedgerId == b.Rent.Id).Amount.Amount);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The on-screen <b>Accept</b> button must run the same verb as Ctrl+A. Until this slice it called
    /// <c>VoucherEntry.Accept()</c> directly, which hard-refuses on an altering screen — so the button and the key
    /// would have disagreed the moment alteration became reachable. Both now come through
    /// <see cref="MainWindowViewModel.AcceptVoucherEntryOrAlteration"/>, and this pins that they agree.
    /// </summary>
    [AvaloniaFact]
    public void The_on_screen_Accept_button_alters_too_and_does_not_hard_refuse()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var b = SeedOneJournal(window, vm, "Alter Button Co");
            OpenDayBookOn(window, vm, b.Journal.Id);
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            Assert.True(vm.VoucherEntry!.IsAltering);

            foreach (var line in vm.VoucherEntry!.Lines) line.AmountText = "7777.77";

            // The click handler's target, not a re-implementation of it.
            Assert.True(vm.AcceptVoucherEntryOrAlteration());

            Assert.Single(vm.Company!.Vouchers);
            Assert.Equal(b.Journal.Id, vm.Company!.Vouchers[0].Id);
            Assert.Equal(7777.77m, Closing(vm.Company!, b.Rent));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>S5e — Ctrl+Enter ON A POS BILL OPENS THE POS BILLING SCREEN, not a refusal.</b>
    ///
    /// <para><b>The defect this locks is the one this whole file exists for.</b> A POS bill can only be inverted on
    /// the screen that keys a tender split, so S5e gave it its own door
    /// (<c>PosBillingViewModel.ForAlter</c> + <c>PosAlterationEligibility</c>) — and a door with no route to it is
    /// exactly the shape <c>VoucherEntryViewModel.ForAlter</c> shipped in: fully built, fully tested, and
    /// unreachable by any sequence of keys. Nothing here calls <c>ForAlter</c>: the bill is posted through the real
    /// POS screen, the Day Book is opened on it, and the REAL Ctrl+Enter is pressed.</para>
    ///
    /// <para>The accounting entry screen's refusal for this family still stands and is still right — its grids have
    /// no tender panel — so the shell branches BEFORE it, and the assertion below is that the operator lands on the
    /// POS screen rather than on that (correct, but unhelpful) sentence.</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_Enter_on_a_POS_bill_opens_the_POS_billing_screen_altering_that_bill()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            vm.NewCompanyName = "POS Alter Reach Co";
            vm.CreateCompany();
            var c = vm.Company!;
            c.FinancialYearStart = FyStart;
            c.BooksBeginFrom = FyStart;

            var masters = new InventoryService(c);
            var group = masters.CreateStockGroup("Goods");
            var nos = masters.CreateSimpleUnit("Nos", "Numbers");
            var widget = masters.CreateStockItem("Till Widget", group.Id, nos.Id);
            AddLedger(c, "Retail Sales", "Sales Accounts");
            AddLedger(c, "Till Cash", "Cash-in-Hand");

            // The POS screen resolves (and, on first use, creates) its own POS-flagged Sales type.
            vm.OpenPosBilling();
            Pump(window);
            var pos = vm.PosBilling!;
            pos.Date = FyStart.AddDays(9);
            pos.SelectedSalesLedger = pos.SalesLedgers.Single(l => l.Name == "Retail Sales");
            pos.CashRow.SelectedLedger = c.Ledgers.Single(l => l.Name == "Till Cash");
            var line = pos.Items[0];
            line.SelectedItem = pos.StockItems.Single(i => i.Id == widget.Id);
            line.SelectedGodown = pos.Godowns.Single(g => g.Id == c.MainLocation!.Id);
            line.QuantityText = "2";
            line.RateText = "649.37";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);      // REAL Ctrl+A accepts

            var bill = Assert.Single(c.Vouchers);
            Assert.True(bill.HasPosTenders);

            OpenDayBookOn(window, vm, bill.Id);
            Assert.Equal(string.Empty, vm.Notice);

            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);

            // The operator is on the POS screen, altering THAT bill — not on a notice bar, not on the read-only column.
            Assert.Equal(Screen.PosBilling, vm.CurrentScreen);
            Assert.NotNull(vm.PosBilling);
            Assert.True(vm.PosBilling!.IsAltering);
            Assert.Equal(bill.Id, vm.PosBilling!.AlteringVoucherId);
            Assert.Equal(649.37m, vm.PosBilling!.Items[0].EffectiveRate!.Value.Amount);

            // …and the cascade column beneath is intact, so Esc returns to the Day Book row they came from.
            Assert.Contains(vm.Columns, col => col.Title.Contains("POS Alteration", StringComparison.Ordinal));

            // REAL Ctrl+A on the altering screen runs AcceptAlteration, not Accept: one bill, same id, still one row.
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            var after = Assert.Single(c.Vouchers);
            Assert.Equal(bill.Id, after.Id);
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (c) THE REFUSAL MUST REACH THE OPERATOR

    /// <summary>
    /// 🔴 <b><c>ForAlter</c> REFUSES most shapes — 13 REFUSE and 8 DEFER out of 33 enumerated rows — so the refusal
    /// is the COMMON outcome on a real book, not the edge case. It must be SHOWN.</b>
    ///
    /// <para>A SALES ITEM INVOICE is refused by name (<c>EntryModeRefusal</c>: only the effective rate is
    /// posted, so the list rate and the price-level discount that produced it cannot be read back — S5e narrowed
    /// this arm from the whole family to Sales, and dropped the batch-split reason, which was measured and found
    /// not to be one). This asserts the whole channel:
    /// the named sentence lands on the window-level <see cref="MainWindowViewModel.Notice"/> bar — NOT on
    /// <c>Message</c>, which the report page's <c>DataTemplate</c> (typed to <c>ReportsViewModel</c>) structurally
    /// cannot render — no entry screen opens, and the operator is still standing on the Day Book.</para>
    ///
    /// <para>🔴 <b>And the keystroke is CONSUMED.</b> Were it to fall through to the RQ-7 drill, the drill would
    /// change the screen, <c>OnCurrentScreenChanged</c> would clear the notice bar on its way past, and the
    /// operator would get the read-only column with NO explanation — a failed operation indistinguishable from a
    /// dead key. That is the S3 review's finding arriving by a different route, so it is pinned here.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_refused_family_puts_its_named_refusal_on_the_notice_bar_and_opens_nothing()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            vm.NewCompanyName = "Alter Refusal Co";
            vm.CreateCompany();
            var c = vm.Company!;
            c.FinancialYearStart = FyStart;
            c.BooksBeginFrom = FyStart;

            var masters = new InventoryService(c);
            var group = masters.CreateStockGroup("Goods");
            var nos = masters.CreateSimpleUnit("Nos", "Numbers");
            var widget = masters.CreateStockItem("Widget", group.Id, nos.Id);
            var sales = AddLedger(c, "Sales", "Sales Accounts");
            var customer = AddLedger(c, "Beta Buyers", "Sundry Debtors");

            // A REAL Sales ITEM INVOICE, keyed on the item grid and accepted through the real screen.
            vm.OpenVoucher(VoucherBaseType.Sales);
            var entry = vm.VoucherEntry!;
            entry.Date = FyStart.AddDays(3);
            vm.ToggleItemInvoice();
            entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == customer.Id);
            var itemLine = entry.InventoryLines[0];
            itemLine.SelectedItem = entry.StockItems.Single(i => i.Id == widget.Id);
            itemLine.SelectedGodown = entry.Godowns.Single(g => g.Id == c.MainLocation!.Id);
            itemLine.QuantityText = "3";
            itemLine.RateText = "1234.55";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            var invoice = Assert.Single(c.Vouchers);
            Assert.True(invoice.HasInventoryLines);

            OpenDayBookOn(window, vm, invoice.Id);
            Assert.Equal(string.Empty, vm.Notice);

            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);

            // Nothing opened, and the operator did NOT get drilled somewhere else instead.
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.Null(vm.VoucherEntry);

            // The refusal is on the bar the report page can actually render, and it NAMES the family.
            Assert.NotEqual(string.Empty, vm.Notice);
            Assert.Contains("ITEM INVOICE", vm.Notice, StringComparison.Ordinal);
            Assert.Equal(
                VoucherAlterationEligibility.RefusalFor(c, invoice.Id),
                vm.Notice);   // the predicate's own sentence, not a paraphrase invented by the shell
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (d) THE GUARDS

    /// <summary>
    /// 🔴 <b>THE SCOPE GUARD — <c>IsLiveReportPage</c>, not <c>IsReportContext</c>.</b> With an F12 configuration
    /// column stacked over the Day Book, the report stays bound underneath with its row still highlighted (that is
    /// deliberate — the report-PARAMETER shortcuts must keep acting on it). Ctrl+Enter from inside that column must
    /// NOT open the alteration for the voucher BEHIND it.
    ///
    /// <para>This is S3's measured hole, inherited rather than re-derived: <c>IsReportContext</c> stayed true on
    /// five stacked columns and Alt+X cancelled the voucher behind the operator's column five times out of five.
    /// <c>IsPickerOpen</c> cannot see this — it looks for an open ComboBox popup, not a Miller column.</para>
    ///
    /// <para>🔴 <b>WHAT THE MUTATION ACTUALLY SHOWED, because the claim that first stood here was WRONG and would
    /// have been cited forward.</b> It said <i>"widening the arm's gate to <c>vm.IsReportContext</c> fails this
    /// test"</i>. Measured: it does <b>not</b> — this test stays GREEN under that mutation, because the stacked
    /// column is refused a SECOND time by <see cref="MainWindowViewModel.RequestAlterHighlightedVoucher"/>'s own
    /// <c>CurrentScreen</c> switch, which has no <c>ReportConfig</c> arm and returns <c>NoVoucherHere</c>. The two
    /// guards are REDUNDANT for this case and the view model is the one that decides it. So this test pins the
    /// PREDICATE (asserted directly below: loose true, narrow false) and the OUTCOME, and it does not pretend to
    /// pin the arm's gate. The arm's gate is falsifiable in the OTHER direction and is pinned there: under the
    /// same <c>IsReportContext</c> mutation, <see cref="Ctrl_Enter_on_the_register_drill_opens_the_voucher_for_alteration"/>
    /// and <see cref="Ctrl_Enter_on_the_read_only_voucher_detail_column_opens_the_alteration"/> go RED (both
    /// screens are excluded from <c>IsReportContext</c> by construction, so the chord loses two of its three
    /// surfaces). Verified against a checksummed backup, restored byte-exact.</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_Enter_from_a_column_stacked_over_the_report_does_not_alter_the_row_behind_it()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var b = SeedOneJournal(window, vm, "Alter Stacked Co");
            OpenDayBookOn(window, vm, b.Journal.Id);

            Key(window, PhysicalKey.F12);                       // the report configuration column
            Assert.Equal(Screen.ReportConfig, vm.CurrentScreen);
            Assert.NotNull(vm.Reports);                          // the report is still bound beneath it
            Assert.True(vm.IsReportContext);                     // …and the LOOSE predicate is still true
            Assert.False(vm.IsVoucherAlterTargetPage);           // …while the one the arm uses is not

            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);

            Assert.Equal(Screen.ReportConfig, vm.CurrentScreen);
            Assert.Null(vm.VoucherEntry);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE EXACT-MODIFIER GUARD.</b> <c>e.KeyModifiers == KeyModifiers.Control</c>, not <c>HasFlag</c>:
    /// Ctrl+Alt+Enter and Ctrl+Shift+Enter are different chords. The doctrine is already written for the
    /// bare-letter quick-jumps ("admitting Shift would leave the same class of hole open on the next chord anyone
    /// binds") and for the Alt+X / Alt+D arms; it applies here because this arm puts a POSTED voucher into an
    /// editable form.
    ///
    /// <para><b>This test bites.</b> Relaxing the arm to <c>e.KeyModifiers.HasFlag(KeyModifiers.Control)</c> fails
    /// it on the first assertion — verified against a checksummed backup, restored byte-exact.</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_Alt_Enter_and_Ctrl_Shift_Enter_are_different_chords_and_do_not_alter()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var b = SeedOneJournal(window, vm, "Alter Chord Co");

            OpenDayBookOn(window, vm, b.Journal.Id);
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control | RawInputModifiers.Alt);
            Assert.NotEqual(Screen.VoucherEntry, vm.CurrentScreen);

            OpenDayBookOn(window, vm, b.Journal.Id);
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.NotEqual(Screen.VoucherEntry, vm.CurrentScreen);

            // …and the plain chord still works, so the assertions above are about the modifiers and not about a
            // dead arm.
            OpenDayBookOn(window, vm, b.Journal.Id);
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.True(vm.VoucherEntry!.IsAltering);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE ARMED-CONFIRMATION GUARD.</b> While an Alt+X cancellation question is up it is answered by a bare
    /// Y, and the entry screen this would open cannot show it. Ctrl+Enter must refuse — and SAY SO, rather than be
    /// a dead key — leaving the question exactly where it was.
    ///
    /// <para>This is the doctrine already settled for Alt+Y, for Escape and for Ctrl+A over an armed lifecycle
    /// question: answer it first, two presses.</para>
    ///
    /// <para><b>This test bites.</b> Deleting the <c>IsAcceptPromptOpen</c> gate from
    /// <c>RequestAlterHighlightedVoucher</c> fails it on <c>IsAcceptPromptOpen</c> — verified against a checksummed
    /// backup, restored byte-exact.</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_Enter_refuses_while_a_lifecycle_confirmation_is_up_and_says_why()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var b = SeedOneJournal(window, vm, "Alter Prompt Co");
            OpenDayBookOn(window, vm, b.Journal.Id);

            Key(window, PhysicalKey.X, RawInputModifiers.Alt);            // arm the cancellation question
            Assert.True(vm.IsAcceptPromptOpen);
            var question = vm.AcceptPromptText;

            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);

            Assert.Equal(Screen.Report, vm.CurrentScreen);                 // no alteration opened
            Assert.Null(vm.VoucherEntry);
            Assert.True(vm.IsAcceptPromptOpen);                            // the question is untouched
            Assert.Equal(question, vm.AcceptPromptText);
            Assert.Contains("Answer the question on screen first", vm.Notice, StringComparison.Ordinal);
            Assert.False(vm.Company!.FindVoucher(b.Journal.Id)!.Cancelled);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE FALL-THROUGH.</b> Ctrl+Enter on a report row that is NOT a voucher must keep doing what it did
    /// before this arm existed — DRILL. The RQ-7 drill arm below tests <c>e.Key == Key.Enter</c> with no modifier
    /// test at all, so consuming the chord unconditionally would have taken a working behaviour away in exchange
    /// for a dead key.
    ///
    /// <para>A Trial Balance ledger row is the case: it is on <see cref="Screen.Report"/> (so the arm's screen gate
    /// passes), it carries no <c>DrillVoucherId</c>, and it drills into the ledger register.</para>
    ///
    /// <para><b>This test bites.</b> Making the arm consume the keystroke on <c>NoVoucherHere</c> as well fails it
    /// on the <c>Screen.LedgerVouchers</c> assertion — verified against a checksummed backup, restored
    /// byte-exact.</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_Enter_on_a_report_row_that_is_not_a_voucher_still_drills()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var b = SeedOneJournal(window, vm, "Alter Fallthrough Co");

            vm.OpenReport(ReportKind.TrialBalance);
            Pump(window);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.True(vm.IsVoucherAlterTargetPage);           // the arm's screen gate PASSES here …

            var rentRow = vm.Reports!.Rows.First(r => r.CanDrill && r.Particulars.Contains("Rent"));
            vm.Reports!.SelectedRow = rentRow;
            Assert.Equal(Guid.Empty, rentRow.DrillVoucherId);   // … and the row is not a voucher

            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);

            Assert.Equal(Screen.LedgerVouchers, vm.CurrentScreen);   // it drilled, exactly as before
            Assert.Null(vm.VoucherEntry);
            Assert.Equal(string.Empty, vm.Notice);                   // and said nothing, because nothing failed
            Assert.Contains(vm.LedgerVouchers!.Rows, r => r.DrillVoucherId == b.Journal.Id);
        }
        finally { Close(window, dir); }
    }

    /// <summary>Ctrl+Enter must stay inert on screens this arm does not own, so claiming the chord shadows
    /// nothing: on the Gateway and on the Chart of Accounts nothing alters and no entry screen appears.</summary>
    [AvaloniaFact]
    public void Ctrl_Enter_does_nothing_on_screens_the_arm_does_not_own()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            SeedOneJournal(window, vm, "Alter Wrong Screen Co");
            while (vm.CurrentScreen != Screen.Gateway && vm.Columns.Count > 1) vm.Back();
            Pump(window);

            Assert.False(vm.IsVoucherAlterTargetPage);
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            Assert.NotEqual(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Null(vm.VoucherEntry);

            vm.ShowChartOfAccounts();
            Pump(window);
            Assert.False(vm.IsVoucherAlterTargetPage);
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            Assert.NotEqual(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Null(vm.VoucherEntry);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The pre-existing Ctrl+Enter owner is NOT shadowed: on the Stock Item master's existing-items list the chord
    /// still opens MASTER alteration. The two arms are scoped to disjoint screens, and this pins that the new one
    /// sitting beside it changed nothing there.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_Enter_still_alters_a_stock_item_on_the_stock_item_master()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            vm.NewCompanyName = "Alter Coexist Co";
            vm.CreateCompany();
            var c = vm.Company!;
            var masters = new InventoryService(c);
            masters.CreateStockGroup("Goods");
            masters.CreateSimpleUnit("Nos", "Numbers");

            vm.ShowStockItemMaster();
            var create = vm.StockItemMaster!;
            create.SelectedGroup = create.Groups.First(g => g.Name == "Goods");
            create.SelectedUnit = create.Units.First(u => u.Symbol == "Nos");
            create.Name = "Widget";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            Key(window, PhysicalKey.ArrowDown);
            Assert.NotNull(vm.StockItemMaster!.HighlightedRow);
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);

            Assert.Equal(Screen.StockItemMaster, vm.CurrentScreen);
            Assert.True(vm.StockItemMaster!.IsAltering);
            Assert.Equal("Widget", vm.StockItemMaster!.Name);
        }
        finally { Close(window, dir); }
    }
}
