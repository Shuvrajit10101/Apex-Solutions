using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS ROW 11.5 — F6 (MONTHLY) ON A LEDGER BOOK.</b>
///
/// <para><b>What was actually missing, which is NOT what the wave brief said.</b> The brief (from
/// <c>wave28-gaps-reports.md</c> §0.3 / §11.5) called for a one-line route change: make
/// <c>MainWindowViewModel.OpenAccountBook</c> open <c>ReportKind.LedgerMonthlySummary</c> instead of the voucher
/// list, on the stated ground that "the vendor first level is the MONTHLY SUMMARY". <b>That premise is false, and
/// making the change would have moved this product AWAY from the vendor.</b> The vendor's own FAQ gives the route
/// as "Gateway of Tally &gt; Display More Reports &gt; Accounts Books &gt; Ledger", which lands on the
/// <b>Ledger Vouchers</b> report — exactly what <c>OpenAccountBook</c> already did — and then says
/// "Press <b>F6 (Monthly)</b> to view the monthly summary" (<c>help.tallysolutions.com/accounting-faq/</c>).
/// The monthly summary is the vendor's SECOND level, reached by a chord. The first level was already right.</para>
///
/// <para><b>So the real gap was the chord.</b> <c>ReportKind.LedgerMonthlySummary</c> and
/// <c>MainWindowViewModel.OpenLedgerMonthlySummary</c> already shipped for the 11.7 group drill, so a Group
/// Summary ledger row could reach the monthly view — but an operator who opened the SAME ledger by the route the
/// vendor documents had no keystroke that reached it at all. A capability reachable from one route and not from
/// the documented one is what this project counts as absent.</para>
///
/// <para>Every assertion drives the REAL key through <see cref="MainWindow"/>'s tunnel handler. A test calling
/// <c>OpenMonthlySummaryForLedgerBook</c> directly would prove nothing about reach — the defect class this
/// project has shipped most often.</para>
///
/// <para><b>Fixture: "Robert"</b>, one of the two R8 baseline regressions. Its <c>Cash</c> ledger carries
/// <b>eight</b> vouchers, all inside April 2020 — so "the summary shows ONE month row" and "the book shows eight
/// posting rows" are different numbers, and a summary that merely re-listed the vouchers cannot pass.</para>
/// </summary>
public sealed class AccountBookMonthlySummaryTests : IDisposable
{
    private const string LedgerName = "Cash";

    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "ApexAcctBook_" + Guid.NewGuid().ToString("N"));

    private (MainWindow Window, MainWindowViewModel Vm) NewWindow()
    {
        var vm = new MainWindowViewModel(new CompanyStorage(_tempDir));
        vm.LoadRobertDemo();
        vm.ShowGateway();

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static void Key(MainWindow window, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, mods);
        Dispatcher.UIThread.RunJobs();
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
    /// 🔴 <b>THE ROW-11.5 TEST.</b> Open the account book, press <b>F6</b>, land on that ledger's Monthly
    /// Summary. Fails on today's <c>main</c> by construction: F6 was bound to the Receipt voucher on every
    /// screen, so the key opened a voucher-entry screen instead of the summary.
    /// </summary>
    [AvaloniaFact]
    public void F6_on_a_ledger_book_opens_that_ledgers_monthly_summary()
    {
        var (window, vm) = NewWindow();
        try
        {
            vm.OpenAccountBook(LedgerName);
            Dispatcher.UIThread.RunJobs();

            // The vendor's FIRST level is the voucher list, and it STAYS that way — see the class remarks.
            Assert.Equal(Screen.LedgerVouchers, vm.CurrentScreen);
            var bookRows = vm.LedgerVouchers!.Rows.Count(r => r.DrillVoucherId != Guid.Empty);
            Assert.Equal(8, bookRows);

            Key(window, PhysicalKey.F6);

            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.NotNull(vm.Reports);
            Assert.Equal(ReportKind.LedgerMonthlySummary, vm.Reports!.Kind);

            // It really summarises BY MONTH: eight vouchers inside one month collapse to ONE month row. A
            // summary that re-listed the vouchers would show eight, i.e. the level the operator came from.
            var monthRows = vm.Reports.Rows.Where(r => r.DrillPeriod is not null).ToList();
            Assert.Single(monthRows);
            Assert.Equal(4, monthRows[0].DrillPeriod!.Value.From.Month);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The summary is built over the BOOK'S OWN window rather than the company's default period — otherwise the
    /// months it lists would not foot to the book the operator pressed F6 on.
    ///
    /// <para>🔴 <b>THE NARROWED WINDOW IS WHAT GIVES THIS TEST ITS BITE, and it is deliberate.</b> Opening the
    /// book through <c>OpenAccountBook</c> would make this assertion VACUOUS on the Robert fixture: that route
    /// uses <c>Company.BooksBeginFrom</c> … last-voucher-date, which for Robert is 01-Apr-2020 … 30-Apr-2020 —
    /// numerically identical to the company's own books period, so substituting one for the other is invisible.
    /// Mutation-checked: with the book opened that way, replacing the book's <c>PeriodFrom</c> with
    /// <c>Company.BooksBeginFrom</c> in <c>OpenMonthlySummaryForLedgerBook</c> left the test GREEN. A book opened
    /// over a narrower window — which is exactly what a Trial Balance or Group Summary drill produces — separates
    /// the two, and the same mutation then reddens this test.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_monthly_summary_covers_the_books_own_window_not_the_companys()
    {
        var (window, vm) = NewWindow();
        try
        {
            var ledgerId = vm.Company!.FindLedgerByName(LedgerName)!.Id;
            var from = new DateOnly(2020, 4, 10);
            var to = new DateOnly(2020, 4, 20);
            Assert.NotEqual(vm.Company.BooksBeginFrom, from);   // the clause is not vacuous

            vm.OpenLedgerVouchers(ledgerId, from, to);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Screen.LedgerVouchers, vm.CurrentScreen);

            Key(window, PhysicalKey.F6);

            Assert.NotNull(vm.Reports!.Period);
            Assert.Equal(from, vm.Reports.Period!.Value.From);
            Assert.Equal(to, vm.Reports.Period!.Value.To);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE IDENTITY CLAUSE.</b> The summary is scoped to the ledger the operator was standing on, proved
    /// behaviourally: drilling its month row returns to a book for the SAME ledger, over that MONTH's window.
    /// Scoping it to the wrong ledger would show one account's months under another account's heading — and an
    /// assertion on the title string alone would not catch that.
    /// </summary>
    [AvaloniaFact]
    public void A_month_row_drills_back_into_the_same_ledger_over_that_month()
    {
        var (window, vm) = NewWindow();
        try
        {
            vm.OpenAccountBook(LedgerName);
            Dispatcher.UIThread.RunJobs();
            var ledgerId = vm.LedgerVouchers!.LedgerId;

            Key(window, PhysicalKey.F6);

            var april = vm.Reports!.Rows.First(r => r.DrillPeriod is not null);
            vm.Reports.SelectedRow = april;
            Key(window, PhysicalKey.Enter);

            Assert.Equal(Screen.LedgerVouchers, vm.CurrentScreen);
            Assert.Equal(ledgerId, vm.LedgerVouchers!.LedgerId);
            Assert.Equal(new DateOnly(2020, 4, 1), vm.LedgerVouchers.PeriodFrom);
            Assert.Equal(new DateOnly(2020, 4, 30), vm.LedgerVouchers.PeriodTo);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE BADGE MUST SAY WHAT THE KEY DOES.</b> F6 is context-sensitive, and this shell's sibling defects
    /// (Alt+C on a dashboard, Alt+A on Outstandings) were exactly this shape: an enabled badge advertising one
    /// verb while the key ran another. On a ledger book the badge reads "Monthly"; everywhere else "Receipt".
    ///
    /// <para>It also asserts only ONE F6 row is emitted. <c>Fire()</c> takes the FIRST key match, so a second row
    /// would shadow the first and the badge would fire the wrong handler — the trap <c>BuildButtonBar</c>'s own
    /// comments record for Alt+I and Alt+A. <see cref="Assert.Single{T}(System.Collections.Generic.IEnumerable{T})"/>
    /// on the F6 rows is what holds that.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_F6_badge_says_Monthly_on_a_ledger_book_and_Receipt_everywhere_else()
    {
        var (window, vm) = NewWindow();
        try
        {
            Assert.Equal("Receipt", vm.ButtonBar.Single(b => b.Key == "F6").Caption);

            vm.OpenAccountBook(LedgerName);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Monthly", vm.ButtonBar.Single(b => b.Key == "F6").Caption);

            // Back out of the book and the badge returns to the voucher verb.
            Key(window, PhysicalKey.Escape);
            Assert.Equal("Receipt", vm.ButtonBar.Single(b => b.Key == "F6").Caption);
        }
        finally { window.Close(); }
    }
}
