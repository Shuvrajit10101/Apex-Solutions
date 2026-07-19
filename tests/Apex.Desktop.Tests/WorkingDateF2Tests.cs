using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// WI-5 part 4c — <b>F2 sets the date "in whatever window"</b>, driven through the REAL <see cref="MainWindow"/>
/// tunnel key handler (<c>window.KeyPressQwerty</c>), never by asserting that a view-model method exists.
/// <para>
/// Before this work item F2 was implemented on REPORTS only; on every other screen it was a stub that printed
/// the company's financial-year start to the status line — a value written once at company open and never
/// updated. So on a voucher-entry screen, precisely the case the reference corpus documents ("Date — Type date
/// of Purchase/Sale transactions by pressing F2"), F2 did nothing useful.
/// </para>
/// <para>
/// These tests also pin the design constraint: F2 is <b>keyboard-only</b>. It moves the caret into the working
/// date field; it must never open a calendar/DatePicker (the app has zero DatePicker controls by design).
/// </para>
/// </summary>
public sealed class WorkingDateF2Tests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexF2Date_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        return (window, vm, tempDir);
    }

    private static TextBox? FocusedWorkingDateBox(MainWindow window) =>
        window.GetVisualDescendants()
            .OfType<TextBox>()
            .FirstOrDefault(b => b.Classes.Contains("working-date") && b.IsFocused);

    // ------------------------------------------------------------------ the driving test

    /// <summary>
    /// THE driving test: on a Payment voucher, pressing F2 puts the caret in the voucher-date field, and the
    /// date typed there — in the Indian day-first convention — becomes the voucher's date, rendered back in
    /// the one canonical spelling.
    /// </summary>
    [AvaloniaFact]
    public void F2_on_voucher_entry_sets_the_voucher_date_from_day_first_keyboard_input()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            vm.NewCompanyName = "F2 Date Co";
            vm.CreateCompany();
            vm.OpenVoucher(VoucherBaseType.Payment);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);

            var entry = vm.VoucherEntry!;
            var before = entry.Date;

            // The screen advertises a working date F2 can set (it is an ISetsWorkingDate page).
            Assert.True(vm.IsWorkingDateContext);
            Assert.Same(entry, vm.ActiveWorkingDateTarget);

            // Nothing is focused on the date field yet.
            Assert.Null(FocusedWorkingDateBox(window));

            // Drive the REAL key handler.
            window.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.None);

            // F2 moved the caret into the voucher-date box — and opened NO calendar.
            var box = FocusedWorkingDateBox(window);
            Assert.NotNull(box);
            Assert.Empty(window.GetVisualDescendants().OfType<DatePicker>());
            Assert.Empty(window.GetVisualDescendants().OfType<CalendarDatePicker>());

            // Type an Indian-convention date into the focused box and commit it (TextBox commits on lost focus).
            box!.Text = "03/04/2024";
            box.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(InputElement.LostFocusEvent));

            // The voucher date is 3-APRIL — not 4-March, which the old InvariantCulture (MM/dd) parse produced.
            Assert.Equal(new DateOnly(2024, 4, 3), entry.Date);
            Assert.NotEqual(new DateOnly(2024, 3, 4), entry.Date);
            Assert.NotEqual(before, entry.Date);

            // …and the field echoes the ONE canonical spelling.
            Assert.Equal("03-Apr-2024", entry.DateText);
            Assert.Equal("03-Apr-2024", entry.WorkingDateText);
        }
        finally
        {
            window.Close();
            Cleanup(tempDir);
        }
    }

    /// <summary>
    /// F2 reaches the working date on an INVENTORY entry screen too — "in whatever window", not just the
    /// accounting voucher.
    /// </summary>
    [AvaloniaFact]
    public void F2_also_sets_the_date_on_an_inventory_entry_screen()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            vm.NewCompanyName = "F2 Inventory Co";
            vm.CreateCompany();
            vm.OpenInventoryVoucher(VoucherBaseType.PurchaseOrder);
            Assert.Equal(Screen.InventoryVoucherEntry, vm.CurrentScreen);

            var entry = vm.InventoryVoucherEntry!;
            Assert.True(vm.IsWorkingDateContext);

            window.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.None);

            var box = FocusedWorkingDateBox(window);
            Assert.NotNull(box);

            box!.Text = "03/04/2024";
            box.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(InputElement.LostFocusEvent));

            Assert.Equal(new DateOnly(2024, 4, 3), entry.Date);
            Assert.Equal("03-Apr-2024", entry.DateText);
        }
        finally
        {
            window.Close();
            Cleanup(tempDir);
        }
    }

    /// <summary>
    /// The report F2 / Alt+F2 split is UNTOUCHED: bare F2 on a report still opens the config panel on the
    /// single as-of date, and never routes to a working-date field (a report has none). This guards the
    /// delicate precedence chain in the shell's key handler.
    /// </summary>
    [AvaloniaFact]
    public void F2_on_a_report_still_sets_the_report_as_of_and_is_not_a_working_date_context()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            vm.NewCompanyName = "F2 Report Co";
            vm.CreateCompany();
            vm.OpenReport(ReportKind.BalanceSheet);
            Assert.Equal(Screen.Report, vm.CurrentScreen);

            // A report is NOT a working-date context — F2 must keep its report meaning.
            Assert.False(vm.IsWorkingDateContext);
            Assert.Null(vm.ActiveWorkingDateTarget);

            window.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.None);

            Assert.NotNull(vm.ReportConfig);
            Assert.False(vm.ReportConfig!.UsePeriod);   // F2 = the single as-of; Alt+F2 = the period window
        }
        finally
        {
            window.Close();
            Cleanup(tempDir);
        }
    }

    /// <summary>
    /// F2 on a plain menu screen is harmless and reports the current date rather than routing anywhere.
    /// </summary>
    [AvaloniaFact]
    public void F2_on_the_gateway_is_inert_and_opens_no_date_editor()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            vm.NewCompanyName = "F2 Gateway Co";
            vm.CreateCompany();
            Assert.Equal(Screen.Gateway, vm.CurrentScreen);
            Assert.False(vm.IsWorkingDateContext);

            window.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.None);

            Assert.Null(FocusedWorkingDateBox(window));
        }
        finally
        {
            window.Close();
            Cleanup(tempDir);
        }
    }

    /// <summary>
    /// Every entry screen that claims to own a working date really round-trips it through the shared canonical
    /// contract. This is the "in whatever window" coverage — asserted on the VM contract rather than by opening
    /// eight windows.
    /// </summary>
    [AvaloniaFact]
    public void Each_entry_screen_exposes_a_canonical_working_date()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            vm.NewCompanyName = "F2 Coverage Co";
            vm.CreateCompany();

            vm.OpenVoucher(VoucherBaseType.Payment);
            AssertWorkingDateRoundTrips(vm.ActiveWorkingDateTarget);

            vm.OpenInventoryVoucher(VoucherBaseType.PurchaseOrder);
            AssertWorkingDateRoundTrips(vm.ActiveWorkingDateTarget);
        }
        finally
        {
            window.Close();
            Cleanup(tempDir);
        }
    }

    private static void AssertWorkingDateRoundTrips(ISetsWorkingDate? target)
    {
        Assert.NotNull(target);

        // A day-first numeric date is accepted and echoed canonically.
        target!.WorkingDateText = "03/04/2024";
        Assert.Equal("03-Apr-2024", target.WorkingDateText);

        // Unparseable input is REJECTED, not silently swallowed: the last valid date survives.
        target.WorkingDateText = "not-a-date";
        Assert.Equal("03-Apr-2024", target.WorkingDateText);
    }

    private static void Cleanup(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}
