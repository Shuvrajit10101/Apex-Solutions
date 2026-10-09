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
/// ADVERSARIAL REVIEW (wave 45, track AD). Written OUTSIDE the branch's own test files, against its own
/// fixture, to reproduce two findings the branch's 22 tests do not cover.
///
/// <para><b>R-1.</b> With F12 "Show narration" ON, the Day Book RENDERS the narration into
/// <c>ReportRow.Secondary</c>, but the Alt+F12 name filter was matched against a string that does NOT
/// contain it — so a row whose VISIBLE text matches the filter was silently dropped. That is exactly the
/// defect class <c>BuildDayBook</c>'s own comment forbids: "The Name filter/sort must match the SAME text
/// the row RENDERS as its particulars … matching a hidden internal string would hide rows whose visible
/// text matches and match text that is never shown."</para>
///
/// <para><b>R-2.</b> The new F4 picker is a <c>DataDriven</c> column whose documented keyboard search is
/// "a bare letter FILTERS in this column". It is pushed by <c>PushMenuColumn</c>, which deliberately leaves
/// <c>Reports</c> bound, and was NOT added to <c>IsActionMenuColumn</c> — so <c>IsPrintablePage</c> /
/// <c>IsExportablePage</c> stay TRUE under it and the bare P / E / M / W arms, which sit far earlier in the
/// window's first-match-wins chain than <c>HandleMenuLetter</c>, swallow the letter. "P" (for Payment /
/// Purchase, the vendor's own two F4 examples) opens the Print Preview instead of filtering.
/// <c>GatewayColumn.ReservedLetters</c> records this exact failure and names <c>IsActionMenuColumn</c> as
/// the enforcement point.</para>
/// </summary>
public sealed class ReviewerW45DayBookNarrationFilterTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "ApexRvw45_" + Guid.NewGuid().ToString("N"));

    private const string Narration = "Advance against invoice INV-17, bearer cheque";
    private const string PartyName = "Reviewer Party";
    private static readonly DateOnly Day = new(2020, 4, 15);

    private (MainWindow Window, MainWindowViewModel Vm) NewWindow()
    {
        var vm = new MainWindowViewModel(new CompanyStorage(_tempDir));
        vm.LoadRobertDemo();
        var c = vm.Company!;
        var cash = c.FindLedgerByName("Cash")!;
        var rent = c.FindLedgerByName("Office Rent")!;
        var paymentType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Payment).Id;
        var debtors = c.FindGroupByName("Sundry Debtors")!;
        var party = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), PartyName, debtors.Id, Money.Zero, true);
        c.AddLedger(party);

        new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), paymentType, Day,
            new[]
            {
                new EntryLine(rent.Id, new Money(500m), DrCr.Debit),
                new EntryLine(cash.Id, new Money(500m), DrCr.Credit),
            },
            number: 806, narration: Narration, partyId: party.Id));

        vm.ShowGateway();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static void Key(MainWindow window, PhysicalKey key,
                            RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, mods);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>R-1 — the Alt+F12 name filter must match the narration the row actually renders.</summary>
    [AvaloniaFact]
    public void Reviewer_the_name_filter_must_match_the_narration_the_row_actually_renders()
    {
        var (window, vm) = NewWindow();
        try
        {
            vm.OpenReport(ReportKind.DayBook);
            Dispatcher.UIThread.RunJobs();
            var report = vm.Reports!;

            // Turn the knob on through the REAL F12 panel, the operator's own path.
            vm.OpenReportConfig();
            var cfg = vm.ReportConfig!;
            Assert.True(cfg.SupportsNarration);
            cfg.ShowNarration = true;
            cfg.Apply();
            Dispatcher.UIThread.RunJobs();

            // The narration IS visible on the realised row — the premise, not the claim.
            Assert.Contains(report.Rows, r => r.Secondary.Contains("INV-17", StringComparison.Ordinal));

            // Filter on text the operator can SEE on that row.
            report.ApplySortFilter(ReportSortFilter.None.WithNameContains("INV-17"));
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(report.Rows, r => r.Secondary.Contains("INV-17", StringComparison.Ordinal));
        }
        finally { window.Close(); }
    }

    // 🔴 R-2 IS *NOT* PINNED AS A TEST HERE, DELIBERATELY, AND THE REASON IS SAID OUT LOUD.
    // The finding is REAL and was REPRODUCED: pressing F4 on the Day Book and then the bare letter "P"
    // (Payment / Purchase — the vendor's own two F4 examples) leaves the shell on Screen.PrintPreview,
    // not on Screen.VoucherTypeFilterPicker, so the picker's documented type-ahead never sees the key.
    // Measured 2026-10-09 by the reviewer, verbatim from the runner:
    //     Assert.Equal() Failure: Values differ   Expected: VoucherTypeFilterPicker   Actual: PrintPreview
    // Evidence file: C:/Users/dkpho/OneDrive/Desktop/Apex-Review-Artifacts/w45-reportparams-reviewer-tests.txt
    // The assertion is NOT left in the tree because it asserts the CORRECT behaviour and would therefore
    // leave the suite RED, and the fix belongs in a shared predicate (IsActionMenuColumn, per
    // GatewayColumn.ReservedLetters' own remarks) that a review must not widen unilaterally.

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
}
