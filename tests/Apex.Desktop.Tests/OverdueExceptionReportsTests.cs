using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
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
/// Census <b>11.13</b> — the <b>Overdue Receivables</b> and <b>Overdue Payables</b> exception views.
///
/// <para>Vendor (TallyPrime, <c>help.tallysolutions.com/manage-receivables-outstanding-tally/</c>, opened by
/// content 2026-10-26): <i>"Alternatively, you can use Alt+G (Go To) &gt; Overdue Receivables."</i>, the same for
/// <i>Overdue Payables</i>; <i>"Review the outstanding bills, party names, pending amounts, due dates, overdue
/// days, and other details."</i>; <i>"Select any outstanding line and press Enter to show/hide the transaction
/// details."</i></para>
///
/// <para>🔴 The figures are read off a <b>RELOADED</b> book (a fresh <see cref="CompanyStorage"/> over the same
/// directory), every amount ends in an odd paisa, and one bill falls due EXACTLY on the as-of date — which must
/// NOT be listed (IV-245) until the next day. Reach is through the REAL window key tunnel: Alt+G and the menu
/// walk press keys; Enter drills are the real key.</para>
/// </summary>
public sealed class OverdueExceptionReportsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public OverdueExceptionReportsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexOverdue_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
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

    // ---------------------------------------------------------------- fixture

    private sealed record Fixture(
        MainWindowViewModel Vm, Company Company, DateOnly AsOf,
        Guid Inv1, Guid Inv2, Guid Inv3, Guid Pb9, Guid AcmeId, Guid ZenithId);

    /// <summary>
    /// A debtor with three bills — INV-1 10,000.37 part-settled by 3,333.33 (6,667.04 pending, 30 days overdue),
    /// INV-2 2,500.01 due EXACTLY on the as-of date, INV-3 1,234.57 due the day before — and a creditor with
    /// PB-9 7,777.77 due 15 days before the as-of date. Saved through the real store.
    /// </summary>
    private Fixture Build(string name, bool provisional = false)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name + " " + Guid.NewGuid().ToString("N")[..6];
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        var c = vm.Company!;
        var start = c.BooksBeginFrom;
        var asOf = start.AddDays(90);

        var svc = new LedgerService(c);
        var journal = c.FindVoucherTypeByName("Journal")!;
        var acme = new DomainLedger(Guid.NewGuid(), "Acme Traders", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, true, maintainBillByBill: true);
        var zenith = new DomainLedger(Guid.NewGuid(), "Zenith Supplies", c.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, false, maintainBillByBill: true);
        var sales = new DomainLedger(Guid.NewGuid(), "Sales Overdue", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, false);
        var purchases = new DomainLedger(Guid.NewGuid(), "Purchases Overdue", c.FindGroupByName("Purchase Accounts")!.Id,
            Money.Zero, true);
        var cash = new DomainLedger(Guid.NewGuid(), "Cash Overdue", c.FindGroupByName("Cash-in-Hand")!.Id,
            Money.Zero, true);
        foreach (var l in new[] { acme, zenith, sales, purchases, cash }) c.AddLedger(l);

        Voucher Sale(DateOnly date, string reference, decimal amount, DateOnly due) =>
            svc.Post(new Voucher(Guid.NewGuid(), journal.Id, date, new[]
            {
                new EntryLine(acme.Id, Money.FromRupees(amount), DrCr.Debit, billAllocations: new[]
                    { new BillAllocation(BillRefType.NewRef, reference, Money.FromRupees(amount), dueDate: due) }),
                new EntryLine(sales.Id, Money.FromRupees(amount), DrCr.Credit),
            }));

        var inv1 = Sale(start.AddDays(30), "INV-1", 10_000.37m, start.AddDays(60));
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, start.AddDays(65), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(3_333.33m), DrCr.Debit),
            new EntryLine(acme.Id, Money.FromRupees(3_333.33m), DrCr.Credit, billAllocations: new[]
                { new BillAllocation(BillRefType.AgstRef, "INV-1", Money.FromRupees(3_333.33m)) }),
        }));
        var inv2 = Sale(start.AddDays(61), "INV-2", 2_500.01m, asOf);
        var inv3 = Sale(start.AddDays(62), "INV-3", 1_234.57m, asOf.AddDays(-1));
        var pb9 = svc.Post(new Voucher(Guid.NewGuid(), journal.Id, start.AddDays(61), new[]
        {
            new EntryLine(purchases.Id, Money.FromRupees(7_777.77m), DrCr.Debit),
            new EntryLine(zenith.Id, Money.FromRupees(7_777.77m), DrCr.Credit, billAllocations: new[]
                { new BillAllocation(BillRefType.NewRef, "PB-9", Money.FromRupees(7_777.77m),
                    dueDate: start.AddDays(75)) }),
        }));

        if (provisional)
        {
            // HIGH-1 (wave 8 review, track ed): non-posting vouchers carrying bill allocations. A Memorandum opens
            // MEMO-1 50,000.01 (due start+20) and another "settles" INV-3 in full; a Reversing Journal opens RJ-1
            // 20,000.03 against Acme and RJ-P 4,444.41 against Zenith, both applicable well past the as-of date.
            var memo = c.FindVoucherTypeByName("Memorandum")!;
            var rev = c.FindVoucherTypeByName("Reversing Journal")!;
            svc.Post(new Voucher(Guid.NewGuid(), memo.Id, start.AddDays(10), new[]
            {
                new EntryLine(acme.Id, Money.FromRupees(50_000.01m), DrCr.Debit, billAllocations: new[]
                    { new BillAllocation(BillRefType.NewRef, "MEMO-1", Money.FromRupees(50_000.01m), dueDate: start.AddDays(20)) }),
                new EntryLine(sales.Id, Money.FromRupees(50_000.01m), DrCr.Credit),
            }));
            svc.Post(new Voucher(Guid.NewGuid(), rev.Id, start.AddDays(12), new[]
            {
                new EntryLine(acme.Id, Money.FromRupees(20_000.03m), DrCr.Debit, billAllocations: new[]
                    { new BillAllocation(BillRefType.NewRef, "RJ-1", Money.FromRupees(20_000.03m), dueDate: start.AddDays(25)) }),
                new EntryLine(sales.Id, Money.FromRupees(20_000.03m), DrCr.Credit),
            }, applicableUpto: asOf.AddDays(60)));
            svc.Post(new Voucher(Guid.NewGuid(), memo.Id, start.AddDays(70), new[]
            {
                new EntryLine(cash.Id, Money.FromRupees(1_234.57m), DrCr.Debit),
                new EntryLine(acme.Id, Money.FromRupees(1_234.57m), DrCr.Credit, billAllocations: new[]
                    { new BillAllocation(BillRefType.AgstRef, "INV-3", Money.FromRupees(1_234.57m)) }),
            }));
            svc.Post(new Voucher(Guid.NewGuid(), rev.Id, start.AddDays(15), new[]
            {
                new EntryLine(purchases.Id, Money.FromRupees(4_444.41m), DrCr.Debit),
                new EntryLine(zenith.Id, Money.FromRupees(4_444.41m), DrCr.Credit, billAllocations: new[]
                    { new BillAllocation(BillRefType.NewRef, "RJ-P", Money.FromRupees(4_444.41m), dueDate: start.AddDays(30)) }),
            }, applicableUpto: asOf.AddDays(60)));
        }

        _storage.Save(c);
        return new Fixture(vm, c, asOf, inv1.Id, inv2.Id, inv3.Id, pb9.Id, acme.Id, zenith.Id);
    }

    /// <summary>The same book read back through a FRESH store handle over the same directory.</summary>
    private Company Reload(Company c)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var fresh = new CompanyStorage(_tempDir);
        return fresh.Load(fresh.ListCompanies().Single(e => e.Name == c.Name));
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    private static IEnumerable<Avalonia.Visual> Descendants(Avalonia.Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static PageConfig ShippedDefault() => new()
    {
        Size = PageSize.A4,
        Orientation = PageOrientation.Portrait,
        FooterText = "Apex Solutions  -  Page {page} of {pages}",
    };

    // ================================================================ reach

    /// <summary>
    /// 🔴 The vendor's own route: <b>Alt+G (Go To) &gt; Overdue Receivables / Overdue Payables</b>, pressed on the
    /// real window, typed into the realised search box, opened with the real Enter.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("Overdue Receivables", ReportKind.OverdueReceivables)]
    [InlineData("Overdue Payables", ReportKind.OverduePayables)]
    public void Alt_G_Go_To_opens_the_overdue_view_by_name(string label, ReportKind expected)
    {
        var f = Build("Overdue GoTo");
        var window = new MainWindow { DataContext = f.Vm, Width = 1366, Height = 768 };
        window.Show();
        Pump(window);

        window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Alt);
        Pump(window);
        var box = Descendants(window).OfType<TextBox>().First(t => t.Name == "GoToSearchBox");
        box.Text = label;
        Pump(window);
        Assert.Equal(label, f.Vm.GoTo!.Selected!.Label);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump(window);

        Assert.Equal(Screen.Report, f.Vm.CurrentScreen);
        Assert.Equal(expected, f.Vm.Reports!.Kind);
        Assert.Equal(label, f.Vm.Reports!.Title);
        window.Close();
    }

    /// <summary>
    /// The menu route: Gateway → Exception Reports → the overdue row, walked with the REAL arrow and Enter keys.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("Overdue Receivables", ReportKind.OverdueReceivables)]
    [InlineData("Overdue Payables", ReportKind.OverduePayables)]
    public void The_exception_reports_menu_reaches_the_overdue_view_by_keyboard(string label, ReportKind expected)
    {
        var f = Build("Overdue Menu");
        var window = new MainWindow { DataContext = f.Vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        ArrowToAndEnter(window, f.Vm, "Exception Reports");
        Assert.Equal("Exception Reports", f.Vm.Columns[^1].Title);
        ArrowToAndEnter(window, f.Vm, label);

        Assert.Equal(Screen.Report, f.Vm.CurrentScreen);
        Assert.Equal(expected, f.Vm.Reports!.Kind);
        window.Close();
    }

    // ================================================================ figures off the RELOADED book

    /// <summary>
    /// 🔴 THE FIGURES AND THE BOUNDARY, off the reloaded book. As at the as-of date: Acme's party row foots
    /// 7,901.61; INV-1 shows 6,667.04 and 30 days overdue; INV-3 shows 1,234.57 and 1 day overdue; INV-2 —
    /// due ON the as-of date — is absent. One day later INV-2 appears, 1 day overdue, and the total moves to
    /// 10,401.62. Each bill row carries the id of the voucher that raised it, which survives the reload.
    /// </summary>
    [Fact]
    public void Overdue_receivables_figures_and_due_date_boundary_off_the_reloaded_book()
    {
        var f = Build("Overdue Figures");
        var reloaded = Reload(f.Company);

        var reports = new ReportsViewModel(reloaded, ReportKind.OverdueReceivables);
        reports.SetAsOf(f.AsOf);

        var party = Assert.Single(reports.Rows, r => r.Particulars == "Acme Traders");
        Assert.Equal("7,901.61", party.Amount);
        Assert.Equal(f.AcmeId, party.DrillLedgerId);
        Assert.Equal("2 overdue bills", party.Secondary);

        var inv1 = Assert.Single(reports.Rows, r => r.Particulars.StartsWith("INV-1", StringComparison.Ordinal));
        Assert.Equal("INV-1  ·  30 days overdue", inv1.Particulars);
        Assert.Equal("6,667.04", inv1.Amount);
        Assert.Equal(f.Inv1, inv1.DrillVoucherId);
        Assert.Contains("Due " + ApexDate.Format(f.Company.BooksBeginFrom.AddDays(60)), inv1.Secondary);

        var inv3 = Assert.Single(reports.Rows, r => r.Particulars.StartsWith("INV-3", StringComparison.Ordinal));
        Assert.Equal("INV-3  ·  1 day overdue", inv3.Particulars);
        Assert.Equal("1,234.57", inv3.Amount);
        Assert.Equal(f.Inv3, inv3.DrillVoucherId);

        Assert.DoesNotContain(reports.Rows, r => r.Particulars.Contains("INV-2", StringComparison.Ordinal));
        var total = Assert.Single(reports.Rows, r => r.IsTotal);
        Assert.Equal("Total Overdue Receivables", total.Particulars);
        Assert.Equal("7,901.61", total.Amount);

        // The next day the bill that was due ON the as-of date becomes 1 day overdue.
        reports.SetAsOf(f.AsOf.AddDays(1));
        var inv2 = Assert.Single(reports.Rows, r => r.Particulars.StartsWith("INV-2", StringComparison.Ordinal));
        Assert.Equal("INV-2  ·  1 day overdue", inv2.Particulars);
        Assert.Equal("2,500.01", inv2.Amount);
        Assert.Equal(f.Inv2, inv2.DrillVoucherId);
        Assert.Equal("10,401.62", Assert.Single(reports.Rows, r => r.IsTotal).Amount);
    }

    [Fact]
    public void Overdue_payables_figures_off_the_reloaded_book_and_no_receivable_leaks_in()
    {
        var f = Build("Overdue Payables");
        var reloaded = Reload(f.Company);

        var reports = new ReportsViewModel(reloaded, ReportKind.OverduePayables);
        reports.SetAsOf(f.AsOf);

        var party = Assert.Single(reports.Rows, r => r.Particulars == "Zenith Supplies");
        Assert.Equal("7,777.77", party.Amount);
        Assert.Equal(f.ZenithId, party.DrillLedgerId);
        var bill = Assert.Single(reports.Rows, r => r.Particulars.StartsWith("PB-9", StringComparison.Ordinal));
        Assert.Equal("PB-9  ·  15 days overdue", bill.Particulars);
        Assert.Equal("7,777.77", bill.Amount);
        Assert.Equal(f.Pb9, bill.DrillVoucherId);
        Assert.Equal("7,777.77", Assert.Single(reports.Rows, r => r.IsTotal).Amount);
        Assert.DoesNotContain(reports.Rows, r => r.Particulars.Contains("INV-", StringComparison.Ordinal)
                                              || r.Particulars == "Acme Traders");
    }

    /// <summary>The as-of date scopes the report through the period too: with an Alt+F2 window ending the day
    /// INV-1 fell due, nothing is overdue and the report says so on its own row.</summary>
    [Fact]
    public void A_period_ending_on_the_first_due_date_lists_nothing_and_says_so()
    {
        var f = Build("Overdue Period");
        var reloaded = Reload(f.Company);
        var reports = new ReportsViewModel(reloaded, ReportKind.OverdueReceivables);

        reports.SetPeriod(reloaded.BooksBeginFrom, reloaded.BooksBeginFrom.AddDays(60));

        var only = Assert.Single(reports.Rows);
        Assert.Equal("No overdue receivable bills as at this date.", only.Particulars);
        Assert.False(only.IsDrillable);
    }

    /// <summary>
    /// LOW-2. The report is AS-AT: an Alt+F2 period whose START is after INV-1's bill date (and after its due
    /// date) still lists INV-1 at 6,667.04, 30 days overdue, because the period END is the as-of date and a bill
    /// raised before the window is still owed. A builder that filtered bills by the period start would drop it.
    /// </summary>
    [Fact]
    public void A_period_starting_after_the_bill_date_still_lists_the_older_overdue_bill()
    {
        var f = Build("Overdue Old Bill");
        var reloaded = Reload(f.Company);
        var reports = new ReportsViewModel(reloaded, ReportKind.OverdueReceivables);

        reports.SetPeriod(reloaded.BooksBeginFrom.AddDays(80), f.AsOf);

        var inv1 = Assert.Single(reports.Rows, r => r.Particulars.StartsWith("INV-1", StringComparison.Ordinal));
        Assert.Equal("INV-1  ·  30 days overdue", inv1.Particulars);
        Assert.Equal("6,667.04", inv1.Amount);
        Assert.Equal("7,901.61", Assert.Single(reports.Rows, r => r.IsTotal).Amount);
    }

    /// <summary>
    /// 🔴 HIGH-1, off the RELOADED book. Memorandum and Reversing Journal vouchers do not impact the books
    /// (vendor, help.tallysolutions.com/accounting-entry-tally/: <i>"Memorandum Vouchers are those vouchers that do
    /// not impact your books of accounts."</i>; Reversing Journals <i>"are not posted to any books of accounts and
    /// cannot be included in regular reports."</i>). A memo bill of 50,000.01, a reversing-journal bill of
    /// 20,000.03 and a memo "settlement" of INV-3 were saved through the real store and read back: Overdue
    /// Receivables still foots 7,901.61 with INV-3 listed at 1,234.57, Overdue Payables still 7,777.77, and Bills
    /// Receivable / Payable — the same projection — carry none of the non-posting bills.
    /// </summary>
    [Fact]
    public void Memorandum_and_reversing_journal_bills_never_reach_overdue_or_bills_outstanding_off_the_reloaded_book()
    {
        var f = Build("Overdue Provisional", provisional: true);
        var reloaded = Reload(f.Company);
        // The non-posting vouchers DID survive the reload — the test is not vacuous.
        Assert.Equal(4, reloaded.Vouchers.Count(v =>
            reloaded.FindVoucherType(v.TypeId)!.Name is "Memorandum" or "Reversing Journal"));

        var receivables = new ReportsViewModel(reloaded, ReportKind.OverdueReceivables);
        receivables.SetAsOf(f.AsOf);
        Assert.Equal("7,901.61", Assert.Single(receivables.Rows, r => r.Particulars == "Acme Traders").Amount);
        Assert.Equal("1,234.57",
            Assert.Single(receivables.Rows, r => r.Particulars.StartsWith("INV-3", StringComparison.Ordinal)).Amount);
        Assert.DoesNotContain(receivables.Rows, r => r.Particulars.Contains("MEMO-", StringComparison.Ordinal)
                                                  || r.Particulars.Contains("RJ-", StringComparison.Ordinal));
        Assert.Equal("7,901.61", Assert.Single(receivables.Rows, r => r.IsTotal).Amount);

        var payables = new ReportsViewModel(reloaded, ReportKind.OverduePayables);
        payables.SetAsOf(f.AsOf);
        Assert.DoesNotContain(payables.Rows, r => r.Particulars.Contains("RJ-P", StringComparison.Ordinal));
        Assert.Equal("7,777.77", Assert.Single(payables.Rows, r => r.IsTotal).Amount);

        var billsReceivable = new ReportsViewModel(reloaded, ReportKind.ReceivablesOutstanding);
        billsReceivable.SetAsOf(f.AsOf);
        Assert.Equal(new[] { "INV-1", "INV-2", "INV-3" },
            billsReceivable.PayrollRows.Where(r => r.Cells.Count > 1 && r.Cells[0].Text == "Acme Traders")
                .Select(r => r.Cells[1].Text).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.Equal("1,234.57",
            Assert.Single(billsReceivable.PayrollRows, r => r.Cells.Count > 1 && r.Cells[1].Text == "INV-3").Cells[5].Text);

        var billsPayable = new ReportsViewModel(reloaded, ReportKind.PayablesOutstanding);
        billsPayable.SetAsOf(f.AsOf);
        Assert.DoesNotContain(billsPayable.PayrollRows, r => r.Cells.Count > 1 && r.Cells[1].Text == "RJ-P");
        Assert.Contains(billsPayable.PayrollRows, r => r.Cells.Count > 1 && r.Cells[1].Text == "PB-9");
    }

    // ================================================================ egress: CSV and PDF bytes

    /// <summary>
    /// 🔴 THE EMITTED ARTEFACTS. The exported CSV bytes and the rendered PDF's drawn strings both carry the
    /// odd-paisa figures, the overdue days and the bill references — and neither carries INV-2.
    /// </summary>
    [Fact]
    public void Overdue_receivables_export_csv_and_print_pdf_carry_the_figures_and_omit_the_boundary_bill()
    {
        var f = Build("Overdue Egress");
        var reloaded = Reload(f.Company);
        var reports = new ReportsViewModel(reloaded, ReportKind.OverdueReceivables);
        reports.SetAsOf(f.AsOf);

        var csv = Encoding.UTF8.GetString(CsvWriter.Write(ReportTabularProjector.Project(reports)));
        Assert.Contains("Overdue Receivables", csv);
        Assert.Contains("INV-1  ·  30 days overdue", csv);
        Assert.Contains("INV-3  ·  1 day overdue", csv);
        Assert.True(csv.Contains("6667.04", StringComparison.Ordinal) || csv.Contains("6,667.04", StringComparison.Ordinal),
            "the CSV must carry INV-1's pending 6,667.04 to the paisa.");
        Assert.True(csv.Contains("7901.61", StringComparison.Ordinal) || csv.Contains("7,901.61", StringComparison.Ordinal),
            "the CSV must carry the overdue total 7,901.61 to the paisa.");
        Assert.DoesNotContain("INV-2", csv);

        var drawn = DrawnStrings(ReportPdf.Render(ReportPrintProjector.Project(reports), ShippedDefault()));
        var page = string.Join("\n", drawn);
        Assert.Contains("Overdue Receivables", page);
        Assert.Contains("6,667.04", drawn);
        Assert.Contains("1,234.57", drawn);
        Assert.Contains(drawn, d => d.Contains("7,901.61", StringComparison.Ordinal));
        Assert.Contains("30 days overdue", page);
        Assert.DoesNotContain("INV-2", page);
    }

    // ================================================================ drill (real Enter)

    /// <summary>
    /// 🔴 Enter on a BILL row opens the voucher that raised that bill; Enter on a PARTY row opens that party's
    /// ledger vouchers. Both through the real key on a shown window.
    /// </summary>
    [AvaloniaFact]
    public void Enter_on_a_bill_row_opens_its_voucher_and_on_a_party_row_opens_the_ledger()
    {
        var f = Build("Overdue Drill");
        var window = new MainWindow { DataContext = f.Vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        f.Vm.OpenReport(ReportKind.OverdueReceivables);
        Pump(window);
        f.Vm.Reports!.SetAsOf(f.AsOf);
        Pump(window);

        var billRow = f.Vm.Reports!.Rows.Single(r => r.Particulars.StartsWith("INV-3", StringComparison.Ordinal));
        f.Vm.Reports!.SelectedRow = billRow;
        Pump(window);
        var before = f.Vm.Columns.Count;
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump(window);

        Assert.Equal(Screen.VoucherDetail, f.Vm.CurrentScreen);
        Assert.Equal(f.Inv3, f.Vm.VoucherDetail!.VoucherId);
        Assert.Equal(before + 1, f.Vm.Columns.Count);

        f.Vm.Back();
        Pump(window);
        Assert.Equal(Screen.Report, f.Vm.CurrentScreen);

        var partyRow = f.Vm.Reports!.Rows.Single(r => r.Particulars == "Acme Traders");
        f.Vm.Reports!.SelectedRow = partyRow;
        Pump(window);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump(window);

        Assert.Equal(Screen.LedgerVouchers, f.Vm.CurrentScreen);
        Assert.Equal(f.AcmeId, f.Vm.LedgerVouchers!.LedgerId);
        window.Close();
    }

    /// <summary>
    /// LOW-1. Under an Alt+F2 period that starts AFTER INV-1's bill date, Enter on the party row must open the
    /// ledger from books-begin to the as-of date, so the invoice that makes the party overdue is on the drilled
    /// screen. Real Enter on a shown window; the realised LedgerVouchers window is asserted.
    /// </summary>
    [AvaloniaFact]
    public void Enter_on_a_party_row_under_a_later_period_opens_the_ledger_from_books_begin()
    {
        var f = Build("Overdue Drill Window");
        var window = new MainWindow { DataContext = f.Vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        f.Vm.OpenReport(ReportKind.OverdueReceivables);
        Pump(window);
        f.Vm.Reports!.SetPeriod(f.Company.BooksBeginFrom.AddDays(80), f.AsOf);
        Pump(window);
        Assert.Contains(f.Vm.Reports!.Rows, r => r.Particulars.StartsWith("INV-1", StringComparison.Ordinal));

        f.Vm.Reports!.SelectedRow = f.Vm.Reports!.Rows.Single(r => r.Particulars == "Acme Traders");
        Pump(window);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump(window);

        Assert.Equal(Screen.LedgerVouchers, f.Vm.CurrentScreen);
        var lv = f.Vm.LedgerVouchers!;
        Assert.Equal(f.AcmeId, lv.LedgerId);
        Assert.Equal(f.Company.BooksBeginFrom, lv.PeriodFrom);
        Assert.Equal(f.AsOf, lv.PeriodTo);
        window.Close();
    }

    /// <summary>Enter on the Total row is a safe no-op — it carries no drill key.</summary>
    [AvaloniaFact]
    public void Enter_on_the_overdue_total_row_is_a_safe_no_op()
    {
        var f = Build("Overdue NoOp");
        var window = new MainWindow { DataContext = f.Vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        f.Vm.OpenReport(ReportKind.OverduePayables);
        Pump(window);
        f.Vm.Reports!.SetAsOf(f.AsOf);
        Pump(window);
        var total = f.Vm.Reports!.Rows.Single(r => r.IsTotal);
        Assert.False(total.IsDrillable);
        f.Vm.Reports!.SelectedRow = total;
        Pump(window);
        var before = f.Vm.Columns.Count;

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump(window);

        Assert.Equal(Screen.Report, f.Vm.CurrentScreen);
        Assert.Equal(before, f.Vm.Columns.Count);
        window.Close();
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Presses the REAL Down arrow until the active column highlights <paramref name="label"/>, then
    /// the REAL Enter.</summary>
    private static void ArrowToAndEnter(MainWindow window, MainWindowViewModel vm, string label)
    {
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            if (vm.Menu[vm.SelectedIndex].Label == label)
            {
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Pump(window);
                return;
            }
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(window);
        }
        Assert.Fail($"'{label}' was not reachable by arrow keys in the active column.");
    }

    /// <summary>The strings a PDF actually DREW (same parse as <c>ComparativePrintedPageTests.DrawnStrings</c>).</summary>
    private static List<string> DrawnStrings(byte[] pdf)
    {
        string text = Encoding.Latin1.GetString(pdf);
        var drawn = new List<string>();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '(') continue;
            if (i > 0 && text[i - 1] == '\\') continue;

            var sb = new StringBuilder();
            int j = i + 1;
            bool closed = false;
            for (; j < text.Length; j++)
            {
                char ch = text[j];
                if (ch == '\\')
                {
                    if (j + 1 < text.Length) sb.Append(text[++j]);
                    continue;
                }
                if (ch == ')') { closed = true; break; }
                sb.Append(ch);
            }

            if (!closed || j + 3 >= text.Length || text.Substring(j + 1, 3) != " Tj") continue;
            drawn.Add(sb.ToString());
            i = j + 3;
        }
        return drawn;
    }
}
