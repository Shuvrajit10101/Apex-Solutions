using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
/// 🔴 <b>CENSUS ROW 8.13 — AUTO-CREATE VOUCHERS FROM AN IMPORTED BANK STATEMENT, END TO END THROUGH THE REAL
/// WINDOW</b> (<c>help.tallysolutions.com/auto-create-vouchers/</c>).
///
/// <para><b>Why these tests drive a real <see cref="MainWindow"/> and read the realised visual tree.</b> This
/// project has filed three capabilities that were built, tested and reachable by nobody — the largest ~625 lines
/// of cheque rendering whose only callers were test files. A view-model test would pass on a build where the
/// Ledger Name column and the selection tick were never templated, and an operator would then be looking at a
/// screen that cannot express the input the feature requires. So the route is walked with the ARROW KEYS from the
/// Gateway, the two vendor chords are pressed as REAL key events through the window's own handler, and the
/// controls the feature needs are found among the controls actually realised on screen.</para>
///
/// <para><b>And the safety clause is asserted as arithmetic, not as a flag.</b> F7 must leave the bank ledger's
/// closing balance untouched, because the vouchers it creates are Optional; R is the only thing that moves it.
/// Asserting <c>Optional == true</c> alone would pass on a build where nothing reads the flag.</para>
///
/// <para>Headless-safe: visual-tree and layout inspection only, no rendered frame.</para>
/// </summary>
public sealed class AutoCreateVouchersFromStatementTests
{
    /// <summary>
    /// The statement is built AROUND the company's own <c>BooksBeginFrom</c> rather than pinned to fixed
    /// calendar dates: a new company's books begin in the current financial year, so hard-coded 2024 dates made
    /// every voucher refusable by the §6.9 "date within books" rule and the fixture asserted nothing about the
    /// feature. Dates are written ISO with the invariant culture — the gate also runs on ubuntu and macos, where
    /// the ambient culture is not this machine's.
    /// </summary>
    private static string StatementCsvFor(DateOnly first, DateOnly second) =>
        "date,description,amount,instrument\n"
        + $"{Iso(first)},OFFICE RENT MAY,-40000.00,UTR9001\n"
        + $"{Iso(second)},OFFICE RENT TOPUP,-15000.00,UTR9002\n";

    private static string Iso(DateOnly d) =>
        d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

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

    /// <summary>Every realised control whose own DataContext is <paramref name="row"/> — the row's container and
    /// everything its template put inside it.</summary>
    private static List<Visual> VisualsFor(MainWindow window, object row) =>
        Descendants(window).Where(v => v is Control c && ReferenceEquals(c.DataContext, row)).ToList();

    /// <summary>Arrows down the ACTIVE column until the highlighted row carries this label, then drills in.</summary>
    private static void ArrowToAndDrill(MainWindowViewModel vm, string label)
    {
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            if (vm.Menu[vm.SelectedIndex].Label == label) { vm.DrillIn(); return; }
            vm.MoveDown();
        }
        Assert.Fail($"'{label}' was not reachable by arrow navigation in the active column.");
    }

    private sealed record Fixture(
        MainWindow Window, MainWindowViewModel Vm, DomainLedger Bank, DomainLedger Rent, string TempDir,
        DateOnly FirstLine, DateOnly SecondLine, DateOnly AsOf);

    /// <summary>
    /// Opens the app, seeds a bank + an expense ledger, then walks Gateway → Banking → <b>Import Bank
    /// Statement</b> with the arrow keys — the operator's own route — and imports the fixture statement.
    /// </summary>
    private static Fixture Open()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexAutoVch_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
        window.Show();

        vm.NewCompanyName = "Auto Vch Desktop Co";
        vm.CreateCompany();

        var bank = new DomainLedger(Guid.NewGuid(), "HDFC Bank",
            vm.Company!.FindGroupByName("Bank Accounts")!.Id, Money.FromRupees(500000m), openingIsDebit: true);
        vm.Company.AddLedger(bank);

        var rent = new DomainLedger(Guid.NewGuid(), "Rent",
            vm.Company.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, openingIsDebit: true);
        vm.Company.AddLedger(rent);

        // The menu route, by keyboard, exactly as it is nested: Gateway → Banking → Import Bank Statement.
        vm.ShowGateway();
        ArrowToAndDrill(vm, "Banking");
        Assert.Equal(GatewayMenu.Banking, vm.CurrentGatewayMenu);
        ArrowToAndDrill(vm, "Import Bank Statement");
        Assert.Equal(Screen.BankStatementImport, vm.CurrentScreen);

        var booksBegin = vm.Company.BooksBeginFrom;
        var first = booksBegin.AddDays(31);
        var second = booksBegin.AddDays(38);

        var page = vm.BankStatementImport!;
        page.SelectedBank = page.BankLedgers.Single(l => l.Id == bank.Id);
        page.ImportFromText(StatementCsvFor(first, second));
        Pump(window);

        return new Fixture(window, vm, bank, rent, tempDir, first, second, booksBegin.AddYears(1));
    }

    private static void Cleanup(Fixture f)
    {
        f.Window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(f.TempDir)) Directory.Delete(f.TempDir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ================================================================ the input surface exists on screen

    /// <summary>
    /// 🔴 The two controls the feature CANNOT work without — the selection tick and the vendor's <b>Ledger
    /// Name</b> picker — are realised on the unmatched statement rows. Without them the chords below have nothing
    /// to act on and the row is a dead menu entry, which is the exact shape this project has filed three times.
    /// </summary>
    [AvaloniaFact]
    public void The_unmatched_statement_rows_offer_a_selection_tick_and_a_ledger_name_picker()
    {
        var f = Open();
        try
        {
            var unmatched = f.Vm.BankStatementImport!.Results.Where(r => r.CanCreateVoucher).ToList();
            Assert.Equal(2, unmatched.Count);

            foreach (var row in unmatched)
            {
                var visuals = VisualsFor(f.Window, row);

                Assert.True(
                    visuals.OfType<CheckBox>().Any(c => c.IsEffectivelyVisible && c.Bounds.Width > 0),
                    $"The unmatched statement line '{row.Description}' has no visible selection tick on screen, "
                    + "so F7 (Create Vch/Multi-Vch) can never be given anything to act on.");

                var picker = visuals.OfType<ComboBox>()
                    .FirstOrDefault(c => c.IsEffectivelyVisible && c.Bounds.Width > 0);
                Assert.True(picker != null,
                    $"The unmatched statement line '{row.Description}' has no visible Ledger Name picker. The "
                    + "vendor requires a Ledger Name per transaction before a voucher can be created, and a bank "
                    + "line cannot supply one, so without this control the feature is unreachable.");
                Assert.NotEmpty(picker!.ItemsSource!.Cast<object>());
            }

            // A MATCHED row must NOT offer them: it already has a voucher, and creating a second would double it.
            var matched = f.Vm.BankStatementImport!.Results.FirstOrDefault(r => !r.CanCreateVoucher);
            if (matched is not null)
                Assert.DoesNotContain(VisualsFor(f.Window, matched).OfType<ComboBox>(),
                    c => c.IsEffectivelyVisible);
        }
        finally { Cleanup(f); }
    }

    // ================================================================ F7 — and the safety property

    /// <summary>
    /// 🔴 <b>THE ONE THAT MATTERS.</b> F7 pressed as a real key event creates the vouchers, marks them Optional,
    /// and moves NO money: the bank ledger's closing balance is byte-identical afterwards. Then R posts them and
    /// the balance moves by exactly the statement amount — once.
    /// </summary>
    [AvaloniaFact]
    public void F7_creates_optional_vouchers_that_move_no_money_until_R_regularises_them()
    {
        var f = Open();
        try
        {
            var page = f.Vm.BankStatementImport!;
            var company = f.Vm.Company!;
            var asOf = f.AsOf;
            var before = LedgerBalances.SignedClosing(company, f.Bank, asOf);

            var rows = page.Results.Where(r => r.CanCreateVoucher).ToList();
            foreach (var r in rows)
            {
                r.SelectedLedger = r.LedgerChoices.Single(l => l.Id == f.Rent.Id);
                r.IsSelected = true;
            }
            Pump(f.Window);

            f.Window.KeyPressQwerty(PhysicalKey.F7, RawInputModifiers.None);
            Pump(f.Window);

            Assert.Equal(2, company.Vouchers.Count(v => v.Optional));
            Assert.Equal(2, page.OptionalVouchers.Count);
            Assert.True(page.HasOptionalVouchers);

            // 🔴 Nothing reached the books.
            Assert.Equal(before, LedgerBalances.SignedClosing(company, f.Bank, asOf));
            Assert.Empty(BankReconciliation.Transactions(company, f.Bank, asOf));

            // The pending queue is on screen, not merely in a collection.
            foreach (var pending in page.OptionalVouchers)
                Assert.True(
                    VisualsFor(f.Window, pending).OfType<CheckBox>().Any(c => c.IsEffectivelyVisible),
                    "A pending Optional voucher has no visible row on the "
                    + "'Bank Reconciliation – Optional Vouchers' section, so R can never be aimed at it.");

            // R — Mark as Regular & Reconcile.
            foreach (var pending in page.OptionalVouchers) pending.IsSelected = true;
            Pump(f.Window);
            f.Window.KeyPressQwerty(PhysicalKey.R, RawInputModifiers.None);
            Pump(f.Window);

            Assert.DoesNotContain(company.Vouchers, v => v.Optional);
            Assert.False(page.HasOptionalVouchers);

            // 55,000 left the bank — once, and reconciled with the statement's own dates.
            Assert.Equal(before - 55000m, LedgerBalances.SignedClosing(company, f.Bank, asOf));
            var brs = BankReconciliation.Transactions(company, f.Bank, asOf);
            Assert.Equal(2, brs.Count);
            Assert.All(brs, t => Assert.True(t.IsReconciled));
            Assert.Contains(brs, t => t.BankDate == f.FirstLine);
            Assert.Contains(brs, t => t.BankDate == f.SecondLine);
        }
        finally { Cleanup(f); }
    }

    /// <summary>
    /// Alt+F7 "Create Voucher(Consolidate)" — one voucher for the combined amount. Pressed as a real key event,
    /// which also pins that the page's chord WINS over the global Alt+F7 (Stock Journal): without the
    /// page-scoped arm this keystroke would navigate away to an inventory voucher instead.
    /// </summary>
    [AvaloniaFact]
    public void Alt_F7_consolidates_the_selection_into_one_optional_voucher_and_does_not_open_a_stock_journal()
    {
        var f = Open();
        try
        {
            var page = f.Vm.BankStatementImport!;
            foreach (var r in page.Results.Where(r => r.CanCreateVoucher))
            {
                r.SelectedLedger = r.LedgerChoices.Single(l => l.Id == f.Rent.Id);
                r.IsSelected = true;
            }
            Pump(f.Window);

            f.Window.KeyPressQwerty(PhysicalKey.F7, RawInputModifiers.Alt);
            Pump(f.Window);

            Assert.Equal(Screen.BankStatementImport, f.Vm.CurrentScreen);   // still here, not on a Stock Journal
            var pending = Assert.Single(page.OptionalVouchers);
            var voucher = f.Vm.Company!.FindVoucher(pending.VoucherId)!;
            Assert.True(voucher.Optional);
            Assert.Equal(Money.FromRupees(55000m), voucher.Lines.Single(l => l.LedgerId == f.Bank.Id).Amount);
            Assert.Equal(f.SecondLine, voucher.Date);                       // the latest constituent
        }
        finally { Cleanup(f); }
    }

    /// <summary>
    /// A selected line with no Ledger Name creates NOTHING and says why. Guessing a ledger would be a misposting
    /// the operator never chose, and creating the others silently would leave a half-done batch.
    /// </summary>
    [AvaloniaFact]
    public void F7_refuses_a_selection_whose_ledger_name_is_blank()
    {
        var f = Open();
        try
        {
            var page = f.Vm.BankStatementImport!;
            var rows = page.Results.Where(r => r.CanCreateVoucher).ToList();
            rows[0].SelectedLedger = rows[0].LedgerChoices.Single(l => l.Id == f.Rent.Id);
            rows[0].IsSelected = true;
            rows[1].IsSelected = true;                                      // left without a ledger
            Pump(f.Window);

            f.Window.KeyPressQwerty(PhysicalKey.F7, RawInputModifiers.None);
            Pump(f.Window);

            Assert.Empty(f.Vm.Company!.Vouchers);
            Assert.Contains("no Ledger Name", page.Message);
        }
        finally { Cleanup(f); }
    }

    /// <summary>
    /// 🔴 <b>LEGIBILITY: every column heading on both new bands renders in full.</b> This wave added two columns
    /// to the statement grid (the selection tick and the vendor's Ledger Name) and a whole second band for the
    /// pending queue. Fixed-width columns plus a longer heading is precisely how this project has repeatedly
    /// shipped headings sliced to "Ledger Na…", and a heading you cannot read is a column you cannot use.
    ///
    /// <para>The measurement is Avalonia's OWN text layout at the shipped font size and weight, compared against
    /// the width the realised header actually got — not a monospace estimate, because these bands are set in the
    /// proportional UI face.</para>
    /// </summary>
    [AvaloniaFact]
    public void Every_column_heading_on_the_statement_and_pending_bands_fits_its_column()
    {
        var f = Open();
        try
        {
            // Put a voucher in the pending queue so its band is realised and measurable too.
            var page = f.Vm.BankStatementImport!;
            var row = page.Results.First(r => r.CanCreateVoucher);
            row.SelectedLedger = row.LedgerChoices.Single(l => l.Id == f.Rent.Id);
            row.IsSelected = true;
            page.CreateVouchersForSelection();
            Pump(f.Window);
            Assert.True(page.HasOptionalVouchers);

            // The two tick columns carry NO caption — a checkbox column needs none, and a three-letter heading
            // that still did not fit was buying nothing. Every caption that IS on screen must be readable.
            var headings = new[]
            {
                "Status", "Date", "Description", "Instrument", "Amount", "Bank Date", "Ledger Name",
                "Vch No.", "Type", "Narration",
            };

            var clipped = new List<string>();
            var seen = 0;
            foreach (var text in headings)
            {
                var header = Descendants(f.Window).OfType<TextBlock>().FirstOrDefault(t =>
                    t.IsEffectivelyVisible && t.Text == text && t.Classes.Contains("colHdr"));
                if (header is null) continue;               // that heading is on the other band; measured there
                seen++;

                var probe = new TextBlock
                {
                    Text = text,
                    FontSize = header.FontSize,
                    FontWeight = header.FontWeight,
                    FontFamily = header.FontFamily,
                };
                probe.Measure(Size.Infinity);

                // The band's cells carry an 8px lead-in; a heading needs its glyphs plus that.
                if (probe.DesiredSize.Width + 8 > header.Bounds.Width + 0.5)
                    clipped.Add($"'{text}' needs {probe.DesiredSize.Width + 8:0.#}px and has "
                                + $"{header.Bounds.Width:0.#}px");
            }

            // Guard the guard: if none of the captions were found, this test would pass by measuring nothing.
            Assert.True(seen >= 9, $"Only {seen} of the {headings.Length} column captions were realised, so this "
                                   + "legibility check measured almost nothing. Look at the band templates.");

            Assert.True(clipped.Count == 0,
                "Column headings are sliced on the Import Bank Statement page, so the operator cannot read what "
                + "the column is: " + string.Join("; ", clipped));
        }
        finally { Cleanup(f); }
    }

    /// <summary>The statement's own narration is carried onto the voucher, so the reviewer on the pending queue
    /// can see what the bank actually said rather than a caption this app invented.</summary>
    [AvaloniaFact]
    public void The_pending_queue_shows_the_bank_narration_and_the_chosen_ledger()
    {
        var f = Open();
        try
        {
            var page = f.Vm.BankStatementImport!;
            var row = page.Results.First(r => r.CanCreateVoucher);
            row.SelectedLedger = row.LedgerChoices.Single(l => l.Id == f.Rent.Id);
            row.IsSelected = true;
            Pump(f.Window);

            f.Window.KeyPressQwerty(PhysicalKey.F7, RawInputModifiers.None);
            Pump(f.Window);

            var pending = Assert.Single(page.OptionalVouchers);
            Assert.Equal("Rent", pending.LedgerName);
            Assert.Equal("OFFICE RENT MAY", pending.Narration);
            Assert.Equal("Payment", pending.VoucherType);
            Assert.Contains("pending", page.OptionalVouchersHeading);
            Assert.Contains("not in your balances", page.OptionalVouchersHeading);
        }
        finally { Cleanup(f); }
    }
}
