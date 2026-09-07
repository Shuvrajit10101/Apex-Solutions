using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Census row 16.5 — Split Company Data</b>, the shell half: the way in, the realised screen, and the
/// end-to-end write.
///
/// <para>The engine identities live in <c>Apex.Ledger.Tests.CompanySplitTests</c> (the two fixtures reconciling
/// to the unsplit book). These tests pin what an engine test cannot:</para>
/// <list type="bullet">
/// <item><b>An operator can reach it from the keyboard</b> — Gateway → Data → Split → Verify Data / Split Data,
///   through the real cascade. A split engine with no route in would move no census row.</item>
/// <item><b>The screen actually realises the vendor's three options</b>, checked on the REALISED VISUAL TREE
///   rather than on a view-model flag — asserting the flag is the test that passes on a build whose template
///   draws nothing (the census 13.6 lesson).</item>
/// <item><b>Running the split writes new company files and leaves the original book byte-identical</b> — the
///   property this row exists to keep, measured on the source file's bytes.</item>
/// </list>
/// </summary>
public sealed class SplitCompanyDataTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public SplitCompanyDataTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexSplitUiTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(Path.Combine(_tempDir, "Companies"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); return; }
            catch (IOException) { SqliteConnection.ClearAllPools(); System.Threading.Thread.Sleep(25); }
            catch (UnauthorizedAccessException) { SqliteConnection.ClearAllPools(); System.Threading.Thread.Sleep(25); }
        }
    }

    private static readonly DateOnly SplitDate = new(2026, 4, 1);
    private static readonly DateOnly Cutoff = new(2026, 3, 31);
    private static readonly DateOnly End = new(2026, 6, 30);

    /// <summary>
    /// A book that straddles a financial-year boundary, with odd paisa throughout so a rounding defect cannot
    /// hide behind a round number: trading in FY 2025-26 and again in FY 2026-27, split at 01-Apr-2026.
    /// </summary>
    private static Company StraddlingCompany(string name)
    {
        var c = CompanyFactory.CreateSeeded(name, new DateOnly(2025, 4, 1));

        var cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(21_306.41m);
        cash.OpeningIsDebit = true;

        var capital = new Domain.Ledger(Guid.NewGuid(), "Capital A/c",
            c.FindGroupByName("Capital Account")!.Id, Money.FromRupees(21_306.41m), openingIsDebit: false);
        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales",
            c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
        var rent = new Domain.Ledger(Guid.NewGuid(), "Rent",
            c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, openingIsDebit: true);
        var debtor = new Domain.Ledger(Guid.NewGuid(), "Harsha Enterprises",
            c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(capital); c.AddLedger(sales); c.AddLedger(rent); c.AddLedger(debtor);

        var journal = c.FindVoucherTypeByName("Journal")!;
        var receipt = c.FindVoucherTypeByName("Receipt")!;
        var payment = c.FindVoucherTypeByName("Payment")!;
        var svc = new LedgerService(c);

        // ---- before the split date
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2025, 7, 9), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(47_606.23m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(47_606.23m), DrCr.Credit),
        }));
        svc.Post(new Voucher(Guid.NewGuid(), receipt.Id, new DateOnly(2025, 11, 21), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(30_939.87m), DrCr.Debit),
            new EntryLine(debtor.Id, Money.FromRupees(30_939.87m), DrCr.Credit),
        }));
        svc.Post(new Voucher(Guid.NewGuid(), payment.Id, new DateOnly(2026, 2, 3), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(8_411.19m), DrCr.Debit),
            new EntryLine(cash.Id, Money.FromRupees(8_411.19m), DrCr.Credit),
        }));

        // ---- on and after the split date (the first lands exactly ON it)
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, SplitDate, new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(12_004.77m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(12_004.77m), DrCr.Credit),
        }));
        svc.Post(new Voucher(Guid.NewGuid(), payment.Id, new DateOnly(2026, 5, 18), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(3_277.31m), DrCr.Debit),
            new EntryLine(cash.Id, Money.FromRupees(3_277.31m), DrCr.Credit),
        }));

        return c;
    }

    private MainWindowViewModel ShellWith(Company company)
    {
        _storage.Save(company);
        var vm = new MainWindowViewModel(_storage);
        vm.ShowCompanySelect();
        vm.Menu.Single(m => m.Label == company.Name).Activate();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    private static void SelectByLabel(GatewayColumn column, string label)
    {
        for (var i = 0; i < column.Items.Count; i++)
            if (column.Items[i].IsSelectable && column.Items[i].Label == label)
            {
                column.SetSelected(i);
                return;
            }
        throw new Xunit.Sdk.XunitException($"No selectable row labelled '{label}' in column '{column.Title}'.");
    }

    // ================================================================= the way in

    /// <summary>
    /// 🔴 <b>The test that fails on today's main.</b> The Gateway's Data section carried exactly one child,
    /// "Backup / Restore"; the vendor's route is <i>"Alt+Y (Data) &gt; Split &gt; Verify Data"</i> and
    /// <i>"… &gt; Split &gt; Split Data"</i>, so the section needs a Split group with those two pages under it,
    /// reachable by the ordinary arrows-and-Enter cascade.
    /// </summary>
    [Fact]
    public void Data_menu_offers_a_Split_Company_Data_group_with_Verify_Data_and_Split_Data()
    {
        var vm = ShellWith(StraddlingCompany("Route Co"));

        Assert.Contains("Data", vm.Menu.Where(m => m.IsHeader).Select(m => m.Label));
        var split = vm.Menu.Single(m => m.Label == "Split Company Data");
        Assert.True(split.IsSelectable);

        SelectByLabel(vm.Columns[0], "Split Company Data");
        vm.DrillIn();

        Assert.Equal("Split Company Data", vm.Columns[^1].Title);
        Assert.Equal(GatewayMenu.Split, vm.CurrentGatewayMenu);
        Assert.Equal(
            new[] { "Verify Data", "Split Data" },
            vm.Columns[^1].Items.Where(i => i.IsSelectable).Select(i => i.Label).ToArray());

        SelectByLabel(vm.Columns[^1], "Verify Data");
        vm.DrillIn();
        Assert.Equal(Screen.VerifyData, vm.CurrentScreen);
        Assert.NotNull(vm.VerifyDataPanel);
        Assert.Same(vm.VerifyDataPanel, vm.Columns[^1].VerifyDataPanel);

        vm.Back();
        SelectByLabel(vm.Columns[^1], "Split Data");
        vm.DrillIn();
        Assert.Equal(Screen.SplitCompany, vm.CurrentScreen);
        Assert.NotNull(vm.SplitCompanyPanel);
        Assert.Same(vm.SplitCompanyPanel, vm.Columns[^1].SplitCompanyPanel);
    }

    [Fact]
    public void Verify_Data_reports_a_sound_book_as_sound()
    {
        var vm = ShellWith(StraddlingCompany("Verify Co"));
        vm.OpenVerifyData();

        Assert.True(vm.ApplyVerifyData(), vm.VerifyDataPanel!.Status);
        Assert.True(vm.VerifyDataPanel!.HasRun);
        Assert.Empty(vm.VerifyDataPanel.Findings);
        Assert.Contains("No errors found", vm.VerifyDataPanel.Status, StringComparison.Ordinal);
    }

    // ================================================================= the end-to-end write

    /// <summary>
    /// The whole row, driven through the panel: two new books appear, the original's FILE IS BYTE-IDENTICAL to
    /// what it was, and the two halves reconcile to the unsplit book when they are re-opened from disk.
    /// </summary>
    [Fact]
    public void Splitting_writes_two_new_books_and_leaves_the_original_byte_identical()
    {
        var source = StraddlingCompany("Straddle Co");
        var vm = ShellWith(source);

        var sourcePath = _storage.PathForName("Straddle Co");
        var beforeBytes = File.ReadAllBytes(sourcePath);
        var closingsBefore = ClosingsOf(_storage.Load(new CompanyEntry("Straddle Co", sourcePath)), End);

        vm.OpenSplitCompany();
        var panel = vm.SplitCompanyPanel!;
        panel.SplitDateText = "01-Apr-2026";
        panel.IntoTwoCompanies = true;

        Assert.True(vm.ApplySplitCompany(), panel.Status + " | " + string.Join(" | ", panel.Refusals));
        Assert.Equal(2, panel.Created.Count);
        Assert.Contains("is unchanged", panel.Status, StringComparison.Ordinal);

        // The original file was not written to at all.
        Assert.Equal(beforeBytes, File.ReadAllBytes(sourcePath));

        // Both new books are on disk and openable…
        var names = _storage.ListCompanies().Select(e => e.Name).ToList();
        Assert.Contains("Straddle Co", names);
        var beforeEntry = _storage.ListCompanies().Single(e => e.Name == panel.Created[0]);
        var fromEntry = _storage.ListCompanies().Single(e => e.Name == panel.Created[1]);
        var beforeBook = _storage.Load(beforeEntry);
        var fromBook = _storage.Load(fromEntry);

        // …and they reconcile to the unsplit book: every balance-sheet ledger's closing at the end date is what
        // the original reports, and the P&L ledgers are partitioned across the two halves.
        foreach (var (name, original) in closingsBefore)
        {
            var ledger = fromBook.FindLedgerByName(name)!;
            if (ClassificationRules.IsProfitAndLossLedger(ledger, fromBook))
            {
                Assert.Equal(original,
                    LedgerBalances.SignedClosing(beforeBook, beforeBook.FindLedgerByName(name)!, Cutoff)
                    + LedgerBalances.SignedClosing(fromBook, ledger, End));
            }
            else if (name != "Profit & Loss A/c")
            {
                Assert.Equal(original, LedgerBalances.SignedClosing(fromBook, ledger, End));
            }
        }

        // The new books balance on their own.
        foreach (var (book, asOf) in new[] { (beforeBook, Cutoff), (fromBook, End) })
        {
            var tb = TrialBalance.Build(book, asOf);
            Assert.Equal(tb.TotalDebit, tb.TotalCredit);
        }

        // The from-book begins on the split date, and the voucher dated ON it is in that book, not the other.
        Assert.Equal(SplitDate, fromBook.BooksBeginFrom);
        Assert.Contains(fromBook.Vouchers, v => v.Date == SplitDate);
        Assert.DoesNotContain(beforeBook.Vouchers, v => v.Date >= SplitDate);
    }

    /// <summary>
    /// 🔴 The refusal that keeps the guarantee true: a new book named the same as the book being split would be
    /// written to the same file, so it is refused before anything is written.
    /// </summary>
    [Fact]
    public void A_new_name_that_would_overwrite_the_company_being_split_is_refused()
    {
        var vm = ShellWith(StraddlingCompany("Overwrite Co"));
        var sourcePath = _storage.PathForName("Overwrite Co");
        var beforeBytes = File.ReadAllBytes(sourcePath);

        vm.OpenSplitCompany();
        var panel = vm.SplitCompanyPanel!;
        panel.SplitDateText = "01-Apr-2026";
        panel.IntoTwoCompanies = true;
        panel.BeforeName = "Overwrite Co";

        Assert.False(vm.ApplySplitCompany());
        Assert.Contains(panel.Refusals, r => r.Contains("overwrite the original", StringComparison.Ordinal));
        Assert.Empty(panel.Created);
        Assert.Equal(beforeBytes, File.ReadAllBytes(sourcePath));
        Assert.Single(_storage.ListCompanies());     // no second book was written
    }

    [Fact]
    public void A_split_date_outside_the_book_is_refused_on_the_panel()
    {
        var vm = ShellWith(StraddlingCompany("Range Co"));
        vm.OpenSplitCompany();
        var panel = vm.SplitCompanyPanel!;
        panel.SplitDateText = "01-Apr-2030";
        panel.IntoTwoCompanies = true;

        Assert.False(vm.ApplySplitCompany());
        Assert.NotEmpty(panel.Refusals);
        Assert.Single(_storage.ListCompanies());
    }

    [Fact]
    public void An_unparseable_split_date_is_refused_before_anything_is_loaded()
    {
        var vm = ShellWith(StraddlingCompany("Bad Date Co"));
        vm.OpenSplitCompany();
        var panel = vm.SplitCompanyPanel!;
        panel.SplitDateText = "not a date";

        Assert.False(vm.ApplySplitCompany());
        Assert.Contains(panel.Refusals, r => r.Contains("is not a date", StringComparison.Ordinal));
    }

    /// <summary>One option only: choosing "From Split Date" writes exactly one new book.</summary>
    [Fact]
    public void From_split_date_writes_one_book_only()
    {
        var vm = ShellWith(StraddlingCompany("One Half Co"));
        vm.OpenSplitCompany();
        var panel = vm.SplitCompanyPanel!;
        panel.SplitDateText = "01-Apr-2026";
        panel.FromSplitDate = true;

        Assert.True(vm.ApplySplitCompany(), panel.Status + " | " + string.Join(" | ", panel.Refusals));
        Assert.Single(panel.Created);
        Assert.Equal(2, _storage.ListCompanies().Count);   // the original + the one new book
    }

    private static List<(string Name, decimal Signed)> ClosingsOf(Company c, DateOnly asOf) =>
        c.Ledgers.Select(l => (l.Name, LedgerBalances.SignedClosing(c, l, asOf))).ToList();

    // ================================================================= the realised screen

    /// <summary>
    /// 🔴 <b>Asserted on the REALISED VISUAL TREE, not on the view model.</b> The three options are the vendor's
    /// own wording — <i>"Before Split Date"</i>, <i>"From Split Date"</i>, <i>"Into Two Companies"</i> — and a
    /// panel that binds them but draws none of them is a screen the operator cannot use. That is exactly the
    /// defect a view-model assertion would have passed through.
    /// </summary>
    [AvaloniaFact]
    public void The_split_screen_realises_the_vendors_three_options_and_both_name_fields()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexSplitTree_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
        window.Show();
        try
        {
            vm.NewCompanyName = "Realised Split Co";
            vm.CreateCompany();
            vm.OpenSplitCompany();
            Pump(window);

            var radios = Descendants(window)
                .OfType<RadioButton>()
                .Where(r => r.IsEffectivelyVisible)
                .Select(r => r.Content as string)
                .Where(t => t is not null)
                .ToList();

            foreach (var option in new[] { "Before Split Date", "From Split Date", "Into Two Companies" })
                Assert.True(radios.Contains(option),
                    $"The Split Data screen does not realise the option '{option}'. The operator cannot choose " +
                    "it, whatever the view model exposes. Realised options: " + string.Join(", ", radios));

            // Both name fields are realised and carry the defaults the panel generated.
            var boxes = Descendants(window)
                .OfType<TextBox>()
                .Where(t => t.IsEffectivelyVisible)
                .Select(t => t.Text ?? string.Empty)
                .ToList();
            Assert.Contains(boxes, t => t.Contains("Realised Split Co (to ", StringComparison.Ordinal));
            Assert.Contains(boxes, t => t.Contains("Realised Split Co (from ", StringComparison.Ordinal));
        }
        finally
        {
            window.Close();
            SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }
}
