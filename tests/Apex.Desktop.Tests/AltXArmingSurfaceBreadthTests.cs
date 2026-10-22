using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 5.2 — A MEASUREMENT THAT CORRECTS THE ROW, NOT A NEW FEATURE.</b>
///
/// <para><b>What the census cell claims.</b> <i>"Gap: armed on <b>one</b> surface (the live Day Book) where the
/// corpus scopes it to 'Vouchers &amp; Reports'"</i>, re-stated in the same cell as <i>"still ONE arming surface
/// where the vendor scopes it to 'Vouchers &amp; Reports'"</i>, and again as <i>"Alt+X's key arm requires the Day
/// Book row, so it fires from no drill column at all"</i>.</para>
///
/// <para>🔴 <b>THE FIRST TWO OF THOSE ARE FALSE AT HEAD, AND THIS FILE IS THE PROOF.</b> The arm in
/// <c>MainWindow.axaml.cs</c> is gated on <c>vm.IsLiveReportPage</c>, which is
/// <c>Reports is not null &amp;&amp; CurrentScreen == Screen.Report</c> — a whole SURFACE CLASS, not the Day
/// Book. And <c>RequestCancelHighlightedVoucher</c> resolves through <c>Reports.SelectedRow</c>, which every
/// report kind on that screen fills. <c>ReportsViewModel</c>'s own drill switch stamps
/// <c>DrillVoucherId</c> on the rows of at least <b>thirteen</b> kinds besides the Day Book — the four register
/// families (Sales / Purchase / Journal / Credit-Note / Debit-Note), <c>GroupVouchers</c>, the three Day-Book
/// siblings (Optional / Cancelled / Post-dated) and the four banking lists (Cheque Printing / Supplier Payment
/// Advice / Deposit Slip / e-Payments). Alt+X is armed on all of them.</para>
///
/// <para><b>🔴 WHAT THE REAL RESIDUAL IS, stated precisely so the row can be re-graded on something true.</b>
/// Alt+X is armed on <c>Screen.Report</c> and NOWHERE ELSE. It is NOT armed on <c>Screen.LedgerVouchers</c> (the
/// ledger book / account register) nor on <c>Screen.VoucherDetail</c> (the drill column) — and
/// <c>RequestCancelHighlightedVoucher</c> has no arm for either, where
/// <c>RequestAlterHighlightedVoucher</c> carries all three. That asymmetry is the third clause of the cell, and
/// it is the one that survives this measurement. <b>Nothing here widens the arm:</b> the cell records a
/// deliberate decision not to widen Alt+X to the detail column, and ruling 3 settles that no un-cancel ships.
/// This file only replaces a wrong count with a measured one.</para>
///
/// <para><b>Why a test and not a note.</b> "The census said X and X is false" has been wrong in this project
/// before — a truth pass found 45 clauses called open that were already closed, and one "gap" that was the
/// project's own invention. A claim about REACH is only worth what the keystroke proves, so both tests below go
/// through the real <c>MainWindow</c> key handler on a surface that is provably not the Day Book.</para>
/// </summary>
public sealed class AltXArmingSurfaceBreadthTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexAltXBreadth_" + Guid.NewGuid().ToString("N"));
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

    private static void Close(Window window, string dir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record Kit(DomainLedger Cash, DomainLedger Capital, Voucher Receipt);

    /// <summary>One posted Receipt (Dr Cash 50,000 / Cr Capital 50,000), keyed and accepted through the real
    /// screens so the voucher is genuinely posted and persisted rather than hand-built.</summary>
    private static Kit SeedOneReceipt(MainWindow window, MainWindowViewModel vm, string name)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();

        vm.ShowLedgerMaster();
        vm.LedgerMaster!.Name = "Capital A/c";
        vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Capital Account");
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);

        var capital = vm.Company!.FindLedgerByName("Capital A/c")!;
        var cash = vm.Company!.FindLedgerByName("Cash")!;

        vm.OpenVoucher(VoucherBaseType.Receipt);
        var e = vm.VoucherEntry!;
        e.Date = vm.Company!.FinancialYearStart.AddDays(5);
        e.Lines[0].SelectedLedger = cash;
        e.Lines[0].Side = DrCr.Debit;
        e.Lines[0].AmountText = "50000";
        e.Lines[1].SelectedLedger = capital;
        e.Lines[1].Side = DrCr.Credit;
        e.Lines[1].AmountText = "50000";
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Assert.Single(vm.Company!.Vouchers);

        return new Kit(cash, capital, vm.Company!.Vouchers[0]);
    }

    /// <summary>
    /// Opens <b>Group Vouchers</b> for the group the Capital ledger sits under — a <c>Screen.Report</c> page that
    /// is provably NOT the Day Book — and highlights the row that drills to the posted receipt.
    /// </summary>
    private static void OpenGroupVouchersOn(MainWindow window, MainWindowViewModel vm, Kit k)
    {
        var group = vm.Company!.FindGroupByName("Capital Account")!;
        vm.OpenGroupReport(ReportKind.GroupVouchers, group.Id);
        Pump(window);

        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.NotNull(vm.Reports);
        Assert.Equal(ReportKind.GroupVouchers, vm.Reports!.Kind);
        // 🔴 The load-bearing negative: this is NOT the Day Book, so a pass below cannot be the Day-Book arm
        // firing under another name.
        Assert.False(vm.IsDayBookReport);

        var row = vm.Reports!.Rows.First(r => r.DrillVoucherId == k.Receipt.Id);
        vm.Reports!.SelectedRow = row;
    }

    // ================================================================= the measurement

    /// <summary>
    /// 🔴 <b>Alt+X RAISES THE CANCELLATION CONFIRMATION FROM A NON-DAY-BOOK REPORT.</b> If the census clause
    /// "armed on one surface (the live Day Book)" were true, this keystroke would be inert.
    ///
    /// <para>It also keeps the destructive half honest: the voucher is NOT cancelled on the keystroke, only on
    /// the answer — the same contract <c>VoucherCancelAltXTests</c> locks for the Day Book.</para>
    /// </summary>
    [AvaloniaFact]
    public void AltX_is_armed_on_a_non_day_book_report_and_raises_the_confirmation_there()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "AltX Breadth Co");
            OpenGroupVouchersOn(window, vm, k);

            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);

            Assert.True(vm.IsAcceptPromptOpen);
            Assert.Contains("Cancel", vm.AcceptPromptText);
            Assert.Contains("Receipt", vm.AcceptPromptText);
            Assert.Contains("(Y/N)", vm.AcceptPromptText);

            // Nothing has happened yet — the keystroke asks, it does not act.
            Assert.False(vm.Company!.FindVoucher(k.Receipt.Id)!.Cancelled);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>AND THE VERB COMPLETES THERE — the arm is not a prompt that leads nowhere.</b> "Y" cancels the
    /// voucher, it KEEPS ITS NUMBER (the voucher number stays consumed), the books empty, and the cancellation
    /// SURVIVES A RELOAD, which is the only proof the snapshot was written.
    ///
    /// <para>This is the clause-by-clause audit the track requires of every voucher verb: the voucher is read
    /// back off the RELOADED book, it is still THERE (a cancellation is not a deletion), and it is flagged.</para>
    /// </summary>
    [AvaloniaFact]
    public void Y_from_a_non_day_book_report_cancels_keeps_the_number_and_survives_a_reload()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "AltX Breadth Y Co");
            var company = vm.Company!;
            var asOf = company.FinancialYearStart.AddYears(1);
            var numberBefore = k.Receipt.Number;
            Assert.Equal(50000m, LedgerBalances.SignedClosing(company, k.Cash, asOf));

            OpenGroupVouchersOn(window, vm, k);
            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);

            var cancelled = company.FindVoucher(k.Receipt.Id)!;
            Assert.True(cancelled.Cancelled);
            Assert.Equal(numberBefore, cancelled.Number);                 // the number stays CONSUMED
            Assert.Equal(0m, LedgerBalances.SignedClosing(company, k.Cash, asOf));
            Assert.Equal(0m, LedgerBalances.SignedClosing(company, k.Capital, asOf));

            // 🔴 OFF THE RELOADED BOOK: still present (cancelled, not deleted) and still flagged.
            var storage = new CompanyStorage(dir);
            var reopened = storage.Load(storage.ListCompanies().Single(e => e.Name == company.Name));
            var persisted = reopened.FindVoucher(k.Receipt.Id);
            Assert.NotNull(persisted);
            Assert.True(persisted!.Cancelled);
            Assert.Equal(numberBefore, persisted.Number);
        }
        finally { Close(window, dir); }
    }

    // ================================================================= the residual, pinned as it really is

    /// <summary>
    /// 🔴 <b>THE RESIDUAL, PINNED SO THE ROW CAN BE RE-GRADED ON SOMETHING TRUE.</b> Alt+X is armed on
    /// <c>Screen.Report</c> and not on <c>Screen.VoucherDetail</c>: on the drill column the keystroke is inert
    /// and raises no confirmation.
    ///
    /// <para><b>This test pins CURRENT, DELIBERATE behaviour — it is not a defect report.</b> The census cell
    /// records the decision in terms this file will not second-guess: widening Alt+X to the detail column "would
    /// make a cancelled voucher more reachable than an ordinary one". It is pinned because the asymmetry with
    /// Alt+D is easy to "tidy up" by accident, and because a future widening should have to change a test that
    /// says out loud what it is changing.</para>
    /// </summary>
    [AvaloniaFact]
    public void AltX_is_inert_on_the_voucher_detail_drill_column_and_that_is_the_surviving_asymmetry()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = SeedOneReceipt(window, vm, "AltX Detail Co");

            // Reach the detail column the way the operator does: highlight the Day-Book row and drill in.
            vm.OpenReport(ReportKind.DayBook);
            Pump(window);
            vm.Reports!.SelectedRow = vm.Reports!.Rows.First(r => r.DrillVoucherId == k.Receipt.Id);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen);
            Assert.NotNull(vm.VoucherDetail);
            Assert.Equal(k.Receipt.Id, vm.VoucherDetail!.VoucherId);

            window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
            Pump(window);

            // Inert: no question asked, nothing cancelled.
            Assert.False(vm.IsAcceptPromptOpen);
            Assert.False(vm.Company!.FindVoucher(k.Receipt.Id)!.Cancelled);
        }
        finally { Close(window, dir); }
    }
}
