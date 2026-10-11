using System;
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
/// 🔴 <b>CENSUS 11.4 GAP (a) — THE DAY BOOK'S F4 (VOUCHER TYPE) FILTER AND ITS F12 SHOW-NARRATION KNOB.</b>
///
/// <para><b>The gap, as the census cell names it.</b> "a single Amount column … no voucher-kind filter; no
/// show-narration or show-inventory-details toggles". Two of those three clauses are closed here; the Dr/Cr
/// split is NOT, and no assertion in this file pretends otherwise.</para>
///
/// <para><b>Fidelity (R7 / ruling 14), opened by content on 2026-10-09.</b> From
/// <c>help.tallysolutions.com/tally-prime/accounting-financial-reports/day-book-tally/</c>:
/// <i>"Day Book &gt; F4 (Voucher Type), and select the Debit Note voucher type."</i>,
/// <i>"Press F4 (Voucher Type) &gt; Purchase."</i> and <i>"If you want to view more details of the transactions
/// such as narration and cost centre, press F12 (Configure) and enable the configurations as required."</i>
/// From <c>help.tallysolutions.com/working-with-reports/</c>: <i>"F4 — This button name differs based on the
/// report you are viewing."</i></para>
///
/// <para>🔴 <b>EVERY ASSERTION DRIVES THE REAL KEY THROUGH <see cref="MainWindow"/>'S TUNNEL HANDLER AND READS
/// THE REALISED ROWS, because that is the half of this campaign's shipped controls that kept being missing:
/// Alt+P was inert on every screen while its verb existed, and nine FK guard columns had correct code no test
/// could see. A test that called <c>SetVoucherTypeFilter</c> on a hand-made view model would have passed against
/// an F4 key that was never bound.</b></para>
/// </summary>
public sealed class DayBookVoucherTypeFilterTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "ApexDbF4_" + Guid.NewGuid().ToString("N"));

    private static readonly DateOnly Day = new(2020, 4, 15);

    /// <summary>
    /// The narration carried by voucher 806, the PARTY voucher — asserted verbatim, so the F12 knob is proven by
    /// the text reaching the row rather than by a flag being set.
    ///
    /// <para>🔴 <b>IT HAS TO BE A VOUCHER WITH A PARTY, AND THE FIRST DRAFT OF THIS FILE GOT THAT WRONG — THE
    /// TEST CAUGHT IT AND THE FIXTURE CHANGED RATHER THAN THE ASSERTION.</b> <c>DayBook.Build</c> falls back to the
    /// narration for a voucher with no party, so on 801 the narration is ALREADY the secondary text with the knob
    /// off, and the draft's "off ⇒ narration absent" assertion was pinning behaviour the product does not have
    /// (and should not: a partyless Journal would otherwise render a blank particulars). The knob's real job is
    /// the voucher where the party occupies that text and the narration is the EXTRA detail, which is 806.</para>
    /// </summary>
    private const string Narration806 = "Advance against invoice INV-17, bearer cheque";

    /// <summary>The narration on 801, a voucher with NO party — where it is already the particulars.</summary>
    private const string Narration801 = "April office rent, paid by cash";

    /// <summary>The party ledger 806 is posted against.</summary>
    private const string PartyName = "F4 Fixture Party";

    /// <summary>
    /// Robert plus THREE vouchers of THREE DIFFERENT TYPES on one day: two Payments (801, 802), one Receipt
    /// (803) and one PURE-STOCK Receipt Note (804).
    ///
    /// <para>🔴 <b>Three types rather than two, and one of them in the OTHER AGGREGATE, is what makes the filter
    /// falsifiable.</b> A filter that narrowed nothing would show all four on every choice; a filter that read
    /// only <c>Company.Vouchers</c> would return an EMPTY book for "Receipt Note" on a company whose Receipt Note
    /// exists — the eight-rows-invisible defect of census 4.9–4.16 re-committed inside a filter. Two Payments
    /// rather than one so a narrowing cannot be mistaken for "shows exactly one row".</para>
    /// </summary>
    private (MainWindow Window, MainWindowViewModel Vm) NewWindow()
    {
        var vm = new MainWindowViewModel(new CompanyStorage(_tempDir));
        vm.LoadRobertDemo();

        var c = vm.Company!;
        var cash = c.FindLedgerByName("Cash")!;
        var rent = c.FindLedgerByName("Office Rent")!;
        var paymentType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Payment).Id;
        var receiptType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Receipt).Id;
        var svc = new LedgerService(c);

        // A party ledger, so 806 can carry BOTH a party (which occupies the secondary text) and a narration
        // (which is the extra detail F12's knob reveals). Under a real seed group, through Company.AddLedger.
        var debtors = c.FindGroupByName("Sundry Debtors")!;
        var party = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), PartyName, debtors.Id, Money.Zero, true);
        c.AddLedger(party);

        Voucher Make(int number, Guid typeId, string? narration, Guid? partyId = null) => new(
            Guid.NewGuid(), typeId, Day,
            new[]
            {
                new EntryLine(rent.Id, new Money(500m), DrCr.Debit),
                new EntryLine(cash.Id, new Money(500m), DrCr.Credit),
            },
            number: number, narration: narration, partyId: partyId);

        svc.Post(Make(801, paymentType, Narration801));
        svc.Post(Make(802, paymentType, null));
        svc.Post(Make(803, receiptType, null));
        svc.Post(Make(806, paymentType, Narration806, party.Id));
        SeedStockVoucher(c);

        vm.ShowGateway();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    /// <summary>One pure-stock Receipt Note (804) — the row that proves the filter reaches the SECOND aggregate.</summary>
    private static void SeedStockVoucher(Company c)
    {
        var masters = new InventoryService(c);
        var group = masters.CreateStockGroup("F4 Goods");
        var unit = masters.CreateSimpleUnit("F4Nos", "F4 Numbers");
        var item = masters.CreateStockItem("F4 Widget", group.Id, unit.Id);
        var godown = c.MainLocation
            ?? throw new InvalidOperationException("the fixture company has no main location to stock");
        var receiptNote = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.ReceiptNote);

        new InventoryPostingService(c).Post(new InventoryVoucher(
            Guid.NewGuid(), receiptNote.Id, Day,
            new[] { new InventoryAllocation(item.Id, godown.Id, 5m, StockDirection.Inward, new Money(100m)) },
            number: 804, narration: null, partyId: null));
    }

    private static void Key(MainWindow window, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, mods);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Opens the Day Book as a live report page — the surface F4 (Voucher Type) is bound on.</summary>
    private static void OpenDayBook(MainWindowViewModel vm)
    {
        vm.OpenReport(ReportKind.DayBook);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsDayBookFamilyReport,
            "the Day Book did not open as a Day-Book-family report; every assertion below would be vacuous");
    }

    /// <summary>Arrows the open picker to <paramref name="label"/> and activates it with Enter — the real gesture.</summary>
    private static void ArrowToAndEnter(MainWindow window, MainWindowViewModel vm, string label)
    {
        for (var i = 0; i < 60; i++)
        {
            if (vm.Columns[vm.ActiveColumnIndex].Selected?.Label == label)
            {
                Key(window, PhysicalKey.Enter);
                return;
            }
            Key(window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"'{label}' was not reachable by arrow navigation on the F4 Voucher Type picker.");
    }

    /// <summary>Presses F4 on the live book and takes <paramref name="label"/> from the picker it opens.</summary>
    private static void FilterTo(MainWindow window, MainWindowViewModel vm, string label)
    {
        Key(window, PhysicalKey.F4);
        Assert.Equal(Screen.VoucherTypeFilterPicker, vm.CurrentScreen);
        ArrowToAndEnter(window, vm, label);
        Assert.Equal(Screen.Report, vm.CurrentScreen);
    }

    /// <summary>
    /// The voucher numbers the live report actually lists, resolved through BOTH drill slots — a pure-stock row
    /// carries <see cref="Guid.Empty"/> in the accounting slot, so reading one slot alone would silently DROP the
    /// stock row instead of comparing it (the finding <c>ExceptionReportsRegisterTests</c> records as F7).
    /// </summary>
    private static int[] ListedNumbers(MainWindowViewModel vm)
    {
        var numbers = new System.Collections.Generic.List<int>();
        foreach (var r in vm.Reports!.Rows)
        {
            if (r.DrillVoucherId != Guid.Empty)
                numbers.Add(vm.Company!.FindVoucher(r.DrillVoucherId)!.Number);
            else if (r.DrillInventoryVoucherId != Guid.Empty)
                numbers.Add(vm.Company!.FindInventoryVoucher(r.DrillInventoryVoucherId)!.Number);
        }
        numbers.Sort();
        return numbers.ToArray();
    }

    private static ButtonBarItem F4Row(MainWindowViewModel vm) =>
        vm.ButtonBar.First(b => b.Key == "F4");

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

    // ============================================================ the chord, and what it does NOT displace

    /// <summary>
    /// 🔴 <b>F4 ON THE DAY BOOK IS "Voucher Type"; F4 EVERYWHERE ELSE IS STILL "Contra".</b> The settled keyboard
    /// contract keeps F4 on the Contra voucher, and the vendor documents F4 as the per-report context key, so the
    /// two must coexist. This fails if either half breaks — a Day Book that still offers Contra, or a Trial
    /// Balance that lost it.
    ///
    /// <para>It also asserts there is EXACTLY ONE F4 row, which is the mechanical half: the shell's
    /// <c>Fire()</c>/hint lookup takes the FIRST key match, so a second row would shadow one of the two verbs and
    /// the badge would fire the wrong one.</para>
    /// </summary>
    [AvaloniaFact]
    public void F4_is_Voucher_Type_on_the_Day_Book_and_still_Contra_elsewhere()
    {
        var (window, vm) = NewWindow();
        try
        {
            vm.OpenReport(ReportKind.TrialBalance);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, vm.ButtonBar.Count(b => b.Key == "F4"));
            Assert.Equal("Contra", F4Row(vm).Caption);

            OpenDayBook(vm);
            Assert.Equal(1, vm.ButtonBar.Count(b => b.Key == "F4"));
            Assert.Equal("Voucher Type", F4Row(vm).Caption);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The picker the real F4 key opens offers the vendor's unfiltered row FIRST and the company's own types
    /// after it. "All Vouchers" is the vendor's own label for the unfiltered book (the Day Book page's view knob:
    /// <i>"displays Day Book for all the vouchers, irrespective of the type of voucher"</i>), and without it F4
    /// would be a ONE-WAY narrowing.
    /// </summary>
    [AvaloniaFact]
    public void F4_opens_a_picker_whose_first_row_is_the_vendors_All_Vouchers()
    {
        var (window, vm) = NewWindow();
        try
        {
            OpenDayBook(vm);
            Key(window, PhysicalKey.F4);

            Assert.Equal(Screen.VoucherTypeFilterPicker, vm.CurrentScreen);
            var labels = vm.Columns[^1].Items.Where(i => i.IsSelectable).Select(i => i.Label).ToArray();
            Assert.Equal("All Vouchers", labels[0]);
            Assert.Contains("Payment", labels);
            Assert.Contains("Receipt Note", labels);
            // The highlight lands on the unfiltered row, so Enter straight away is a safe no-op rather than an
            // arbitrary narrowing onto whichever type sorts first.
            Assert.Equal("All Vouchers", vm.Columns[^1].Selected?.Label);
        }
        finally { window.Close(); }
    }

    // ============================================================ the filter itself, on the realised rows

    /// <summary>
    /// 🔴 <b>THE CORE ASSERTION: the realised Day Book lists ONLY the chosen type's vouchers.</b> Unfiltered it
    /// shows 801, 802, 803 and 804; "Payment" must show 801 and 802 and nothing else.
    /// </summary>
    [AvaloniaFact]
    public void F4_Payment_narrows_the_realised_Day_Book_to_the_payments()
    {
        var (window, vm) = NewWindow();
        try
        {
            OpenDayBook(vm);
            var unfiltered = ListedNumbers(vm);
            Assert.Contains(801, unfiltered);
            Assert.Contains(802, unfiltered);
            Assert.Contains(803, unfiltered);
            Assert.Contains(804, unfiltered);

            FilterTo(window, vm, "Payment");

            var filtered = ListedNumbers(vm);
            Assert.Contains(801, filtered);
            Assert.Contains(802, filtered);
            Assert.DoesNotContain(803, filtered);
            Assert.DoesNotContain(804, filtered);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE SECOND-AGGREGATE ASSERTION, AND IT IS THE ONE THAT WOULD HAVE CAUGHT THE OBVIOUS IMPLEMENTATION
    /// MISTAKE.</b> A Receipt Note is an <c>InventoryVoucher</c>, so a filter applied to the accounting loop alone
    /// returns an EMPTY book for a type whose vouchers all exist — and an empty register reads as "you have none",
    /// which is the defect class census rows 4.9–4.16 exist to record. 804 must be listed, 801–803 must not.
    /// </summary>
    [AvaloniaFact]
    public void F4_Receipt_Note_narrows_to_the_pure_stock_voucher_in_the_other_aggregate()
    {
        var (window, vm) = NewWindow();
        try
        {
            OpenDayBook(vm);
            FilterTo(window, vm, "Receipt Note");

            var filtered = ListedNumbers(vm);
            Assert.Equal(new[] { 804 }, filtered);
        }
        finally { window.Close(); }
    }

    /// <summary>"All Vouchers" takes the narrowing back off, so the filter is not a one-way door.</summary>
    [AvaloniaFact]
    public void All_Vouchers_restores_the_unfiltered_book()
    {
        var (window, vm) = NewWindow();
        try
        {
            OpenDayBook(vm);
            var unfiltered = ListedNumbers(vm);

            FilterTo(window, vm, "Payment");
            Assert.DoesNotContain(803, ListedNumbers(vm));

            FilterTo(window, vm, "All Vouchers");
            Assert.Equal(unfiltered, ListedNumbers(vm));
            Assert.Null(vm.Reports!.VoucherTypeFilterId);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>A NARROWED BOOK MUST SAY SO IN ITS OWN SUBTITLE, AND THIS IS THE ASSERTION THAT MAKES IT A
    /// WRONG-FIGURES GUARD RATHER THAN A COSMETIC ONE.</b> The subtitle is what the print projector, the export
    /// and the share payload all read for the header, so a Day Book showing two of four vouchers under a header
    /// that reads only "01-Apr-2020 to …" is a register misstating its own contents on paper.
    /// </summary>
    [AvaloniaFact]
    public void A_narrowed_book_declares_the_filter_in_its_subtitle_and_its_F4_caption()
    {
        var (window, vm) = NewWindow();
        try
        {
            OpenDayBook(vm);
            Assert.DoesNotContain("only", vm.Reports!.Subtitle);

            FilterTo(window, vm, "Payment");

            Assert.Contains("Payment only", vm.Reports!.Subtitle);
            Assert.Equal("Voucher Type: Payment", F4Row(vm).Caption);

            FilterTo(window, vm, "All Vouchers");
            Assert.DoesNotContain("only", vm.Reports!.Subtitle);
            Assert.Equal("Voucher Type", F4Row(vm).Caption);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The filter is per voucher TYPE, not per base kind — and this is asserted on the data, because the Alt+A
    /// picker shipped the opposite mistake first (it passed the base kind and opened a different type than the
    /// row the operator stood on). A second Payment series must be filterable away from the first.
    /// </summary>
    [AvaloniaFact]
    public void The_filter_separates_two_types_that_share_one_base_kind()
    {
        var (window, vm) = NewWindow();
        try
        {
            var c = vm.Company!;
            var basePayment = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Payment);
            var second = new VoucherTypeService(c)
                .Create("Petty Payment", VoucherBaseType.Payment, NumberingMethod.Automatic);

            var cash = c.FindLedgerByName("Cash")!;
            var rent = c.FindLedgerByName("Office Rent")!;
            new LedgerService(c).Post(new Voucher(
                Guid.NewGuid(), second.Id, Day,
                new[]
                {
                    new EntryLine(rent.Id, new Money(50m), DrCr.Debit),
                    new EntryLine(cash.Id, new Money(50m), DrCr.Credit),
                },
                number: 805, narration: null, partyId: null));

            OpenDayBook(vm);
            FilterTo(window, vm, "Petty Payment");

            var filtered = ListedNumbers(vm);
            Assert.Equal(new[] { 805 }, filtered);
            Assert.NotEqual(basePayment.Id, second.Id);
        }
        finally { window.Close(); }
    }

    // ============================================================ the Ctrl+J registers inherit the narrowing

    /// <summary>
    /// The three Ctrl+J exception registers are built by FILTERING the Day Book, and their own class summary
    /// commits them to never disagreeing with the book the operator pressed Ctrl+J on. So the narrowing carries:
    /// a Cancelled register opened under "F4 &gt; Payment" must not list a cancelled Receipt.
    ///
    /// <para><b>This clause is OURS, not the vendor's</b> — the vendor attests F4 on the Day Book and is silent on
    /// these registers (<c>docs/invented-vs-cloned.md</c>).</para>
    /// </summary>
    [AvaloniaFact]
    public void An_exception_register_honours_the_voucher_type_narrowing()
    {
        var (window, vm) = NewWindow();
        try
        {
            var c = vm.Company!;
            var cash = c.FindLedgerByName("Cash")!;
            var rent = c.FindLedgerByName("Office Rent")!;
            var paymentType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Payment).Id;
            var receiptType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Receipt).Id;
            var svc = new LedgerService(c);

            Voucher Cancelled(int number, Guid typeId) => new(
                Guid.NewGuid(), typeId, Day,
                new[]
                {
                    new EntryLine(rent.Id, new Money(900m), DrCr.Debit),
                    new EntryLine(cash.Id, new Money(900m), DrCr.Credit),
                },
                number: number, narration: null, partyId: null, cancelled: true);

            svc.Post(Cancelled(811, paymentType));
            svc.Post(Cancelled(812, receiptType));

            // Open the Cancelled register directly over the Day Book's window, then narrow it with F4.
            vm.OpenReport(ReportKind.DayBook);
            Dispatcher.UIThread.RunJobs();
            Key(window, PhysicalKey.J, RawInputModifiers.Control);
            ArrowToAndEnter(window, vm, "Cancelled Vouchers");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ReportKind.CancelledVouchersRegister, vm.Reports!.Kind);
            var both = ListedNumbers(vm);
            Assert.Contains(811, both);
            Assert.Contains(812, both);

            FilterTo(window, vm, "Payment");

            var narrowed = ListedNumbers(vm);
            Assert.Contains(811, narrowed);
            Assert.DoesNotContain(812, narrowed);
            Assert.Contains("Payment only", vm.Reports!.Subtitle);
        }
        finally { window.Close(); }
    }

    // ============================================================ the refusals

    /// <summary>
    /// 🔴 <b>F4 REFUSES WHILE A COLUMN IS DRAWN OVER THE BOOK</b>, through <c>IsDayBookRowHidden</c> — the same
    /// predicate Alt+A, Alt+I and Ctrl+J are gated on, and for the recorded reason: with a modal column on top the
    /// operator cannot see the list the verb would act on. Asserted by pressing the real key with the Ctrl+J
    /// picker already up and checking that NO second picker was stacked.
    /// </summary>
    [AvaloniaFact]
    public void F4_refuses_while_another_column_is_drawn_over_the_book()
    {
        var (window, vm) = NewWindow();
        try
        {
            OpenDayBook(vm);
            Key(window, PhysicalKey.J, RawInputModifiers.Control);
            Assert.Equal(Screen.ExceptionReportsPicker, vm.CurrentScreen);
            var columns = vm.Columns.Count;

            Key(window, PhysicalKey.F4);

            Assert.Equal(Screen.ExceptionReportsPicker, vm.CurrentScreen);
            Assert.Equal(columns, vm.Columns.Count);
            Assert.False(F4Row(vm).Enabled,
                "the F4 badge stayed LIT over a column that hides the book — the badge and the key must refuse "
                + "together, which is the defect class the Ctrl+B door records.");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// A narrowing cannot survive onto a report kind that has no such control. Drilling the filter onto another
    /// kind in the same viewer must clear it, or the rows would be built unfiltered while the subtitle still
    /// claimed "— Payment only" — a header disagreeing with its own figures.
    /// </summary>
    [AvaloniaFact]
    public void The_narrowing_does_not_survive_onto_a_kind_that_cannot_honour_it()
    {
        var (window, vm) = NewWindow();
        try
        {
            OpenDayBook(vm);
            FilterTo(window, vm, "Payment");
            Assert.NotNull(vm.Reports!.VoucherTypeFilterId);

            vm.Reports!.Show(ReportKind.TrialBalance);
            Dispatcher.UIThread.RunJobs();

            Assert.Null(vm.Reports!.VoucherTypeFilterId);
            Assert.False(vm.Reports!.SupportsVoucherTypeFilter);
            Assert.DoesNotContain("only", vm.Reports!.Subtitle);
        }
        finally { window.Close(); }
    }

    // ============================================================ Alt+F12 on the Ctrl+J registers

    /// <summary>
    /// 🔴 <b>A LIVE CAPABILITY BEHIND A DISABLED DOOR — THE MIRROR-IMAGE OF THE DEAD KNOB, FOUND WHILE MEASURING
    /// THIS AREA AND FIXED HERE.</b> <c>BuildExceptionRegister</c> calls <c>_sortFilter.Apply</c> over the Day
    /// Book's own two projections and renders the "No rows match the current filter." empty state, so the Alt+F12
    /// view really does act on all three registers — and its own comment says so in writing: "the same sort/filter
    /// view the Day Book offers … so Alt+F12 behaves identically on a register and on the book it was opened
    /// from". <c>ReportsViewModel.SupportsSortFilter</c> nonetheless listed only the Day Book, so the panel took
    /// its REFUSAL branch ("does not act on this report") and every editable control on it was disabled through
    /// <c>IsEnabled="{Binding SupportsSortFilter}"</c>. A shipped comment that was false.
    ///
    /// <para>Asserted in BOTH directions: the panel must not refuse on a register, AND the filter must actually
    /// narrow its rows — "stop refusing" alone would satisfy half of this and leave a door onto nothing.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_sort_filter_panel_acts_on_a_Ctrl_J_register_instead_of_refusing_it()
    {
        var (window, vm) = NewWindow();
        try
        {
            var c = vm.Company!;
            var cash = c.FindLedgerByName("Cash")!;
            var rent = c.FindLedgerByName("Office Rent")!;
            var payment = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Payment).Id;
            var receipt = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Receipt).Id;
            var svc = new LedgerService(c);

            Voucher Cancelled(int number, Guid typeId) => new(
                Guid.NewGuid(), typeId, Day,
                new[]
                {
                    new EntryLine(rent.Id, new Money(900m), DrCr.Debit),
                    new EntryLine(cash.Id, new Money(900m), DrCr.Credit),
                },
                number: number, narration: null, partyId: null, cancelled: true);

            svc.Post(Cancelled(821, payment));
            svc.Post(Cancelled(822, receipt));

            vm.OpenReport(ReportKind.DayBook);
            Dispatcher.UIThread.RunJobs();
            Key(window, PhysicalKey.J, RawInputModifiers.Control);
            ArrowToAndEnter(window, vm, "Cancelled Vouchers");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ReportKind.CancelledVouchersRegister, vm.Reports!.Kind);
            Assert.Contains(821, ListedNumbers(vm));
            Assert.Contains(822, ListedNumbers(vm));

            var panel = new ReportSortFilterViewModel(vm.Reports!);
            Assert.False(panel.CannotSortOrFilter,
                "the Alt+F12 panel refuses on a register whose builder honours the view — a live capability "
                + "behind a disabled door, and the builder's own comment says the opposite");

            // The register's particulars read "<Type> No. <n>", so a name filter on the type narrows it.
            panel.NameContains = "Receipt";
            panel.Apply();
            Dispatcher.UIThread.RunJobs();

            Assert.Contains("Applied", panel.Status);
            var narrowed = ListedNumbers(vm);
            Assert.Contains(822, narrowed);
            Assert.DoesNotContain(821, narrowed);
        }
        finally { window.Close(); }
    }

    // ============================================================ F12: Show narration

    /// <summary>
    /// 🔴 <b>THE F12 SHOW-NARRATION KNOB, PROVEN BY THE NARRATION TEXT REACHING THE REALISED ROW.</b> Off, the row
    /// for voucher 806 shows its PARTY and not its narration; on, it shows both. Asserted on
    /// <c>ReportRow.Secondary</c> — the string the grid and the print projector both render — not on the boolean
    /// that was just set, and the knob is toggled back off so the "on" state is proven to be the knob's doing.
    /// </summary>
    [AvaloniaFact]
    public void F12_Show_narration_puts_the_narration_on_the_realised_row()
    {
        var (window, vm) = NewWindow();
        try
        {
            OpenDayBook(vm);
            Assert.True(vm.Reports!.SupportsNarration, "the Day Book must offer the knob or this is vacuous");
            Assert.Contains(vm.Reports!.Rows, r => r.Secondary.Contains(PartyName));
            Assert.DoesNotContain(vm.Reports!.Rows, r => r.Secondary.Contains(Narration806));

            vm.Reports!.SetShowNarration(true);
            Dispatcher.UIThread.RunJobs();

            var row = vm.Reports!.Rows.Single(r => r.Secondary.Contains(Narration806));
            Assert.Contains(PartyName, row.Secondary);

            vm.Reports!.SetShowNarration(false);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(vm.Reports!.Rows, r => r.Secondary.Contains(Narration806));
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The F12 PANEL is the operator's door to the knob, so the panel is driven too: open it with the real F12
    /// key, tick the box, Apply, and read the realised row. A knob that only answers to a view-model call is the
    /// unreachable-control defect this file's summary names.
    /// </summary>
    [AvaloniaFact]
    public void The_F12_panel_offers_Show_narration_and_applying_it_reaches_the_rows()
    {
        var (window, vm) = NewWindow();
        try
        {
            OpenDayBook(vm);
            Key(window, PhysicalKey.F12);

            Assert.Equal(Screen.ReportConfig, vm.CurrentScreen);
            var panel = vm.ReportConfig!;
            Assert.True(panel.SupportsNarration);
            Assert.True(panel.SupportsDisplayOptions,
                "the 'Display' heading must show above the box, or the knob renders under the period fields with "
                + "no section it belongs to");

            panel.ShowNarration = true;
            panel.Apply();
            Dispatcher.UIThread.RunJobs();

            Assert.True(vm.Reports!.ShowNarration);
            Assert.Contains(vm.Reports!.Rows, r => r.Secondary.Contains(Narration806));
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The narration is NOT printed twice on a voucher that has no party. <c>DayBook.Build</c> already falls back
    /// to the narration for the particulars, so on a Journal or a Contra the narration IS the secondary text —
    /// appending it again would render "x  ·  x" on exactly the vouchers an operator turns the knob on for.
    /// </summary>
    [AvaloniaFact]
    public void Show_narration_does_not_duplicate_a_narration_that_is_already_the_particulars()
    {
        var (window, vm) = NewWindow();
        try
        {
            OpenDayBook(vm);
            vm.Reports!.SetShowNarration(true);
            Dispatcher.UIThread.RunJobs();

            var row = vm.Reports!.Rows.Single(r => r.Secondary.Contains(Narration801));
            var occurrences = row.Secondary.Split(Narration801).Length - 1;
            Assert.Equal(1, occurrences);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The knob is OFF on a report that cannot honour it, and turning it on there is a no-op rather than a stored
    /// flag nothing applies — the dead-field shape (census T1-14), where the panel would answer "Applied — report
    /// recomputed" having changed nothing.
    /// </summary>
    [AvaloniaFact]
    public void Show_narration_is_inert_outside_the_Day_Book_family()
    {
        var (window, vm) = NewWindow();
        try
        {
            vm.OpenReport(ReportKind.TrialBalance);
            Dispatcher.UIThread.RunJobs();

            Assert.False(vm.Reports!.SupportsNarration);
            vm.Reports!.SetShowNarration(true);
            Assert.False(vm.Reports!.ShowNarration);
        }
        finally { window.Close(); }
    }
}
