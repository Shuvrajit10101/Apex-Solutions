using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// WI-12 — the Day-Book keyboard additions, driven through the REAL <see cref="MainWindow"/> tunnel handler
/// (<c>window.KeyPressQwerty</c>), never by asserting a binding exists in isolation:
/// <list type="bullet">
/// <item><b>Alt+A</b> on the Day Book opens a voucher-type PICKER of every active type and does NOT destroy the
/// Day Book (<see cref="MainWindowViewModel.Reports"/> stays bound); picking a type opens that entry seeded with
/// the highlighted row's date, and saving returns to a REFRESHED Day Book that shows the new voucher.</item>
/// <item><b>Alt+F5 / Alt+F6</b> open the (already-implemented) Debit / Credit Note entry screens.</item>
/// </list>
/// Scoping is proven too: Alt+A on POS still shows Tax Analysis and Alt+A on a non-Day-Book report is inert.
/// </summary>
public sealed class DayBookAddVoucherKeyboardTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexAddVoucher_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        return (window, vm, tempDir);
    }

    /// <summary>A fresh company carrying one posted Receipt (Dr Cash / Cr Capital) dated <paramref name="on"/>,
    /// so the Day Book has a drillable row + a date basis. Returns the Cash and Capital ledgers for reuse.</summary>
    private static (DomainLedgerPair Ledgers, DateOnly First) SeedCompanyWithOneReceipt(
        MainWindow window, MainWindowViewModel vm, string name)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var fyStart = vm.Company!.FinancialYearStart;
        var on = fyStart.AddDays(5);

        vm.ShowLedgerMaster();
        vm.LedgerMaster!.Name = "Capital A/c";
        vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Capital Account");
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control); // Ctrl+A creates the ledger
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
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control); // accept → posted + persisted
        Assert.Single(vm.Company!.Vouchers);

        return (new DomainLedgerPair(cash, capital), on);
    }

    private sealed record DomainLedgerPair(DomainLedger Cash, DomainLedger Capital);

    /// <summary>Steps the active menu-column highlight (real Down keys) until it lands on <paramref name="label"/>.</summary>
    private static void NavigateMenuTo(MainWindowViewModel vm, MainWindow window, string label)
    {
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            if (vm.Menu[vm.SelectedIndex].Label == label) return;
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        }
        Assert.Equal(label, vm.Menu[vm.SelectedIndex].Label);
    }

    // ============================================================ (a) Alt+A → picker, non-destructive

    /// <summary>
    /// THE DRIVING TEST. Real Alt+A on the Day Book opens the voucher-type picker as a NEW column AND leaves the
    /// Day Book alive: <see cref="MainWindowViewModel.Reports"/> stays the SAME instance and its report column
    /// survives. (If the Alt+A binding were absent the screen would stay <see cref="Screen.Report"/> and no picker
    /// column would appear — the assertion below fails, which is what proves this test bites.)
    /// </summary>
    [AvaloniaFact]
    public void AltA_on_the_day_book_opens_the_picker_and_does_not_null_the_day_book()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedCompanyWithOneReceipt(window, vm, "AddVoucher DayBook Co");
            vm.OpenReport(ReportKind.DayBook);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            var dayBook = vm.Reports!;
            var columnsBefore = vm.Columns.Count;

            // Real Alt+A on the window — drives the actual tunnel handler.
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);

            // The picker opened as a new column…
            Assert.Equal(Screen.AddVoucherPicker, vm.CurrentScreen);
            Assert.Equal(columnsBefore + 1, vm.Columns.Count);
            // …and the Day Book was NOT destroyed: the SAME report instance is still bound and still on-screen.
            Assert.NotNull(vm.Reports);
            Assert.Same(dayBook, vm.Reports);
            Assert.Contains(vm.Columns, c => ReferenceEquals(c.Report, dayBook));
            Assert.True(vm.IsDayBookReport);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>The picker lists EVERY active voucher type — which is what makes "any type of voucher" true and, as
    /// a side effect, gives Credit/Debit Note their first real UI route.</summary>
    [AvaloniaFact]
    public void AltA_picker_lists_all_active_types_including_credit_and_debit_note()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedCompanyWithOneReceipt(window, vm, "AddVoucher Types Co");
            vm.OpenReport(ReportKind.DayBook);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);

            var picker = vm.Columns[^1];
            Assert.True(picker.IsMenu);
            var labels = picker.Items.Where(i => i.IsSelectable).Select(i => i.Label).ToList();

            // Exactly the ACTIVE seeded types, and no inactive ones (Attendance / Payroll / Job Work / Material).
            var expectedActive = vm.Company!.VoucherTypes.Where(t => t.IsActive).Select(t => t.Name).ToList();
            Assert.Equal(expectedActive, labels);
            Assert.Contains("Credit Note", labels);
            Assert.Contains("Debit Note", labels);
            Assert.Contains("Payment", labels);
            Assert.DoesNotContain("Payroll", labels);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// Full round-trip: Alt+A → pick "Receipt" (real Down+Enter) → the entry opens seeded with the highlighted
    /// row's date → fill + accept (real Ctrl+A) → back on a REFRESHED Day Book whose rows now include the new
    /// voucher. This is the assertion that actually proves the point (reachability + refresh), not merely that a
    /// method ran.
    /// </summary>
    [AvaloniaFact]
    public void Picking_a_type_opens_its_entry_seeded_with_the_row_date_and_saving_refreshes_the_day_book()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            var (ledgers, firstDate) = SeedCompanyWithOneReceipt(window, vm, "AddVoucher Refresh Co");
            vm.OpenReport(ReportKind.DayBook);

            // Highlight the existing Day-Book row so the added voucher inherits its date.
            var existingRow = vm.Reports!.Rows.First(r => r.DrillVoucherId != Guid.Empty);
            vm.Reports!.SelectedRow = existingRow;

            var vouchersBefore = vm.Company!.Vouchers.Count;

            // Alt+A → picker, navigate to "Receipt", Enter to pick it.
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
            Assert.Equal(Screen.AddVoucherPicker, vm.CurrentScreen);
            NavigateMenuTo(vm, window, "Receipt");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            // The Receipt entry opened, seeded with the highlighted row's date.
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Equal(VoucherBaseType.Receipt, vm.VoucherEntry!.Type.BaseType);
            Assert.Equal(firstDate, vm.VoucherEntry!.Date);

            // Fill a balanced Receipt and accept it through the real window.
            var e = vm.VoucherEntry!;
            e.Lines[0].SelectedLedger = ledgers.Cash;
            e.Lines[0].Side = DrCr.Debit;
            e.Lines[0].AmountText = "12345";
            e.Lines[1].SelectedLedger = ledgers.Capital;
            e.Lines[1].Side = DrCr.Credit;
            e.Lines[1].AmountText = "12345";
            Assert.True(e.CanAccept);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control); // accept

            // Back on a refreshed Day Book (NOT the Gateway) that shows the newly-posted voucher.
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.NotNull(vm.Reports);
            Assert.Equal(ReportKind.DayBook, vm.Reports!.Kind);
            Assert.Equal(vouchersBefore + 1, vm.Company!.Vouchers.Count);
            var newVoucher = vm.Company!.Vouchers.OrderBy(v => v.Number).Last();
            Assert.Contains(vm.Reports!.Rows, r => r.DrillVoucherId == newVoucher.Id);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>Esc on the picker pops it and restores the SAME live Day Book beneath — the non-destructive
    /// contract works in the back direction too (the report is re-bound, not orphaned).</summary>
    [AvaloniaFact]
    public void Esc_on_the_picker_returns_to_the_live_day_book()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedCompanyWithOneReceipt(window, vm, "AddVoucher Esc Co");
            vm.OpenReport(ReportKind.DayBook);
            var dayBook = vm.Reports!;
            var columnsBefore = vm.Columns.Count;

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
            Assert.Equal(Screen.AddVoucherPicker, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.Same(dayBook, vm.Reports);
            Assert.Equal(columnsBefore, vm.Columns.Count);
            Assert.True(vm.IsDayBookReport);
        }
        finally { Close(window, tempDir); }
    }

    // ============================================================ (b) Alt+F5 / Alt+F6 → Debit / Credit Note

    /// <summary>Alt+F5 opens the (existing) Debit Note entry screen — the catalogue-specified accelerator that had
    /// no key route.</summary>
    [AvaloniaFact]
    public void AltF5_opens_the_debit_note_entry()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            vm.NewCompanyName = "AddVoucher DN Co";
            vm.CreateCompany();

            window.KeyPressQwerty(PhysicalKey.F5, RawInputModifiers.Alt);

            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.NotNull(vm.VoucherEntry);
            Assert.Equal(VoucherBaseType.DebitNote, vm.VoucherEntry!.Type.BaseType);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>Alt+F6 opens the (existing) Credit Note entry screen.</summary>
    [AvaloniaFact]
    public void AltF6_opens_the_credit_note_entry()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            vm.NewCompanyName = "AddVoucher CN Co";
            vm.CreateCompany();

            window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.Alt);

            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.NotNull(vm.VoucherEntry);
            Assert.Equal(VoucherBaseType.CreditNote, vm.VoucherEntry!.Type.BaseType);
        }
        finally { Close(window, tempDir); }
    }

    // ============================================================ scoping / no-regression

    /// <summary>No regression: Alt+A on the POS Billing entry still surfaces Tax Analysis (the picker never
    /// hijacks it — the POS binding is ordered first and stays scoped to the POS screen).</summary>
    [AvaloniaFact]
    public void AltA_on_pos_billing_still_shows_tax_analysis_not_the_picker()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            vm.NewCompanyName = "AddVoucher POS Co";
            vm.CreateCompany();
            vm.OpenPosBilling();
            Assert.Equal(Screen.PosBilling, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);

            Assert.Equal(Screen.PosBilling, vm.CurrentScreen);          // NOT the picker
            Assert.NotEqual(Screen.AddVoucherPicker, vm.CurrentScreen);
            Assert.True(vm.PosBilling!.IsTaxAnalysisVisible);           // Tax Analysis fired
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>Scoping: Alt+A on a NON-Day-Book report (Trial Balance) is inert — the "Add Voucher" picker is a
    /// Day-Book-only affordance, so the report is untouched and no picker column appears.</summary>
    [AvaloniaFact]
    public void AltA_on_a_non_day_book_report_does_not_open_the_picker()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedCompanyWithOneReceipt(window, vm, "AddVoucher NonDayBook Co");
            vm.OpenReport(ReportKind.TrialBalance);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            var columnsBefore = vm.Columns.Count;

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);

            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.NotEqual(Screen.AddVoucherPicker, vm.CurrentScreen);
            Assert.Equal(columnsBefore, vm.Columns.Count);
        }
        finally { Close(window, tempDir); }
    }

    private static void Close(MainWindow window, string tempDir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }
}
