using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Phase 10.11 S3 — Alt+X CANCELS A POSTED VOUCHER.</b> The engine half already existed and is untouched by
/// this slice (<c>LedgerService.Cancel</c> sets a flag; <c>LedgerBalances.CountsAsOf</c> and
/// <c>ItemInvoiceStock.Counts</c> already exclude cancelled vouchers, and all of that carries its own engine
/// tests). What ships here is the WIRING and the PRESENTATION, and that is what these prove — end to end, through
/// the REAL <see cref="MainWindow"/> tunnel handler, never by asserting that a binding exists.
///
/// <para><b>🔴 FIDELITY — read before changing any string or colour below (R7).</b> The source corpus states that
/// Alt+X cancels a voucher and says NOTHING about what cancelling means: the word returns two hits across the nine
/// admissible PDFs, one of them an EPF "cancelled cheque", and struck / strike-through return none. So
/// <b>retaining the number, the single (not double) confirmation, its wording, the greyed row and the "CANCELLED"
/// over-print are ALL OURS — UNVERIFIED-BY-DESIGN, corpus silent.</b> They are recorded as our decisions, and no
/// test here may be re-labelled as fidelity to any other product.</para>
///
/// <para><b>What each group locks.</b> (a) the arm and its four keyboard guards; (b) the confirmation channel,
/// including the disarm paths — a pending cancellation that outlived its prompt would let a plain "Y" on an
/// unrelated master screen void a voucher; (c) the Day Book presentation flag; (d) the TWO PICKER LEAKS that go
/// live the moment Cancel is reachable, because a cancelled invoice offered as the original supply of a §34 note
/// or as an advance's adjustment target writes a link to a document that is not on the books; (e) the print
/// over-print, which is the one that leaves the building.</para>
/// </summary>
public sealed class VoucherCancelAltXTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly InvoiceDate = new(2024, 6, 10);

    // ============================================================ harness

    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexCancelAltX_" + Guid.NewGuid().ToString("N"));
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

    private sealed record Kit(DomainLedger Cash, DomainLedger Capital, Voucher Receipt);

    /// <summary>
    /// A company carrying ONE posted Receipt (Dr Cash 50,000 / Cr Capital 50,000), created and accepted through
    /// the real screens so the voucher is genuinely posted and persisted — not hand-built.
    /// </summary>
    private static Kit SeedOneReceipt(MainWindow window, MainWindowViewModel vm, string name)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var on = vm.Company!.FinancialYearStart.AddDays(5);

        vm.ShowLedgerMaster();
        vm.LedgerMaster!.Name = "Capital A/c";
        vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Capital Account");
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        var capital = vm.Company!.FindLedgerByName("Capital A/c")!;
        var cash = vm.Company!.FindLedgerByName("Cash")!;

        vm.OpenVoucher(VoucherBaseType.Receipt);
        var e = vm.VoucherEntry!;
        e.Date = on;
        e.Lines[0].SelectedLedger = cash;
        e.Lines[0].Side = DrCr.Debit;
        e.Lines[0].AmountText = "50000";
        e.Lines[1].SelectedLedger = capital;
        e.Lines[1].Side = DrCr.Credit;
        e.Lines[1].AmountText = "50000";
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);   // accept → posted + persisted
        Assert.Single(vm.Company!.Vouchers);

        return new Kit(cash, capital, vm.Company!.Vouchers[0]);
    }

    /// <summary>A SECOND posted Receipt on a later date, so a test can prove Cancel acts on the voucher the
    /// confirmation NAMED and not merely on "whatever is highlighted now".</summary>
    private static Voucher PostAnotherReceipt(MainWindow window, MainWindowViewModel vm, Kit k, decimal amount)
    {
        vm.OpenVoucher(VoucherBaseType.Receipt);
        var e = vm.VoucherEntry!;
        e.Date = vm.Company!.FinancialYearStart.AddDays(9);
        e.Lines[0].SelectedLedger = k.Cash;
        e.Lines[0].Side = DrCr.Debit;
        e.Lines[0].AmountText = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        e.Lines[1].SelectedLedger = k.Capital;
        e.Lines[1].Side = DrCr.Credit;
        e.Lines[1].AmountText = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Assert.Equal(2, vm.Company!.Vouchers.Count);
        return vm.Company!.Vouchers.First(v => v.Id != k.Receipt.Id);
    }

    /// <summary>Opens the Day Book and highlights the row that drills to <paramref name="voucherId"/>.</summary>
    private static ReportRow OpenDayBookOn(MainWindow window, MainWindowViewModel vm, Guid voucherId)
    {
        vm.OpenReport(ReportKind.DayBook);
        Pump(window);
        var row = vm.Reports!.Rows.First(r => r.DrillVoucherId == voucherId);
        vm.Reports!.SelectedRow = row;
        return row;
    }

    // ============================================================ (a) the arm — the driving tests

    /// <summary>
    /// THE DRIVING TEST. Alt+X on a highlighted Day-Book voucher row raises ONE confirmation naming the document,
    /// and — the half that matters most — cancels NOTHING until it is answered. A destructive verb that acted on
    /// the keystroke would pass a weaker test that only checked the flag afterwards.
    /// </summary>
    [AvaloniaFact]
    public void AltX_on_a_day_book_voucher_row_raises_one_confirmation_and_cancels_nothing_yet()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel Prompt Co");
            OpenDayBookOn(window, vm, k.Receipt.Id);

            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);

            Assert.True(vm.IsAcceptPromptOpen);
            Assert.Contains("Cancel", vm.AcceptPromptText);
            Assert.Contains("Receipt", vm.AcceptPromptText);          // it names the document, not just "voucher"
            Assert.Contains("(Y/N)", vm.AcceptPromptText);
            Assert.False(vm.Company!.FindVoucher(k.Receipt.Id)!.Cancelled);   // nothing happened yet
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 THE DRIVING TEST FOR THE VERB ITSELF. "Y" cancels: the voucher is flagged, it KEEPS ITS NUMBER and its
    /// place in the Day Book, its amount leaves the books, the row greys, and the change is PERSISTED — the store
    /// is a snapshot, so a cancel that is not saved is a cancel that dies with the session.
    ///
    /// <para><b>UNVERIFIED-BY-DESIGN:</b> retaining the number is our decision, not a sourced behaviour.</para>
    /// </summary>
    [AvaloniaFact]
    public void Y_cancels_the_voucher_keeps_its_number_empties_the_books_and_persists()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel Y Co");
            var c = vm.Company!;
            var asOf = c.FinancialYearStart.AddYears(1);
            var numberBefore = k.Receipt.Number;
            Assert.Equal(50000m, LedgerBalances.SignedClosing(c, k.Cash, asOf));

            OpenDayBookOn(window, vm, k.Receipt.Id);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);

            var cancelled = c.FindVoucher(k.Receipt.Id)!;
            Assert.True(cancelled.Cancelled);
            Assert.Equal(numberBefore, cancelled.Number);                       // the number is KEPT
            Assert.Equal(0m, LedgerBalances.SignedClosing(c, k.Cash, asOf));     // and the books are empty again
            Assert.Equal(0m, LedgerBalances.SignedClosing(c, k.Capital, asOf));
            Assert.False(vm.IsAcceptPromptOpen);

            // The live report rebuilt itself: the row is still there, still drillable, and now flagged.
            var row = vm.Reports!.Rows.First(r => r.DrillVoucherId == k.Receipt.Id);
            Assert.True(row.IsCancelled);

            // …and it survives a reload, which is the only proof the snapshot was written.
            var storage = new CompanyStorage(dir);
            var reopened = storage.Load(storage.ListCompanies().Single(e => e.Name == c.Name));
            Assert.True(reopened.FindVoucher(k.Receipt.Id)!.Cancelled);
        }
        finally { Close(window, dir); }
    }

    /// <summary>"N" answers the question with a no: the voucher stays posted and the books are untouched.</summary>
    [AvaloniaFact]
    public void N_leaves_the_voucher_posted()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel N Co");
            OpenDayBookOn(window, vm, k.Receipt.Id);

            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.None);
            Pump(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.False(vm.Company!.FindVoucher(k.Receipt.Id)!.Cancelled);
            Assert.Equal(50000m, LedgerBalances.SignedClosing(
                vm.Company!, k.Cash, vm.Company!.FinancialYearStart.AddYears(1)));
        }
        finally { Close(window, dir); }
    }

    /// <summary>Escape is the same "no" — the documented dismissal of this confirmation channel.</summary>
    [AvaloniaFact]
    public void Escape_leaves_the_voucher_posted()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel Esc Co");
            OpenDayBookOn(window, vm, k.Receipt.Id);

            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.False(vm.Company!.FindVoucher(k.Receipt.Id)!.Cancelled);
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (b) the four keyboard guards

    /// <summary>GUARD — no Ctrl. Ctrl+Alt+X is a different chord and must not reach a destructive verb.</summary>
    [AvaloniaFact]
    public void CtrlAltX_raises_nothing()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel Ctrl Co");
            OpenDayBookOn(window, vm, k.Receipt.Id);

            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt | RawInputModifiers.Control);
            Pump(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.False(vm.Company!.FindVoucher(k.Receipt.Id)!.Cancelled);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// GUARD — report context. Off a report there is no highlighted voucher to act on, and Alt+X must be silent
    /// rather than reaching for whatever the shell last had selected.
    /// </summary>
    [AvaloniaFact]
    public void AltX_on_the_gateway_raises_nothing()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel Gateway Co");
            Assert.NotEqual(Screen.Report, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.False(vm.Company!.FindVoucher(k.Receipt.Id)!.Cancelled);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 GUARD — <c>vm.IsReportContext</c>, and this is the case that makes it load-bearing. Drilling a Day-Book
    /// row opens the read-only VoucherDetail column and leaves <c>Reports</c> BOUND BENEATH IT, highlighted row
    /// and all — so the view-model gate alone cannot tell the two screens apart, and only the arm's
    /// <c>IsReportContext</c> (false on <c>VoucherDetail</c> / <c>LedgerVouchers</c> by construction) keeps Alt+X
    /// out of a drill column.
    ///
    /// <para>Written after the first mutation run: deleting <c>IsReportContext</c> from the arm left the suite
    /// GREEN, because the Gateway test it was supposed to pin is satisfied by the "no report open" gate instead.
    /// A guard nothing pins is dead code that looks like safety.</para>
    ///
    /// <para>The scope itself is OURS, not fidelity: the corpus scopes Alt+X to "Vouchers &amp; Reports"; report
    /// rows only is our narrowing, taken because no voucher alteration screen exists yet.</para>
    /// </summary>
    [AvaloniaFact]
    public void AltX_in_a_drill_column_beneath_a_live_report_raises_nothing()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel Drill Co");
            OpenDayBookOn(window, vm, k.Receipt.Id);

            Assert.True(vm.DrillSelectedRow());
            Pump(window);
            Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen);
            Assert.NotNull(vm.Reports);                                   // the report is still bound beneath…
            Assert.Equal(k.Receipt.Id, vm.Reports!.SelectedRow!.DrillVoucherId);   // …with the SAME row highlighted
            Assert.False(vm.IsReportContext);                             // …but this is not report context

            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.False(vm.Company!.FindVoucher(k.Receipt.Id)!.Cancelled);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 THE BLOCKER CASE, AND THE ONE THE FIRST BUILD SHIPPED WRONG. The arm was gated on
    /// <c>vm.IsReportContext</c>, which is deliberately TRUE while a column is stacked OVER the report — that is
    /// what it was built for (the report-PARAMETER shortcuts must survive an F12 panel). With the Day Book row
    /// still highlighted underneath, Alt+X from inside FIVE different columns raised the confirmation for the
    /// voucher BEHIND the column the operator was standing in, and one Y voided it. The Print Preview was among
    /// them.
    ///
    /// <para>Each case asserts <c>IsReportContext</c> is still TRUE — i.e. the OLD gate would have let the key
    /// through — and that <c>IsLiveReportPage</c> is false, so the test names the mechanism rather than just the
    /// symptom. Then it presses Alt+X <b>and Y</b>: pressing only Alt+X would pass on a weaker fix that raised the
    /// prompt but failed to act.</para>
    ///
    /// <para>The positive control is the last case: the same key, the same row, the column popped — the prompt
    /// comes up. Without it this test would pass against an arm that never fires at all.</para>
    /// </summary>
    [AvaloniaFact]
    public void AltX_from_a_column_stacked_over_a_live_report_cancels_nothing()
    {
        var columns = new (string Name, Action<MainWindowViewModel> Open)[]
        {
            ("F12 report config",      vm => vm.OpenReportConfig()),
            ("Alt+F12 sort/filter",    vm => vm.OpenReportSortFilter()),
            ("Alt+A add-voucher",      vm => vm.OpenAddVoucherFromReport()),
            ("Alt+K saved views",      vm => vm.OpenSavedViews()),
            ("P print preview",        vm => vm.OpenPrintPreview()),
        };

        for (var i = 0; i < columns.Length; i++)
        {
            var (name, open) = columns[i];
            var (window, vm, dir) = NewWindow();
            try
            {
                var k = SeedOneReceipt(window, vm, $"Cancel Stack {i} Co");
                OpenDayBookOn(window, vm, k.Receipt.Id);
                open(vm);
                Pump(window);

                Assert.NotEqual(Screen.Report, vm.CurrentScreen);
                Assert.True(vm.IsReportContext, $"{name}: the old gate would not even have been reached");
                Assert.False(vm.IsLiveReportPage, $"{name}: the report is not the active column here");
                Assert.Equal(k.Receipt.Id, vm.Reports!.SelectedRow!.DrillVoucherId);   // still highlighted beneath

                window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
                Pump(window);
                window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
                Pump(window);

                Assert.False(vm.IsAcceptPromptOpen, $"{name}: a confirmation was raised");
                Assert.False(vm.Company!.FindVoucher(k.Receipt.Id)!.Cancelled, $"{name}: the voucher was CANCELLED");
            }
            finally { Close(window, dir); }
        }

        // POSITIVE CONTROL — pop the column and the same keystroke on the same row does raise the prompt.
        var (w2, vm2, dir2) = NewWindow();
        try
        {
            var k = SeedOneReceipt(w2, vm2, "Cancel Stack Control Co");
            OpenDayBookOn(w2, vm2, k.Receipt.Id);
            vm2.OpenReportConfig();
            Pump(w2);
            vm2.Back();
            Pump(w2);
            Assert.Equal(Screen.Report, vm2.CurrentScreen);
            Assert.True(vm2.IsLiveReportPage);

            w2.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(w2);
            Assert.True(vm2.IsAcceptPromptOpen);
        }
        finally { Close(w2, dir2); }
    }

    /// <summary>
    /// The F12 config column with a DROPDOWN UP is refused, and popping the column makes the same keystroke work.
    ///
    /// <para><b>What this test used to claim and no longer does.</b> It was written as the pin for
    /// <c>!IsPickerOpen</c>, with a positive control that shut the dropdown and expected the prompt to appear
    /// <i>while still on the config column</i> — i.e. it asserted the very behaviour the BLOCKER fix removed. The
    /// negative half was always right and is kept; the control now pops the column, which is what actually
    /// re-enables the verb. <c>!IsPickerOpen</c> is no longer independently pinnable (every picker that sits over a
    /// report lives in a column the screen gate refuses) and the arm's comment says so rather than pretending
    /// otherwise.</para>
    /// </summary>
    [AvaloniaFact]
    public void AltX_with_a_picker_open_over_a_report_raises_nothing()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel Picker Co");
            OpenDayBookOn(window, vm, k.Receipt.Id);
            vm.OpenReportConfig();
            Pump(window);

            var picker = Descendants(window).OfType<ComboBox>().FirstOrDefault(cb => cb.IsEffectivelyVisible);
            Assert.NotNull(picker);                       // otherwise this test proves nothing
            picker!.Focus();
            Pump(window);
            picker.IsDropDownOpen = true;
            Pump(window);
            Assert.True(picker.IsDropDownOpen);

            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.False(vm.Company!.FindVoucher(k.Receipt.Id)!.Cancelled);

            // POSITIVE CONTROL — dropdown shut AND the column popped: the prompt DOES come up on the report.
            picker.IsDropDownOpen = false;
            Pump(window);
            vm.Back();
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            Assert.True(vm.IsAcceptPromptOpen);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The same shape for a focused TEXT FIELD over a report. See the note on the picker test above: the negative
    /// half stands, and the positive control now pops the column rather than asserting the arm fires from inside
    /// one.
    /// </summary>
    [AvaloniaFact]
    public void AltX_while_typing_over_a_report_raises_nothing()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel Typing Co");
            OpenDayBookOn(window, vm, k.Receipt.Id);
            vm.OpenReportConfig();
            Pump(window);

            var box = Descendants(window).OfType<TextBox>().FirstOrDefault(t => t.IsEffectivelyVisible);
            Assert.NotNull(box);                          // otherwise this test proves nothing
            box!.Focus();
            Pump(window);

            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.False(vm.Company!.FindVoucher(k.Receipt.Id)!.Cancelled);

            // POSITIVE CONTROL — the column popped, the same key on the same row: the prompt comes up.
            vm.Back();
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            Assert.True(vm.IsAcceptPromptOpen);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// GUARD — <b>an EXACT modifier match</b>. <c>HasFlag(Alt)</c> plus a Ctrl exclusion also admitted
    /// <c>Alt+Shift+X</c> and <c>Alt+Win+X</c>, measured: both cancelled a posted voucher. That made the one
    /// destructive accelerator in the app the loosest match in the window's chain, against the project's own
    /// written doctrine for the bare-letter quick-jumps ("It deliberately excludes Shift as well … admitting Shift
    /// would leave the same class of hole open on the next chord anyone binds"). Nothing is shadowed today, which
    /// is exactly why it had to be closed before something is.
    /// <para>The positive control at the end is plain Alt+X on the same row.</para>
    /// </summary>
    [AvaloniaFact]
    public void AltX_with_any_extra_modifier_raises_nothing()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel Modifier Co");
            OpenDayBookOn(window, vm, k.Receipt.Id);

            foreach (var extra in new[] { RawInputModifiers.Shift, RawInputModifiers.Meta })
            {
                window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt | extra);
                Pump(window);
                window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
                Pump(window);
                Assert.False(vm.IsAcceptPromptOpen, $"Alt+{extra}+X raised a confirmation");
                Assert.False(vm.Company!.FindVoucher(k.Receipt.Id)!.Cancelled, $"Alt+{extra}+X CANCELLED the voucher");
            }

            // POSITIVE CONTROL — bare Alt+X, same row, same screen.
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            Assert.True(vm.IsAcceptPromptOpen);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The Alt+X keystroke comes back <c>Handled</c> on a report row, and NOT handled where the arm declines.
    ///
    /// <para>Why it needs its own test: <c>e.Handled = true</c> survived its own deletion against the whole
    /// Desktop suite, and the comment above it justified the flag as stopping "a fall-through to some later Alt
    /// arm" — which the <c>return</c> on the next line already does unaided. The flag's real job is that the
    /// keystroke does not bubble on past the window handler to a control beneath, and nothing said or pinned that.
    /// The negative case is what makes the positive one mean something.</para>
    /// </summary>
    [AvaloniaFact]
    public void AltX_on_a_report_row_comes_back_Handled()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            // Registered for BOTH phases: a Tunnel-only observer on the same element does not see the flag the
            // window's own tunnel handler set (measured — it reported not-handled), so the bubble leg is what
            // actually reads the outcome. `handledEventsToo` is mandatory either way, or the observer is skipped.
            bool? handled = null;
            window.AddHandler(
                InputElement.KeyDownEvent,
                (object? _, KeyEventArgs e) => { if (e.Key == Key.X) handled = e.Handled; },
                Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble,
                handledEventsToo: true);

            var k = SeedOneReceipt(window, vm, "Cancel Handled Co");

            // NEGATIVE — on the Gateway the arm declines, so nothing consumes the key.
            handled = null;
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            Assert.False(handled ?? true, "Alt+X off a report should not be consumed by this arm");

            // POSITIVE — on the Day Book row it is consumed.
            OpenDayBookOn(window, vm, k.Receipt.Id);
            handled = null;
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            Assert.True(vm.IsAcceptPromptOpen);
            Assert.True(handled ?? false, "Alt+X on a report row must come back Handled");
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (c) the view-model gates

    /// <summary>
    /// GUARD — the highlighted row must carry a voucher. A header / total / empty-state row has
    /// <see cref="ReportRow.DrillVoucherId"/> of <see cref="Guid.Empty"/>, and the engine must never be called
    /// with it.
    /// </summary>
    [AvaloniaFact]
    public void AltX_on_a_row_that_carries_no_voucher_raises_nothing()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel NoRow Co");
            vm.OpenReport(ReportKind.DayBook);
            Pump(window);
            vm.Reports!.SelectedRow = new ReportRow { Particulars = "a header", IsHeader = true };

            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.False(vm.Company!.FindVoucher(k.Receipt.Id)!.Cancelled);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// GUARD — an already-cancelled voucher is REFUSED with a named message, not re-asked. Re-cancelling would be
    /// harmless to the books, which is exactly why a silent second prompt is the wrong answer: a confirmation
    /// whose Yes changes nothing teaches the operator to answer without reading.
    /// </summary>
    [AvaloniaFact]
    public void AltX_on_an_already_cancelled_voucher_refuses_with_a_named_message()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel Twice Co");
            OpenDayBookOn(window, vm, k.Receipt.Id);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);
            Assert.True(vm.Company!.FindVoucher(k.Receipt.Id)!.Cancelled);

            OpenDayBookOn(window, vm, k.Receipt.Id);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.Contains("already cancelled", vm.Message);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 GUARD — <c>if (IsAcceptPromptOpen) return false;</c>. A second Alt+X while the confirmation is up must
    /// not RE-POINT the question. The highlight keeps moving while a prompt is open (arrows fall straight through
    /// the confirmation arm, by design), so without this gate the sequence "ask about A → arrow to B → Alt+X out
    /// of habit → Y" silently voids B while the operator believes they answered the question they read about A.
    /// <b>The whole point is that the ARMED VOUCHER is A, not merely that a prompt is still open</b>, which is why
    /// this test moves the selection between the two presses and asserts on which voucher actually died.
    ///
    /// <para>Written after a mutation run: the first version pressed Alt+X twice on the SAME row, and deleting
    /// the gate left it green — re-arming the same id with the same text is indistinguishable from not
    /// re-arming.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_second_AltX_does_not_repoint_the_open_confirmation_at_a_different_voucher()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel Stack Co");
            var second = PostAnotherReceipt(window, vm, k, 7000m);
            OpenDayBookOn(window, vm, k.Receipt.Id);

            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            var textAboutTheFirst = vm.AcceptPromptText;
            Assert.True(vm.IsAcceptPromptOpen);

            // The operator moves the highlight to the OTHER voucher and presses Alt+X again.
            vm.Reports!.SelectedRow = vm.Reports!.Rows.First(r => r.DrillVoucherId == second.Id);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);

            Assert.True(vm.IsAcceptPromptOpen);
            Assert.Equal(textAboutTheFirst, vm.AcceptPromptText);   // the question did NOT change under them

            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);

            Assert.True(vm.Company!.FindVoucher(k.Receipt.Id)!.Cancelled);    // the one the question named
            Assert.False(vm.Company!.FindVoucher(second.Id)!.Cancelled);      // and ONLY that one
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 ANSWERING "NO" DISARMS THE CHANNEL. The cancellation and the master Accept share one prompt, so a
    /// pending cancellation that outlived its own question would be executed by the next plain "Y" the operator
    /// pressed on an unrelated master screen: they would accept a ledger and void a voucher.
    ///
    /// <para>This and the navigate-away test below both come to rest on the SAME line — the disarm inside
    /// <c>ResetMasterAcceptPrompt</c>, which every exit now routes through — and deleting it turns both of them
    /// red. Written as two tests because they are two operator journeys, not because they pin two mechanisms.</para>
    /// </summary>
    [AvaloniaFact]
    public void Answering_No_disarms_the_channel_so_a_later_master_accept_cancels_nothing()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel Disarm Co");
            OpenDayBookOn(window, vm, k.Receipt.Id);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.None);
            Pump(window);

            // Now go and accept an ordinary master through the SAME Y.
            vm.ShowLedgerMaster();
            vm.LedgerMaster!.Name = "Nalanda Papers";
            vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Sundry Debtors");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.True(vm.IsAcceptPromptOpen);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);

            Assert.NotNull(vm.Company!.FindLedgerByName("Nalanda Papers"));   // the master DID save…
            Assert.False(vm.Company!.FindVoucher(k.Receipt.Id)!.Cancelled);   // …and the voucher did NOT die
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 GUARD — NAVIGATING AWAY DISARMS IT TOO. Answering is only one way to leave a confirmation; the teardown
    /// choke point has to clear the armed voucher as well, or the same cross-wiring returns by the other door.
    /// </summary>
    [AvaloniaFact]
    public void Navigating_away_disarms_the_channel_so_a_later_master_accept_cancels_nothing()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel Nav Co");
            OpenDayBookOn(window, vm, k.Receipt.Id);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            Assert.True(vm.IsAcceptPromptOpen);

            vm.ShowLedgerMaster();              // navigate away WITHOUT answering
            Pump(window);
            Assert.False(vm.IsAcceptPromptOpen);

            vm.LedgerMaster!.Name = "Kalyan Stationers";
            vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Sundry Debtors");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);

            Assert.NotNull(vm.Company!.FindLedgerByName("Kalyan Stationers"));
            Assert.False(vm.Company!.FindVoucher(k.Receipt.Id)!.Cancelled);
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (d) presentation — the greyed row

    /// <summary>
    /// The Day Book carries <see cref="ReportRow.IsCancelled"/>, which is what the muted foreground binds to. A
    /// live row is false, and so is every row of a report that has no notion of cancellation — the flag must not
    /// leak a grey row into the Trial Balance.
    ///
    /// <para><b>UNVERIFIED-BY-DESIGN:</b> greying is our presentation choice; the corpus attests none.</para>
    /// </summary>
    [AvaloniaFact]
    public void Only_the_cancelled_day_book_row_is_flagged()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel Grey Co");
            OpenDayBookOn(window, vm, k.Receipt.Id);
            Assert.All(vm.Reports!.Rows, r => Assert.False(r.IsCancelled));   // before: nothing is flagged

            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);

            var row = vm.Reports!.Rows.First(r => r.DrillVoucherId == k.Receipt.Id);
            Assert.True(row.IsCancelled);
            Assert.Contains("(Cancelled)", row.Secondary);      // colour is never the only carrier

            vm.OpenReport(ReportKind.TrialBalance);
            Pump(window);
            Assert.All(vm.Reports!.Rows, r => Assert.False(r.IsCancelled));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 THE GREY ACTUALLY REACHES THE SCREEN. The test above pins the view-model flag; this one pins the two
    /// things between that flag and the operator's eye — the <c>CancelledRowToBrushConverter</c> and the
    /// <c>Foreground</c> binding in the accounting-grid row template. Without it the flag could be set perfectly
    /// and every row would still render in the same ink, with the suite green.
    ///
    /// <para>Asserts a DIFFERENCE between the cancelled row's brush and a live row's, not a hard-coded hex: the
    /// palette is a design decision that may move, the "a voided row does not look like a live one" contract is
    /// not.</para>
    ///
    /// <para><b>UNVERIFIED-BY-DESIGN:</b> the greying is ours; the corpus attests no visual treatment.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_cancelled_day_book_row_renders_in_a_different_ink_from_a_live_one()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel Ink Co");
            var second = PostAnotherReceipt(window, vm, k, 7000m);

            OpenDayBookOn(window, vm, k.Receipt.Id);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);
            Assert.True(vm.Company!.FindVoucher(k.Receipt.Id)!.Cancelled);
            Assert.False(vm.Company!.FindVoucher(second.Id)!.Cancelled);

            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            // Each realised accounting row is the "*,150,150" grid. 🔴 BOTH VISIBLE CELLS ARE CHECKED: the row
            // template carries FOUR `Foreground` bindings to the converter (Particulars, Debit, Credit, Amount) and
            // this test originally selected `Grid.GetColumn(c) == 0` only — so deleting the AMOUNT cell's binding
            // left all 2220 Desktop tests green while a cancelled ₹50,000 rendered in exactly the same ink as a
            // live one. Debit/Credit are unreachable by data rather than merely unpinned (`BuildDayBook` sets
            // `IsTwoColumn = false` and no other report ever sets `IsCancelled`), so they are named here instead of
            // being silently counted as covered.
            var rows = window.GetVisualDescendants()
                .OfType<ListBoxItem>()
                .SelectMany(item => item.GetVisualDescendants().OfType<Grid>())
                .Where(g => g.ColumnDefinitions.Count == 3
                            && g.ColumnDefinitions[0].Width.IsStar
                            && g.ColumnDefinitions[1].Width.IsAbsolute
                            && g.ColumnDefinitions[2].Width.IsAbsolute)
                .Select(g => (Row: (g.DataContext as ReportRow), Grid: g))
                .Where(x => x.Row is not null)
                .ToList();

            Assert.True(rows.Count >= 2, $"only {rows.Count} Day-Book rows realised — this test proves nothing");

            static TextBlock CellAt(Grid g, int column) =>
                Assert.IsType<TextBlock>(g.Children
                    .First(c => Grid.GetColumn(c) == column && c.IsEffectivelyVisible));

            var cancelledGrid = rows.Single(x => x.Row!.DrillVoucherId == k.Receipt.Id).Grid;
            var liveGrid = rows.Single(x => x.Row!.DrillVoucherId == second.Id).Grid;

            foreach (var (column, what) in new[] { (0, "Particulars"), (2, "Amount") })
            {
                var cancelledInk = CellAt(cancelledGrid, column).Foreground;
                var liveInk = CellAt(liveGrid, column).Foreground;

                Assert.NotNull(cancelledInk);
                Assert.NotNull(liveInk);
                Assert.NotEqual(liveInk!.ToString(), cancelledInk!.ToString());
                Assert.False(string.IsNullOrEmpty(cancelledInk.ToString()), $"{what}: no brush at all");
            }
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (e) the two picker leaks

    private sealed record GstKit(MainWindowViewModel Vm, Voucher Invoice, DomainLedger Customer, DomainLedger Sales);

    /// <summary>A Regular-GST company with one posted Sales invoice — the supply a §34 note or an advance points at.</summary>
    private static GstKit GstCompanyWithInvoice(string dir, string name)
    {
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var customer = new DomainLedger(Guid.NewGuid(), "Acme Ltd",
            c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true);
        c.AddLedger(customer);
        var sales = c.FindLedgerByName("Sales");
        if (sales is null)
        {
            sales = new DomainLedger(Guid.NewGuid(), "Sales",
                c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, false);
            c.AddLedger(sales);
        }

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var invoice = new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), salesType, InvoiceDate,
            new[]
            {
                new EntryLine(customer.Id, Money.FromRupees(11800m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(11800m), DrCr.Credit),
            },
            partyId: customer.Id));

        return new GstKit(vm, invoice, customer, sales);
    }

    /// <summary>
    /// 🔴 LEAK 1. <c>BuildSection34Pickers</c> filtered on base type ALONE, so a CANCELLED invoice was offered as
    /// the original supply a §34 credit note adjusts — a note netting a document that is not on the books, filed
    /// against a number the recipient can never match. Latent only while nothing could cancel; live the moment
    /// this slice ships, which is why it closes here.
    /// </summary>
    [Fact]
    public void A_cancelled_invoice_is_not_offered_as_the_section34_original()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexCancelCdn_" + Guid.NewGuid().ToString("N"));
        try
        {
            var k = GstCompanyWithInvoice(dir, "Cdn Leak Co");
            var vm = k.Vm;

            // It IS offered while the invoice is live — the control that makes the next assertion mean something.
            vm.OpenVoucher(VoucherBaseType.CreditNote);
            Assert.Contains(vm.VoucherEntry!.CdnOriginalInvoices, o => o.Invoice?.Id == k.Invoice.Id);
            vm.AbandonEntry();

            new LedgerService(vm.Company!).Cancel(k.Invoice.Id);

            vm.OpenVoucher(VoucherBaseType.CreditNote);
            Assert.DoesNotContain(vm.VoucherEntry!.CdnOriginalInvoices, o => o.Invoice?.Id == k.Invoice.Id);
        }
        finally { TryDelete(dir); }
    }

    /// <summary>
    /// 🔴 LEAK 2, the mirror. <c>BuildAdvancePickers</c> had the same base-type-only filter, so an outstanding
    /// advance could be adjusted against a cancelled sale: real advance tax retired against a supply with no
    /// value, and the advance marked settled by an invoice that is not on the books.
    /// </summary>
    [Fact]
    public void A_cancelled_invoice_is_not_offered_as_the_advance_adjustment_target()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexCancelAdv_" + Guid.NewGuid().ToString("N"));
        try
        {
            var k = GstCompanyWithInvoice(dir, "Advance Leak Co");
            var vm = k.Vm;

            vm.OpenVoucher(VoucherBaseType.Journal);          // Journal ⇒ AdvanceAction.Adjust
            Assert.Contains(vm.VoucherEntry!.AdvanceInvoices, o => o.Invoice?.Id == k.Invoice.Id);
            vm.AbandonEntry();

            new LedgerService(vm.Company!).Cancel(k.Invoice.Id);

            vm.OpenVoucher(VoucherBaseType.Journal);
            Assert.DoesNotContain(vm.VoucherEntry!.AdvanceInvoices, o => o.Invoice?.Id == k.Invoice.Id);
        }
        finally { TryDelete(dir); }
    }

    /// <summary>
    /// 🔴 LEAK 3 — <b>inside the same method as leak 2, and the first build closed only one of its two lists.</b>
    /// <c>BuildAdvancePickers</c> populates <c>AdvanceInvoices</c> AND <c>OutstandingAdvances</c>; only the former
    /// got the cancelled filter. So an advance whose BOOKING RECEIPT had been cancelled was still offered for
    /// adjustment (Journal) and for refund (Payment). <c>LedgerService.Cancel</c> sets the voucher flag and nothing
    /// else, so the <c>GstAdvanceReceipt</c> survives untouched: acting on it releases suspense the books no longer
    /// recognise (the receipt's <c>Cr Output {head}</c> / <c>Dr Output Tax on Advances</c> pair is off the books)
    /// and marks the advance settled from a voucher with zero effect.
    /// <para>Both routes are asserted, because <c>AdvanceActionForType</c> maps Journal ⇒ Adjust and Payment ⇒
    /// Refund and each rebuilds the picker independently.</para>
    /// </summary>
    [Fact]
    public void A_cancelled_receipt_is_not_offered_as_an_outstanding_advance_on_either_route()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexCancelOutAdv_" + Guid.NewGuid().ToString("N"));
        try
        {
            var k = GstCompanyWithInvoice(dir, "Outstanding Advance Co");
            var vm = k.Vm;
            var c = vm.Company!;

            // Book an advance against a REAL Receipt voucher, the way the advance panel does.
            var cash = c.FindLedgerByName("Cash")!;
            var receiptType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Receipt).Id;
            var receipt = new LedgerService(c).Post(new Voucher(
                Guid.NewGuid(), receiptType, InvoiceDate,
                new[]
                {
                    new EntryLine(cash.Id, Money.FromRupees(11800m), DrCr.Debit),
                    new EntryLine(k.Customer.Id, Money.FromRupees(11800m), DrCr.Credit),
                },
                partyId: k.Customer.Id));
            new AdvanceReceiptService(c).BuildAdvanceReceipt(
                receipt.Id, isService: true, Money.FromRupees(10000m), rateBasisPoints: 1800, interState: true);
            var advanceId = c.AdvanceReceipts.Single().Id;

            // CONTROLS — offered on both routes while the receipt is live.
            foreach (var type in new[] { VoucherBaseType.Journal, VoucherBaseType.Payment })
            {
                vm.OpenVoucher(type);
                Assert.Contains(vm.VoucherEntry!.OutstandingAdvances, o => o.Receipt?.Id == advanceId);
                vm.AbandonEntry();
            }

            new LedgerService(c).Cancel(receipt.Id);
            Assert.True(c.FindVoucher(receipt.Id)!.Cancelled);
            Assert.Single(c.AdvanceReceipts);                 // the record itself survives — that is the trap

            foreach (var type in new[] { VoucherBaseType.Journal, VoucherBaseType.Payment })
            {
                vm.OpenVoucher(type);
                Assert.DoesNotContain(vm.VoucherEntry!.OutstandingAdvances, o => o.Receipt?.Id == advanceId);
                vm.AbandonEntry();
            }
        }
        finally { TryDelete(dir); }
    }

    // ============================================================ (g) the live-IRN / live-EWB refusal

    /// <summary>Rehydrates a <c>Generated</c> e-invoice record onto <paramref name="voucherId"/> — the state a book
    /// reopened after an IRP round-trip is in. Rehydration is used rather than the prepare/record flow because the
    /// guard under test reads the RECORD's status and nothing about how it got there.</summary>
    private static EInvoiceRecord AttachLiveIrn(Company c, Guid voucherId, DateOnly ackDate)
    {
        var record = EInvoiceRecord.Rehydrate(
            Guid.NewGuid(), voucherId, "RCPT-1", EInvoiceStatus.Generated,
            irn: new string('a', 64), ackNo: "112210000123456", ackDate: ackDate,
            signedQr: "eyJhbGciOi", signedJson: Array.Empty<byte>(),
            cancelledOn: null, cancelReasonCode: null);
        c.AddEInvoiceRecord(record);
        return record;
    }

    /// <summary>
    /// 🔴 CANCELLING A VOUCHER THAT STILL HOLDS A LIVE IRN IS REFUSED — because doing it is a ONE-WAY DOOR.
    /// <c>LedgerService.Cancel</c> touches only the flag, so the <c>Generated</c> record keeps its IRP-issued IRN;
    /// but <c>GenerateEInvoiceViewModel.Rebuild()</c> lists <c>Vouchers.Where(v =&gt; !v.Cancelled)</c> and is the
    /// only writer of the rows the IRP-cancel action reads, so the moment the voucher is cancelled locally the IRN
    /// can never be cancelled from the app again. The design named this as one of Cancel's three open questions
    /// (§3.5 — presentational, referential, statutory) and the first build answered two of them.
    ///
    /// <para><b>The refusal is an ORDER, not a dead end, and the positive control proves it:</b> cancel the IRN at
    /// the portal first — the record moves to <c>Cancelled</c> — and the same Alt+X then works.</para>
    /// </summary>
    [AvaloniaFact]
    public void AltX_refuses_a_voucher_holding_a_live_IRN_and_allows_it_once_the_IRN_is_cancelled()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel Irn Co");
            var c = vm.Company!;
            var record = AttachLiveIrn(c, k.Receipt.Id, k.Receipt.Date);

            OpenDayBookOn(window, vm, k.Receipt.Id);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.False(c.FindVoucher(k.Receipt.Id)!.Cancelled);
            Assert.Contains("live IRN", vm.Notice);
            Assert.Contains("portal", vm.Notice);

            // POSITIVE CONTROL — the IRP cancellation happens first (still reachable: the voucher is still live and
            // therefore still listed on that screen), and then the voucher can be cancelled.
            new EInvoiceService(c).Cancel(record, k.Receipt.Date, reasonCode: "1");
            Assert.Equal(EInvoiceStatus.Cancelled, record.Status);

            OpenDayBookOn(window, vm, k.Receipt.Id);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            Assert.True(vm.IsAcceptPromptOpen);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);
            Assert.True(c.FindVoucher(k.Receipt.Id)!.Cancelled);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The e-Way Bill half of the same guard, pinned separately because it is a separate record collection and a
    /// separate arm of the blocker switch — <c>GenerateEWayBillViewModel.Rebuild()</c> carries the identical
    /// <c>!v.Cancelled</c> filter and therefore the identical trap.
    /// </summary>
    [AvaloniaFact]
    public void AltX_refuses_a_voucher_holding_a_live_e_way_bill()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel Ewb Co");
            var c = vm.Company!;
            c.AddEWayBillRecord(EWayBillRecord.Rehydrate(
                Guid.NewGuid(), k.Receipt.Id, "RCPT-1", EWayStatus.Generated,
                supplyType: "O", subSupplyType: "1", docType: "INV", consignmentValuePaisa: 5_000_000,
                transporterId: null, mode: null, vehicleNumber: null, distanceKm: 10, transportDocNo: null,
                shipFromStateCode: "27", shipToStateCode: "27", isOverDimensionalCargo: false,
                shipToGstin: null, closureRequested: false, closedOn: null,
                ewbNumber: "123456789012",
                generatedAt: new DateTimeOffset(2026, 4, 6, 10, 0, 0, TimeSpan.Zero),
                validUpto: new DateTimeOffset(2026, 4, 8, 10, 0, 0, TimeSpan.Zero),
                cancelledOn: null, cancelReasonCode: null));

            OpenDayBookOn(window, vm, k.Receipt.Id);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.False(c.FindVoucher(k.Receipt.Id)!.Cancelled);
            Assert.Contains("live e-Way Bill", vm.Notice);
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (h) the verb has to be VISIBLE, and TRUE

    /// <summary>
    /// 🔴 EVERY OUTCOME OF THE VERB REACHES A SURFACE THE REPORT PAGE ACTUALLY RENDERS.
    ///
    /// <para>The refusals and the success message were written to <c>MainWindowViewModel.Message</c>, and the report
    /// page <b>cannot render it</b>: that <c>DataTemplate</c> is typed <c>x:DataType="vm:ReportsViewModel"</c>,
    /// which has no <c>Message</c> property at all, and every <c>{Binding Message}</c> in the window sits in a
    /// master/entry/action panel. Alt+X works on exactly one screen — that one. So "already cancelled" was
    /// indistinguishable from a dead key, and a FAILED cancel was silent.</para>
    ///
    /// <para>This asserts the REALISED VISUAL, not the property: a visible TextBlock somewhere in the window
    /// carrying the text while the report is the active page. Binding to a property no template shows is exactly
    /// the defect being fixed, so a property-only assertion would not have caught it.</para>
    /// </summary>
    [AvaloniaFact]
    public void Every_cancel_outcome_is_rendered_while_the_report_is_the_active_page()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel Notice Co");

            bool ShowsOnScreen(string fragment) =>
                Descendants(window).OfType<TextBlock>()
                    .Any(t => t.IsEffectivelyVisible && (t.Text ?? string.Empty).Contains(fragment));

            // (1) the SUCCESS outcome.
            OpenDayBookOn(window, vm, k.Receipt.Id);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.True(ShowsOnScreen("cancelled"), $"success not on screen; Notice='{vm.Notice}'");

            // (2) the REFUSAL outcome, on the same screen.
            OpenDayBookOn(window, vm, k.Receipt.Id);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            Assert.False(vm.IsAcceptPromptOpen);
            Assert.True(ShowsOnScreen("already cancelled"), $"refusal not on screen; Notice='{vm.Notice}'");

            // …and it does not follow the operator off the screen it was raised on.
            vm.ShowGateway();
            Pump(window);
            Assert.False(ShowsOnScreen("already cancelled"));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 A SAVE THAT FAILS LEAVES THE VOUCHER POSTED — the aggregate must never end up ahead of the store.
    ///
    /// <para>The engine mutates in memory and the save follows, so a throwing save used to leave the voucher
    /// cancelled in memory with nothing on disk, the report un-rebuilt and the row still black. Worse, the catch
    /// was <c>InvalidOperationException</c> while the one genuinely reachable failure —
    /// <c>CompanyStorage.Save</c> → <c>Company.EnsureValid()</c> — throws <b>ArgumentException</b>, so it was not
    /// caught at all: an unhandled exception out of the window's key handler, voucher already flagged.</para>
    ///
    /// <para>The failure is provoked the way the product itself can reach it: a company PIN that
    /// <c>EnsureValid</c> refuses. Its own doc records that such a book "loads without complaint … and then the
    /// next save on any screen throws".</para>
    /// </summary>
    [AvaloniaFact]
    public void A_failed_save_rolls_the_cancellation_back_and_says_so()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel SaveFail Co");
            var c = vm.Company!;
            c.Pin = "12";                                  // EnsureValid refuses this on the next save

            OpenDayBookOn(window, vm, k.Receipt.Id);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            Assert.True(vm.IsAcceptPromptOpen);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);   // must not throw out of the handler
            Pump(window);

            Assert.False(c.FindVoucher(k.Receipt.Id)!.Cancelled);           // rolled back
            Assert.Contains("Cannot cancel", vm.Notice);
            Assert.Equal(50000m, LedgerBalances.SignedClosing(
                c, k.Cash, c.FinancialYearStart.AddYears(1)));              // and the books never moved

            // POSITIVE CONTROL — fix the PIN and the same keystrokes do cancel, so the test is not passing because
            // the verb is broken outright.
            c.Pin = "400001";
            OpenDayBookOn(window, vm, k.Receipt.Id);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);
            Assert.True(c.FindVoucher(k.Receipt.Id)!.Cancelled);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 THE PROMPT TELLS THE TRUTH ABOUT THE BOOKS, and the wording and the behaviour are asserted in the SAME
    /// case so they cannot drift apart again.
    ///
    /// <para>The confirmation gating this irreversible verb used to read "The number is kept; the books are
    /// unaffected." The second clause is the exact opposite of what cancelling does, and it is inverted from the
    /// design's own driving test T-5 ("every balance moves by exactly the invoice"). With no un-cancel
    /// (ORCHESTRATOR RULING 3) and no alteration route yet (S5), an operator who read it as "my figures will not
    /// move" and pressed Y had no way back. Nothing asserted the wording, so nothing checked whether it was true.
    /// </para>
    ///
    /// <para><b>UNVERIFIED-BY-DESIGN:</b> the wording is ours — the corpus describes no cancellation confirmation.
    /// What is fixed here is not fidelity, it is that OUR sentence was false.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_prompt_tells_the_truth_about_the_books()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel Truth Co");
            var c = vm.Company!;
            var asOf = c.FinancialYearStart.AddYears(1);
            Assert.Equal(50000m, LedgerBalances.SignedClosing(c, k.Cash, asOf));

            OpenDayBookOn(window, vm, k.Receipt.Id);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);

            var prompt = vm.AcceptPromptText;
            Assert.DoesNotContain("books are unaffected", prompt);
            Assert.Contains("number is kept", prompt);
            Assert.Contains("every balance it touched will move", prompt);
            Assert.Contains("cannot be undone", prompt);

            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);

            // The behaviour the sentence now describes — measured, in the same test.
            Assert.Equal(0m, LedgerBalances.SignedClosing(c, k.Cash, asOf));
            Assert.Equal(0m, LedgerBalances.SignedClosing(c, k.Capital, asOf));
            Assert.DoesNotContain("books are unaffected", vm.Notice);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// A cancelled Day Book row keeps its FULL MAGNITUDE and therefore survives an RQ-3 amount range filter.
    ///
    /// <para>The comment governing that filter used to assert the opposite — "cancelled rows carry
    /// <c>Money.Zero</c> so a positive range filter naturally excludes them" — and nothing zeroes a cancelled
    /// row: <c>DayBook.Build</c> passes <c>v.TotalDebit</c> regardless of <c>v.Cancelled</c>, and
    /// <c>Voucher.TotalDebit</c> sums debit-line magnitudes with no cancelled test. The claim was untestable
    /// until this slice made a voucher cancellable at all. Keeping the row is the RIGHT behaviour — the Day Book
    /// lists cancelled vouchers by design, and a row that vanished from a filtered view but not the unfiltered one
    /// would be worse — so this test locks the behaviour the comment now describes, rather than the comment being
    /// left to describe whatever the code happens to do.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_cancelled_row_keeps_its_magnitude_and_survives_an_amount_range_filter()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel Range Co");          // 50,000
            var small = PostAnotherReceipt(window, vm, k, 7000m);

            OpenDayBookOn(window, vm, k.Receipt.Id);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);
            Assert.True(vm.Company!.FindVoucher(k.Receipt.Id)!.Cancelled);

            vm.OpenReportSortFilter();
            Pump(window);
            vm.ReportSortFilter!.MinText = "15000";
            vm.ReportSortFilter!.MaxText = "100000";
            vm.ApplyReportSortFilter();
            Pump(window);

            // The cancelled 50,000 is IN the range and stays; the live 7,000 is out of it and goes.
            Assert.Contains(vm.Reports!.Rows, r => r.DrillVoucherId == k.Receipt.Id && r.IsCancelled);
            Assert.DoesNotContain(vm.Reports!.Rows, r => r.DrillVoucherId == small.Id);
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (i) report EGRESS — print / PDF / CSV / XLSX

    /// <summary>
    /// 🔴 THE CANCELLED FACT LEAVES THE BUILDING WITH THE REPORT, not just with the voucher.
    ///
    /// <para>On screen a cancelled Day Book row carries the fact two ways — the muted ink and the "(Cancelled)"
    /// tag in <c>ReportRow.Secondary</c>. Both report projectors emitted <c>Particulars</c> + <c>Amount</c> and
    /// nothing else: <c>Secondary</c> has no cell to land in and ink does not survive a projection. So the printed,
    /// print-previewed, PDF-exported, CSV/XLSX-exported and emailed Day Book rendered a cancelled ₹50,000 receipt
    /// byte-for-byte identically to a live one — full amount, no marker anywhere. The slice added the "CANCELLED"
    /// over-print to the voucher/invoice DTOs because that is "the one that leaves the building"; this egress
    /// leaves the building too and got nothing.</para>
    ///
    /// <para>Both projectors are asserted separately: <c>ReportPrintProjector</c> feeds print / preview / PDF,
    /// <c>ReportTabularProjector</c> feeds CSV / XLSX / the emailed workbook. The live row is asserted CLEAN in the
    /// same pass, so a projector that stamped every row would fail.</para>
    /// </summary>
    [AvaloniaFact]
    public void Both_report_projectors_carry_the_cancelled_marker_out_of_the_screen()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel Egress Co");
            var second = PostAnotherReceipt(window, vm, k, 7000m);

            OpenDayBookOn(window, vm, k.Receipt.Id);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);

            var reports = vm.Reports!;
            Assert.True(reports.IsAccountingReport);
            var cancelledRow = reports.Rows.First(r => r.DrillVoucherId == k.Receipt.Id);
            var liveRow = reports.Rows.First(r => r.DrillVoucherId == second.Id);
            Assert.True(cancelledRow.IsCancelled);
            Assert.False(liveRow.IsCancelled);

            // (1) the PRINT projection — what print, print preview and the PDF are built from.
            var print = ReportPrintProjector.Project(reports);
            var printCancelled = print.Rows.Single(r => r.Cells[0].Contains(cancelledRow.Particulars));
            var printLive = print.Rows.Single(r => r.Cells[0].Contains(liveRow.Particulars));
            Assert.Contains("(Cancelled)", printCancelled.Cells[0]);
            Assert.DoesNotContain("(Cancelled)", printLive.Cells[0]);
            Assert.Contains("50,000.00", printCancelled.Cells[^1]);       // the figure still prints, as it must

            // (2) the TABULAR projection — what CSV, XLSX and the emailed workbook are built from.
            var tab = ReportTabularProjector.Project(reports);
            var tabCancelled = tab.Rows.Single(r => r.Cells[0].TextValue.Contains(cancelledRow.Particulars));
            var tabLive = tab.Rows.Single(r => r.Cells[0].TextValue.Contains(liveRow.Particulars));
            Assert.Contains("(Cancelled)", tabCancelled.Cells[0].TextValue);
            Assert.DoesNotContain("(Cancelled)", tabLive.Cells[0].TextValue);
            Assert.True(tabCancelled.Cells[^1].HasNumber);                // still a real Number, so exports sum
            Assert.Equal(50000m, tabCancelled.Cells[^1].NumberValue);
        }
        finally { Close(window, dir); }
    }

    // ============================================================ (f) print — the CANCELLED over-print

    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    /// <summary>
    /// The projector reads the posted flag and the renderer over-prints CANCELLED. Both halves are asserted from
    /// a REAL posted voucher, and the live case is asserted too — the over-print must not appear on a document
    /// that was never cancelled, or every voucher in the company reads as void.
    ///
    /// <para><b>UNVERIFIED-BY-DESIGN:</b> the over-print and its wording are ours; the corpus attests none.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_cancelled_voucher_prints_the_CANCELLED_overprint_and_a_live_one_does_not()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel Print Co");
            var c = vm.Company!;

            var live = VoucherPrintProjector.ProjectVoucher(c, c.FindVoucher(k.Receipt.Id)!);
            Assert.False(live.IsCancelled);
            Assert.DoesNotContain("CANCELLED", AsLatin1(VoucherPdf.Render(live, new PrintConfig(), new PageConfig())));

            new LedgerService(c).Cancel(k.Receipt.Id);

            var voided = VoucherPrintProjector.ProjectVoucher(c, c.FindVoucher(k.Receipt.Id)!);
            Assert.True(voided.IsCancelled);
            var pdf = AsLatin1(VoucherPdf.Render(voided, new PrintConfig(), new PageConfig()));
            Assert.Contains("CANCELLED", pdf);
            Assert.Contains("Receipt Voucher", pdf);      // the document still says what it is
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The invoice mirror — and the one that matters, because this document leaves the building. The over-print
    /// rides ALONGSIDE the statutory title: cancelling a tax invoice does not change what it was issued as, so
    /// "TAX INVOICE" must still be on the page.
    /// </summary>
    [Fact]
    public void A_cancelled_tax_invoice_prints_the_overprint_and_keeps_its_statutory_title()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexCancelInvPrint_" + Guid.NewGuid().ToString("N"));
        try
        {
            var k = GstCompanyWithInvoice(dir, "Invoice Print Co");
            var c = k.Vm.Company!;

            var live = VoucherPrintProjector.ProjectInvoice(c, c.FindVoucher(k.Invoice.Id)!);
            Assert.False(live.IsCancelled);
            Assert.DoesNotContain("CANCELLED", AsLatin1(InvoicePdf.Render(live, new PrintConfig(), new PageConfig())));

            new LedgerService(c).Cancel(k.Invoice.Id);

            var voided = VoucherPrintProjector.ProjectInvoice(c, c.FindVoucher(k.Invoice.Id)!);
            Assert.True(voided.IsCancelled);
            Assert.Equal(live.DocumentTitle, voided.DocumentTitle);      // the statutory title is untouched
            var pdf = AsLatin1(InvoicePdf.Render(voided, new PrintConfig(), new PageConfig()));
            Assert.Contains("CANCELLED", pdf);
            Assert.Contains(live.DocumentTitle, pdf);
        }
        finally { TryDelete(dir); }
    }

    /// <summary>
    /// 🔴 THE OVER-PRINT IS ON EVERY PAGE, not only page 1. A continuation sheet is a loose piece of paper the
    /// moment it leaves the printer, so a two-page cancelled voucher whose second sheet says nothing is a live
    /// document to whoever picks it up.
    ///
    /// <para>This also pins the CONTINUATION band-height reservation, which nothing else reaches: the count is
    /// taken against the page count the footer itself declares, so a banner that stopped repeating, or a page
    /// that stopped being produced, both show up here.</para>
    /// </summary>
    [Fact]
    public void The_overprint_repeats_on_every_page_of_a_multi_page_cancelled_voucher()
    {
        // Enough posting lines to force SEVERAL continuation pages at the default page size — several, not one,
        // so the continuation band's own reservation is exercised as well as the first page's.
        var lines = new List<VoucherPrintLine>();
        for (var i = 0; i < 240; i++)
            lines.Add(new VoucherPrintLine
            {
                LedgerName = $"Ledger {i:00}",
                IsDebit = i % 2 == 0,
                Amount = Money.FromRupees(100m),
            });

        var data = new VoucherPrintData
        {
            CompanyName = "Bright Traders",
            VoucherTypeName = "Journal",
            VoucherNumber = "7",
            DateText = "31-03-2025",
            Lines = lines,
            IsCancelled = true,
        };

        // A plain-ASCII footer template, so the page count can be read straight back out of the stream.
        var page = new PageConfig { FooterText = "Page {page} of {pages}" };
        var pdf = AsLatin1(VoucherPdf.Render(data, new PrintConfig(), page));

        // Read N out of the footer the renderer itself wrote, so the expectation is not hard-coded.
        const string marker = "Page 1 of ";
        var at = pdf.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at >= 0, "the footer did not render — this test cannot count pages");
        var declared = new string(pdf[(at + marker.Length)..].TakeWhile(char.IsDigit).ToArray());
        var pageCount = int.Parse(declared, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(pageCount >= 2, $"the fixture did not paginate (pages={pageCount}) — this test would be vacuous");

        var banners = pdf.Split("CANCELLED").Length - 1;
        Assert.Equal(pageCount, banners);

        // …and the extra band row is RESERVED, not merely drawn: the renderer's own contract is that body text
        // never intrudes on the footer band (it paginates against `MarginBottom + FooterFontSize + 6`). If the
        // header-height estimate ignored the over-print, the paginator would believe page 1 had a spare row and
        // the last posting line would be printed into that band.
        // Stated as a comparison against the SAME voucher rendered LIVE, so it needs no magic coordinate and
        // survives any change to the page geometry: cancelling a voucher may add a page, but the over-print must
        // never push the body text further down the sheet than the live document reaches. Without the matching
        // reservation the paginator believes page 1 still has a spare row and packs one more posting line onto
        // it — drawn below where the layout thought the page ended.
        var live = AsLatin1(VoucherPdf.Render(
            new VoucherPrintData
            {
                CompanyName = data.CompanyName,
                VoucherTypeName = data.VoucherTypeName,
                VoucherNumber = data.VoucherNumber,
                DateText = data.DateText,
                Lines = lines,
            },
            new PrintConfig(), page));

        Assert.True(LowestBaseline(pdf) >= LowestBaseline(live),
            $"the cancelled render reached y={LowestBaseline(pdf)}, below the live render's {LowestBaseline(live)}"
            + " — the over-print's band row was drawn but not reserved");
    }

    /// <summary>
    /// The invoice mirror of the pagination lock above. The over-print grows the INVOICE's title band too, and
    /// the same reservation has to grow with it or a long cancelled invoice prints one item row past the point
    /// the layout believed the page ended — on the document that actually goes to the customer.
    ///
    /// <para>Driven through <see cref="InvoicePdf"/> on a hand-built DTO rather than a posted voucher, because
    /// what is under test is the RENDERER's band arithmetic, not the projector (which the tests above cover).</para>
    /// </summary>
    [Fact]
    public void The_overprint_does_not_push_a_long_invoice_past_its_page_end()
    {
        var items = BuildServiceLines(240);

        InvoicePrintData Build(bool cancelled) => new()
        {
            DocumentTitle = "TAX INVOICE",
            Seller = new InvoicePartyBlock { Name = "Bright Traders", StateText = "Maharashtra (27)" },
            Buyer = new InvoicePartyBlock { Name = "Acme Ltd", StateText = "Maharashtra (27)" },
            InvoiceNumber = "INV-900",
            InvoiceDateText = "10-06-2024",
            PlaceOfSupply = "Maharashtra (27)",
            IsInterState = false,
            Items = items,
            TotalTaxable = Money.FromRupees(24000m),
            IsCancelled = cancelled,
        };

        var page = new PageConfig { FooterText = "Page {page} of {pages}" };
        var voided = AsLatin1(InvoicePdf.Render(Build(true), new PrintConfig(), page));
        var live = AsLatin1(InvoicePdf.Render(Build(false), new PrintConfig(), page));

        var voidedPages = PagesOf(voided);
        Assert.True(voidedPages.Count >= 2, "the fixture did not paginate — this test would be vacuous");

        // The banner is on EVERY sheet, page 1 and continuations alike.
        Assert.All(voidedPages, p => Assert.Contains("CANCELLED", p));

        // …and every sheet reserved the room for it. Compared PAGE BY PAGE against the live render: a whole-document
        // minimum would hide a page-1 overflow behind a last page that merely shifted by a row.
        var livePages = PagesOf(live);
        for (var i = 0; i < Math.Min(voidedPages.Count, livePages.Count); i++)
            Assert.True(LowestBaseline(voidedPages[i]) >= LowestBaseline(livePages[i]),
                $"page {i + 1} of the cancelled invoice reached y={LowestBaseline(voidedPages[i])}, below the live "
                + $"page's {LowestBaseline(livePages[i])} — the over-print's band row was drawn but not reserved");
    }

    /// <summary>
    /// 🔴 THE FIRST-PAGE RESERVATION, pinned as an INVARIANT OVER A RANGE OF SIZES rather than at one hand-picked
    /// item count. The over-print costs page 1 a band row, so a cancelled invoice can never fit MORE item rows on
    /// page 1 than the live one — and at the sizes where page 1 is filled to within a row of its end it must fit
    /// strictly fewer. Without the reservation in <c>FirstHeaderHeight</c> the paginator keeps handing page 1 the
    /// live row count and draws the last of them below where the layout ended the page.
    ///
    /// <para><b>Why a sweep.</b> The defect only shows at the sizes that sit on a page boundary. Pinning it with a
    /// single tuned item count would make the test a fossil of today's font metrics; sweeping a range asserts the
    /// general property and lets whichever size is on the boundary do the work. The "at least one strictly fewer"
    /// assertion is what stops the sweep passing vacuously if the range ever stops crossing a boundary.</para>
    /// </summary>
    [Fact]
    public void A_cancelled_invoice_never_fits_more_rows_on_page_one_than_a_live_one()
    {
        var page = new PageConfig { FooterText = "Page {page} of {pages}" };
        var strictlyFewer = 0;

        for (var n = 30; n <= 70; n++)
        {
            var items = BuildServiceLines(n);

            int RowsOnPageOne(bool cancelled)
            {
                var pdf = AsLatin1(InvoicePdf.Render(
                    new InvoicePrintData
                    {
                        DocumentTitle = "TAX INVOICE",
                        Seller = new InvoicePartyBlock { Name = "Bright Traders", StateText = "Maharashtra (27)" },
                        Buyer = new InvoicePartyBlock { Name = "Acme Ltd", StateText = "Maharashtra (27)" },
                        InvoiceNumber = "INV-901",
                        InvoiceDateText = "10-06-2024",
                        PlaceOfSupply = "Maharashtra (27)",
                        IsInterState = false,
                        Items = items,
                        TotalTaxable = Money.FromRupees(100m * n),
                        IsCancelled = cancelled,
                    },
                    new PrintConfig(), page));
                return PagesOf(pdf)[0].Split("Service line").Length - 1;
            }

            var live = RowsOnPageOne(cancelled: false);
            var voided = RowsOnPageOne(cancelled: true);
            Assert.True(voided <= live,
                $"with {n} lines a CANCELLED invoice fitted {voided} rows on page 1 but the live one fitted {live} "
                + "— the over-print's band row was not reserved");
            if (voided < live) strictlyFewer++;
        }

        Assert.True(strictlyFewer > 0,
            "no size in the swept range put page 1 on a boundary, so this test proved nothing — widen the range");
    }

    private static List<InvoiceItemRow> BuildServiceLines(int count)
    {
        var items = new List<InvoiceItemRow>(count);
        for (var i = 0; i < count; i++)
            items.Add(new InvoiceItemRow
            {
                Description = $"Service line {i:000}",
                HsnSac = "998311",
                QuantityText = "1.000",
                RateText = "100.00",
                TaxableValue = Money.FromRupees(100m),
            });
        return items;
    }

    /// <summary>Splits an uncompressed PDF stream into per-page content chunks on the footer each page ends with.</summary>
    private static List<string> PagesOf(string pdf) =>
        System.Text.RegularExpressions.Regex
            .Split(pdf, @"\(Page \d+ of \d+\) Tj")
            .Where(chunk => chunk.Contains(" Td", StringComparison.Ordinal))
            .ToList();

    /// <summary>The lowest text baseline drawn anywhere in a PDF stream — how far down the page the renderer got.
    /// The streams this writer produces are uncompressed, so the <c>x y Td</c> operators are readable text.</summary>
    private static double LowestBaseline(string pdf) =>
        System.Text.RegularExpressions.Regex.Matches(pdf, @"(-?[\d.]+) (-?[\d.]+) Td")
            .Select(m => double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Min();

    /// <summary>
    /// 🔴 THE SECOND INVOICE PROJECTION. A SERVICE (Accounting Invoice) sale carries no stock lines, so it takes
    /// its OWN projection path — a separate <c>new InvoicePrintData</c> that has to set the flag separately. This
    /// test exists because a mutation run proved the item-invoice test above could not reach it: deleting
    /// <c>IsCancelled</c> from the service branch alone left the suite green, i.e. a cancelled service invoice
    /// would have printed as a live one and nothing would have said so.
    /// </summary>
    [Fact]
    public void A_cancelled_service_accounting_invoice_also_prints_the_overprint()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexCancelSvcPrint_" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new CompanyStorage(dir);
            var vm = new MainWindowViewModel(storage);
            vm.NewCompanyName = "Service Print Co";
            vm.CreateCompany();
            var c = vm.Company!;
            c.FinancialYearStart = FyStart;
            c.BooksBeginFrom = FyStart;

            new GstService(c).EnableGst(new GstConfig
            {
                HomeStateCode = "27",
                Gstin = GstinMaharashtra,
                RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = FyStart,
                Periodicity = GstReturnPeriodicity.Monthly,
            });

            var consultancy = new DomainLedger(Guid.NewGuid(), "Consultancy Income",
                c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, false);
            consultancy.SalesPurchaseGst = new StockItemGstDetails
            {
                HsnSac = "998311", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
                SupplyType = GstSupplyType.Services,
            };
            c.AddLedger(consultancy);

            var customer = new DomainLedger(Guid.NewGuid(), "Local Customer",
                c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, false);
            customer.PartyGst = new PartyGstDetails
            { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
            c.AddLedger(customer);
            storage.Save(c);

            vm.OpenVoucher(VoucherBaseType.Sales);
            var entry = vm.VoucherEntry!;
            entry.ChangeMode();                       // As Voucher   -> Item Invoice
            entry.ChangeMode();                       // Item Invoice -> Accounting Invoice
            Assert.True(entry.IsAccountingInvoice);
            entry.Date = InvoiceDate;
            entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == customer.Id);
            while (entry.AccountingInvoiceLines.Count == 0) entry.AddAccountingInvoiceLine();
            entry.AccountingInvoiceLines[0].SelectedLedger =
                entry.AccountingInvoiceLedgers.Single(l => l.Id == consultancy.Id);
            entry.AccountingInvoiceLines[0].AmountText = "10000";
            entry.Recalculate();
            Assert.True(entry.Accept());

            var sale = c.Vouchers.Single(v => c.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Sales);
            Assert.True(VoucherPrintProjector.IsServiceAccountingInvoice(c, sale));   // the OTHER branch is live

            Assert.DoesNotContain("CANCELLED", AsLatin1(InvoicePdf.Render(
                VoucherPrintProjector.ProjectInvoice(c, sale), new PrintConfig(), new PageConfig())));

            new LedgerService(c).Cancel(sale.Id);

            var voided = VoucherPrintProjector.ProjectInvoice(c, c.FindVoucher(sale.Id)!);
            Assert.True(voided.IsCancelled);
            Assert.Contains("CANCELLED", AsLatin1(InvoicePdf.Render(voided, new PrintConfig(), new PageConfig())));
        }
        finally { TryDelete(dir); }
    }

    private static void TryDelete(string dir)
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }

    // ============================================================ (f) the VOUCHER EDIT LOG (schema v52)

    /// <summary>
    /// 🔴 <b>Alt+X now leaves a record that is not the flag.</b> Cancel was always the only one of the three verbs
    /// that left ANY evidence, and even that evidence was just a boolean on the voucher: it says the voucher IS
    /// cancelled, never that somebody cancelled it, and it says nothing at all about WHEN. The log entry does, and
    /// this proves the screen route reaches it — the engine's own tests cannot, because the wiring is what a
    /// screen-level guard is famously able to be missing from.
    /// </summary>
    [AvaloniaFact]
    public void Y_writes_one_edit_log_entry_that_survives_a_reload()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel Log Co");
            var c = vm.Company!;
            Assert.Empty(c.VoucherEditLog);

            OpenDayBookOn(window, vm, k.Receipt.Id);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);

            var entry = Assert.Single(c.VoucherEditLog);
            Assert.Equal(VoucherEditVerb.Cancel, entry.Verb);
            Assert.Equal(k.Receipt.Id, entry.VoucherId);
            // The snapshot is the voucher BEFORE the flag moved — the after-state is on the book.
            Assert.Contains("\"Cancelled\":false", entry.BeforeSnapshot, StringComparison.Ordinal);
            Assert.True(c.FindVoucher(k.Receipt.Id)!.Cancelled);

            // …and it is on disk, which is the only thing that makes it evidence rather than a session artefact.
            var storage = new CompanyStorage(dir);
            var reopened = storage.Load(storage.ListCompanies().Single(e => e.Name == c.Name));
            var persisted = Assert.Single(reopened.VoucherEditLog);
            Assert.Equal(entry.Id, persisted.Id);
            Assert.Equal(entry.BeforeSnapshot, persisted.BeforeSnapshot);
            Assert.Equal(entry.RecordedAt, persisted.RecordedAt);
        }
        finally { Close(window, dir); }
    }

    /// <summary>A declined cancellation writes NO entry. A log that records the questions as well as the answers
    /// would be as misleading as one that missed edits.</summary>
    [AvaloniaFact]
    public void N_writes_no_edit_log_entry()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "Cancel NoLog Co");
            OpenDayBookOn(window, vm, k.Receipt.Id);

            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.None);
            Pump(window);

            Assert.Empty(vm.Company!.VoucherEditLog);
        }
        finally { Close(window, dir); }
    }

}
