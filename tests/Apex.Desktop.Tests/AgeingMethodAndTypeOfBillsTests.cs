using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
/// Census <b>11.13</b> (advancing 11.9's ageing clause, T2-24) — <b>F6 Ageing Method</b> on Bills Receivable /
/// Payable and Overdue Receivables / Payables, and <b>Ctrl+B Basis of Values &gt; Type of Bills &gt; Overdue
/// Bills</b> on Bills Receivable / Payable.
///
/// <para>Vendor (TallyPrime), opened by content 2026-10-29:
/// help.tallysolutions.com/manage-receivables-outstanding-tally/ — <i>"To view only overdue bills, use Basis of
/// Values to select Overdue Bills in Type of Bills."</i> and <i>"Press F6 (Ageing Method) to configure the report
/// view as per ageing by bill dates or by due dates and set the ageing windows."</i>;
/// help.tallysolutions.com/tally-prime/analysis-verification/outstandings-tally/ — <i>"Select the appropriate
/// Ageing Methods from Ageing by Bill Date or Ageing by Due Date and press Enter."</i></para>
///
/// <para>🔴 Figures are read off a book RELOADED through a fresh <see cref="CompanyStorage"/>; every amount ends in
/// an odd paisa; one bill has NO due date and one falls due EXACTLY on the as-of date (IV-245). The keys are the
/// real F6 / arrows / Enter / Ctrl+B / Ctrl+A through <see cref="MainWindow"/>'s handler.</para>
///
/// <para>Fixture (as-of = books-begin + 120). Acme Traders (debtor): INV-A 10,000.37 dated s+10 due s+40,
/// part-settled 3,333.33 → 6,667.04 pending (80 days overdue, 110 days old); INV-B 2,500.01 dated s+110 due ON the
/// as-of date; INV-C 1,234.57 dated s+60, no due date (60 days either way); INV-D 999.99 dated s+20 due s+105 (15
/// overdue, 100 old). Zenith Supplies (creditor): PB-9 7,777.77 dated s+50 due s+100 (20 overdue, 70 old); PB-10
/// 3,000.03 dated s+115 due s+130 (not due, 5 old).</para>
/// </summary>
public sealed class AgeingMethodAndTypeOfBillsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public AgeingMethodAndTypeOfBillsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexAgeing_" + Guid.NewGuid().ToString("N"));
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

    private sealed record Fixture(MainWindowViewModel Vm, Company Company, DateOnly AsOf);

    private Fixture Build(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name + " " + Guid.NewGuid().ToString("N")[..6];
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        var c = vm.Company!;
        var s = c.BooksBeginFrom;
        var asOf = s.AddDays(120);

        var svc = new LedgerService(c);
        var journal = c.FindVoucherTypeByName("Journal")!;
        var acme = new DomainLedger(Guid.NewGuid(), "Acme Traders", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, true, maintainBillByBill: true);
        var zenith = new DomainLedger(Guid.NewGuid(), "Zenith Supplies", c.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, false, maintainBillByBill: true);
        var sales = new DomainLedger(Guid.NewGuid(), "Sales Ageing", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, false);
        var purchases = new DomainLedger(Guid.NewGuid(), "Purchases Ageing", c.FindGroupByName("Purchase Accounts")!.Id,
            Money.Zero, true);
        var cash = new DomainLedger(Guid.NewGuid(), "Cash Ageing", c.FindGroupByName("Cash-in-Hand")!.Id,
            Money.Zero, true);
        foreach (var l in new[] { acme, zenith, sales, purchases, cash }) c.AddLedger(l);

        BillAllocation Alloc(string reference, decimal amount, int? dueDay) => dueDay is { } d
            ? new BillAllocation(BillRefType.NewRef, reference, Money.FromRupees(amount), dueDate: s.AddDays(d))
            : new BillAllocation(BillRefType.NewRef, reference, Money.FromRupees(amount));

        void Sale(int day, string reference, decimal amount, int? dueDay) =>
            svc.Post(new Voucher(Guid.NewGuid(), journal.Id, s.AddDays(day), new[]
            {
                new EntryLine(acme.Id, Money.FromRupees(amount), DrCr.Debit,
                    billAllocations: new[] { Alloc(reference, amount, dueDay) }),
                new EntryLine(sales.Id, Money.FromRupees(amount), DrCr.Credit),
            }));

        void Purchase(int day, string reference, decimal amount, int dueDay) =>
            svc.Post(new Voucher(Guid.NewGuid(), journal.Id, s.AddDays(day), new[]
            {
                new EntryLine(purchases.Id, Money.FromRupees(amount), DrCr.Debit),
                new EntryLine(zenith.Id, Money.FromRupees(amount), DrCr.Credit,
                    billAllocations: new[] { Alloc(reference, amount, dueDay) }),
            }));

        Sale(10, "INV-A", 10_000.37m, 40);
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, s.AddDays(45), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(3_333.33m), DrCr.Debit),
            new EntryLine(acme.Id, Money.FromRupees(3_333.33m), DrCr.Credit, billAllocations: new[]
                { new BillAllocation(BillRefType.AgstRef, "INV-A", Money.FromRupees(3_333.33m)) }),
        }));
        Sale(110, "INV-B", 2_500.01m, 120);
        Sale(60, "INV-C", 1_234.57m, null);
        Sale(20, "INV-D", 999.99m, 105);
        Purchase(50, "PB-9", 7_777.77m, 100);
        Purchase(115, "PB-10", 3_000.03m, 130);

        _storage.Save(c);
        return new Fixture(vm, c, asOf);
    }

    private Company Reload(Company c)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var fresh = new CompanyStorage(_tempDir);
        return fresh.Load(fresh.ListCompanies().Single(e => e.Name == c.Name));
    }

    private static readonly string[] DueBucketsAll =
        { "Not due=2,500.01", "0-30 days=999.99", "31-60 days=1,234.57", "61-90 days=6,667.04", "90+ days=0.00" };

    private static readonly string[] BillBucketsAll =
        { "0-30 days=2,500.01", "31-60 days=1,234.57", "61-90 days=0.00", "90+ days=7,667.03" };

    /// <summary>The second band (the ageing summary) of a Bills Receivable / Payable matrix, as rendered text.</summary>
    private static string[] SecondBand(ReportsViewModel r) =>
        r.PayrollRows2.Select(row => $"{row.Cells[0].Text}={row.Cells[1].Text}").ToArray();

    /// <summary>Per-bill (reference → Ageing cell) on a Bills Receivable / Payable matrix.</summary>
    private static Dictionary<string, string> AgeingCells(ReportsViewModel r) =>
        r.PayrollRows.Where(row => !row.IsTotal && row.Cells.Count == 8)
            .ToDictionary(row => row.Cells[1].Text, row => row.Cells[7].Text);

    /// <summary>The ageing band appended under an overdue view's total, as rendered text.</summary>
    private static string[] OverdueBand(ReportsViewModel r)
    {
        var rows = r.Rows.ToList();
        var total = rows.FindIndex(x => x.IsTotal);
        return rows.Skip(total + 2).Select(x => $"{x.Particulars}={x.Amount}").ToArray();
    }

    // ================================================================ figures off the RELOADED book

    [Fact]
    public void Bills_receivable_buckets_under_both_bases_off_the_reloaded_book()
    {
        var f = Build("Ageing Figures");
        var r = new ReportsViewModel(Reload(f.Company), ReportKind.ReceivablesOutstanding);
        r.SetAsOf(f.AsOf);

        Assert.Equal(AgeingMethod.ByDueDate, r.AgeingMethod);
        Assert.Equal("Ageing Analysis (by due date)", r.PayrollSection2Title);
        Assert.Equal(DueBucketsAll, SecondBand(r));
        var due = AgeingCells(r);
        Assert.Equal("61-90 days", due["INV-A"]);
        Assert.Equal("Not due", due["INV-B"]);      // due ON the as-of date
        Assert.Equal("31-60 days", due["INV-C"]);   // no due date: due = bill date
        Assert.Equal("0-30 days", due["INV-D"]);
        Assert.EndsWith("Ageing by Due Date", r.Subtitle);

        r.SetAgeingMethod(AgeingMethod.ByBillDate);

        Assert.Equal("Ageing Analysis (by bill date)", r.PayrollSection2Title);
        Assert.Equal(BillBucketsAll, SecondBand(r));
        var bill = AgeingCells(r);
        Assert.Equal("90+ days", bill["INV-A"]);
        Assert.Equal("0-30 days", bill["INV-B"]);
        Assert.Equal("31-60 days", bill["INV-C"]);
        Assert.Equal("90+ days", bill["INV-D"]);
        Assert.EndsWith("Ageing by Bill Date", r.Subtitle);
        // Pending and the total are untouched by the basis; the Overdue Days column stays due-date based.
        var inv = r.PayrollRows.Single(x => x.Cells.Count == 8 && x.Cells[1].Text == "INV-A");
        Assert.Equal("6,667.04", inv.Cells[5].Text);
        Assert.Equal("80", inv.Cells[6].Text);
        Assert.Equal("11,401.61", r.PayrollRows.Single(x => x.IsTotal).Cells[5].Text);
    }

    [Fact]
    public void Bills_payable_buckets_under_both_bases_off_the_reloaded_book()
    {
        var f = Build("Ageing Payables");
        var r = new ReportsViewModel(Reload(f.Company), ReportKind.PayablesOutstanding);
        r.SetAsOf(f.AsOf);

        Assert.Equal(new[] { "Not due=3,000.03", "0-30 days=7,777.77", "31-60 days=0.00", "61-90 days=0.00", "90+ days=0.00" },
            SecondBand(r));
        r.SetAgeingMethod(AgeingMethod.ByBillDate);
        Assert.Equal(new[] { "0-30 days=3,000.03", "31-60 days=0.00", "61-90 days=7,777.77", "90+ days=0.00" },
            SecondBand(r));
    }

    [Fact]
    public void Type_of_bills_overdue_filters_bills_receivable_through_the_overdue_engine_off_the_reloaded_book()
    {
        var f = Build("Ageing TypeOfBills");
        var reloaded = Reload(f.Company);
        var r = new ReportsViewModel(reloaded, ReportKind.ReceivablesOutstanding);
        r.SetAsOf(f.AsOf);
        Assert.Equal(4, AgeingCells(r).Count);

        r.SetTypeOfBills(TypeOfBills.OverdueBills);

        Assert.Equal(new[] { "INV-A", "INV-C", "INV-D" }, AgeingCells(r).Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal("8,901.60", r.PayrollRows.Single(x => x.IsTotal).Cells[5].Text);
        Assert.Contains("Overdue Bills", r.Subtitle);
        Assert.Equal(new[] { "Not due=0.00", "0-30 days=999.99", "31-60 days=1,234.57", "61-90 days=6,667.04", "90+ days=0.00" },
            SecondBand(r));

        // One engine: the Overdue Receivables view of the same reloaded book lists the same bills at the same pending.
        var overdue = new ReportsViewModel(reloaded, ReportKind.OverdueReceivables);
        overdue.SetAsOf(f.AsOf);
        var fromOverdue = overdue.Rows.Where(x => x.Indent == 18 && x.DrillVoucherId != Guid.Empty)
            .Select(x => x.Particulars.Split(' ')[0] + "=" + x.Amount).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var fromFilter = r.PayrollRows.Where(x => !x.IsTotal && x.Cells.Count == 8)
            .Select(x => x.Cells[1].Text + "=" + x.Cells[5].Text).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(fromFilter, fromOverdue);
        Assert.Equal("8,901.60", overdue.Rows.Single(x => x.IsTotal).Amount);

        // Under bill-date ageing the filtered buckets move and the bills do not.
        r.SetAgeingMethod(AgeingMethod.ByBillDate);
        Assert.Equal(new[] { "0-30 days=0.00", "31-60 days=1,234.57", "61-90 days=0.00", "90+ days=7,667.03" }, SecondBand(r));
        Assert.Equal("8,901.60", r.PayrollRows.Single(x => x.IsTotal).Cells[5].Text);
    }

    [Fact]
    public void Overdue_receivables_ageing_band_and_row_windows_under_both_bases_off_the_reloaded_book()
    {
        var f = Build("Ageing Overdue View");
        var r = new ReportsViewModel(Reload(f.Company), ReportKind.OverdueReceivables);
        r.SetAsOf(f.AsOf);

        Assert.DoesNotContain(r.Rows, x => x.Particulars.StartsWith("INV-B", StringComparison.Ordinal));
        Assert.Equal("Ageing by Due Date", r.Rows[r.Rows.ToList().FindIndex(x => x.IsTotal) + 1].Particulars);
        Assert.Equal(new[] { "Not due=0.00", "0-30 days=999.99", "31-60 days=1,234.57", "61-90 days=6,667.04", "90+ days=0.00" },
            OverdueBand(r));
        var invA = r.Rows.Single(x => x.Particulars.StartsWith("INV-A", StringComparison.Ordinal));
        Assert.Equal("INV-A  ·  80 days overdue", invA.Particulars);
        Assert.EndsWith("61-90 days", invA.Secondary);

        r.SetAgeingMethod(AgeingMethod.ByBillDate);

        Assert.Equal("Ageing by Bill Date", r.Rows[r.Rows.ToList().FindIndex(x => x.IsTotal) + 1].Particulars);
        Assert.Equal(new[] { "0-30 days=0.00", "31-60 days=1,234.57", "61-90 days=0.00", "90+ days=7,667.03" },
            OverdueBand(r));
        invA = r.Rows.Single(x => x.Particulars.StartsWith("INV-A", StringComparison.Ordinal));
        Assert.Equal("INV-A  ·  80 days overdue", invA.Particulars);   // membership and overdue days: due date
        Assert.EndsWith("90+ days", invA.Secondary);
        Assert.Equal("8,901.60", r.Rows.Single(x => x.IsTotal).Amount);
    }

    // ================================================================ egress: CSV and PDF bytes

    private static PageConfig ShippedDefault() => new()
    {
        Size = PageSize.A4,
        Orientation = PageOrientation.Portrait,
        FooterText = "Apex Solutions  -  Page {page} of {pages}",
    };

    [Fact]
    public void Bills_receivable_csv_and_pdf_carry_the_basis_and_its_buckets_under_both_bases()
    {
        var f = Build("Ageing Egress");
        var r = new ReportsViewModel(Reload(f.Company), ReportKind.ReceivablesOutstanding);
        r.SetAsOf(f.AsOf);
        r.SetTypeOfBills(TypeOfBills.OverdueBills);

        var csvDue = Encoding.UTF8.GetString(CsvWriter.Write(ReportTabularProjector.Project(r)));
        var pdfDue = string.Join("\n", DrawnStrings(ReportPdf.Render(ReportPrintProjector.Project(r), ShippedDefault())));
        Assert.Contains("Ageing Analysis (by due date)", csvDue);
        Assert.Contains("Ageing Analysis (by due date)", pdfDue);
        Assert.DoesNotContain("INV-B", csvDue);
        Assert.DoesNotContain("INV-B", pdfDue);
        Assert.True(csvDue.Contains("8901.60", StringComparison.Ordinal) || csvDue.Contains("8,901.60", StringComparison.Ordinal),
            "the CSV must carry the overdue-bills total 8,901.60 to the paisa");
        Assert.Contains("6,667.04", pdfDue);

        r.SetAgeingMethod(AgeingMethod.ByBillDate);
        var csvBill = Encoding.UTF8.GetString(CsvWriter.Write(ReportTabularProjector.Project(r)));
        var pdfBill = string.Join("\n", DrawnStrings(ReportPdf.Render(ReportPrintProjector.Project(r), ShippedDefault())));
        Assert.Contains("Ageing Analysis (by bill date)", csvBill);
        Assert.Contains("Ageing Analysis (by bill date)", pdfBill);
        Assert.True(csvBill.Contains("7667.03", StringComparison.Ordinal) || csvBill.Contains("7,667.03", StringComparison.Ordinal),
            "the CSV must carry the bill-date 90+ bucket 7,667.03 to the paisa");
        Assert.Contains("7,667.03", pdfBill);
        Assert.DoesNotContain("7,667.03", pdfDue);
    }

    [Fact]
    public void Overdue_receivables_csv_and_pdf_carry_the_basis_and_its_buckets_under_both_bases()
    {
        var f = Build("Ageing Overdue Egress");
        var r = new ReportsViewModel(Reload(f.Company), ReportKind.OverdueReceivables);
        r.SetAsOf(f.AsOf);

        var csvDue = Encoding.UTF8.GetString(CsvWriter.Write(ReportTabularProjector.Project(r)));
        var pdfDue = DrawnStrings(ReportPdf.Render(ReportPrintProjector.Project(r), ShippedDefault()));
        Assert.Contains("Ageing by Due Date", csvDue);
        Assert.Contains(pdfDue, d => d.Contains("Ageing by Due Date", StringComparison.Ordinal));
        Assert.DoesNotContain("INV-B", csvDue);

        r.SetAgeingMethod(AgeingMethod.ByBillDate);
        var csvBill = Encoding.UTF8.GetString(CsvWriter.Write(ReportTabularProjector.Project(r)));
        var pdfBill = DrawnStrings(ReportPdf.Render(ReportPrintProjector.Project(r), ShippedDefault()));
        Assert.Contains("Ageing by Bill Date", csvBill);
        Assert.Contains(pdfBill, d => d.Contains("Ageing by Bill Date", StringComparison.Ordinal));
        Assert.True(csvBill.Contains("7667.03", StringComparison.Ordinal) || csvBill.Contains("7,667.03", StringComparison.Ordinal),
            "the CSV must carry the bill-date 90+ bucket 7,667.03");
        Assert.Contains(pdfBill, d => d.Contains("7,667.03", StringComparison.Ordinal));
        Assert.DoesNotContain(pdfDue, d => d.Contains("7,667.03", StringComparison.Ordinal));
    }

    // ================================================================ reach: the real keys

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    private static void Press(MainWindow w, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPressQwerty(key, mods);
        Pump(w);
    }

    private static IEnumerable<Avalonia.Visual> Descendants(Avalonia.Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static void ArrowToAndEnter(MainWindow w, MainWindowViewModel vm, string label)
    {
        for (var i = 0; i < 10; i++)
        {
            if (vm.Columns[vm.ActiveColumnIndex].Selected?.Label == label)
            {
                Press(w, PhysicalKey.Enter);
                return;
            }
            Press(w, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"'{label}' was not reachable by arrow keys on the F6 Ageing Method picker.");
    }

    private (MainWindow Window, Fixture F) Open(string name, ReportKind kind)
    {
        var f = Build(name);
        var window = new MainWindow { DataContext = f.Vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);
        f.Vm.OpenReport(kind);
        Pump(window);
        f.Vm.Reports!.SetAsOf(f.AsOf);
        Pump(window);
        Assert.Equal(Screen.Report, f.Vm.CurrentScreen);
        return (window, f);
    }

    [AvaloniaTheory]
    [InlineData(ReportKind.ReceivablesOutstanding)]
    [InlineData(ReportKind.PayablesOutstanding)]
    [InlineData(ReportKind.OverdueReceivables)]
    [InlineData(ReportKind.OverduePayables)]
    public void F6_opens_the_ageing_method_picker_and_Enter_on_bill_date_re_ages_the_report(ReportKind kind)
    {
        var (window, f) = Open("Ageing F6 " + kind, kind);
        Assert.Equal("Ageing Method", f.Vm.ButtonBar.First(b => b.Key == "F6").Caption);

        Press(window, PhysicalKey.F6);
        Assert.Equal(Screen.AgeingMethodPicker, f.Vm.CurrentScreen);
        // The highlight lands on the basis in force.
        Assert.Equal("Ageing by Due Date", f.Vm.Columns[f.Vm.ActiveColumnIndex].Selected?.Label);

        ArrowToAndEnter(window, f.Vm, "Ageing by Bill Date");

        Assert.Equal(Screen.Report, f.Vm.CurrentScreen);
        Assert.Equal(kind, f.Vm.Reports!.Kind);
        Assert.Equal(AgeingMethod.ByBillDate, f.Vm.Reports!.AgeingMethod);
        Assert.EndsWith("Ageing by Bill Date", f.Vm.Reports!.Subtitle);
        if (kind == ReportKind.ReceivablesOutstanding)
            Assert.Equal(BillBucketsAll, SecondBand(f.Vm.Reports!));
        if (kind == ReportKind.OverdueReceivables)
            Assert.Contains(f.Vm.Reports!.Rows, x => x.Particulars == "90+ days" && x.Amount == "7,667.03");

        // And back: F6, Enter on "Ageing by Due Date".
        Press(window, PhysicalKey.F6);
        ArrowToAndEnter(window, f.Vm, "Ageing by Due Date");
        Assert.Equal(AgeingMethod.ByDueDate, f.Vm.Reports!.AgeingMethod);
        if (kind == ReportKind.ReceivablesOutstanding)
            Assert.Equal(DueBucketsAll, SecondBand(f.Vm.Reports!));
        window.Close();
    }

    [AvaloniaFact]
    public void Escape_on_the_F6_picker_leaves_the_basis_unchanged()
    {
        var (window, f) = Open("Ageing F6 Esc", ReportKind.ReceivablesOutstanding);
        Press(window, PhysicalKey.F6);
        Press(window, PhysicalKey.ArrowDown);
        Press(window, PhysicalKey.Escape);
        Assert.Equal(Screen.Report, f.Vm.CurrentScreen);
        Assert.Equal(AgeingMethod.ByDueDate, f.Vm.Reports!.AgeingMethod);
        Assert.Equal(DueBucketsAll, SecondBand(f.Vm.Reports!));
        window.Close();
    }

    [AvaloniaFact]
    public void F6_stays_the_receipt_voucher_off_the_outstandings_views()
    {
        var (window, f) = Open("Ageing F6 TB", ReportKind.TrialBalance);
        Assert.Equal("Receipt", f.Vm.ButtonBar.First(b => b.Key == "F6").Caption);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(ReportKind.ReceivablesOutstanding, "8,901.60", "INV-B")]
    [InlineData(ReportKind.PayablesOutstanding, "7,777.77", "PB-10")]
    public void Ctrl_B_type_of_bills_overdue_bills_by_keyboard_filters_the_report(
        ReportKind kind, string overdueTotal, string notOverdueRef)
    {
        var (window, f) = Open("Ageing CtrlB " + kind, kind);
        Assert.True(f.Vm.ButtonBar.First(b => b.Key == "Ctrl+B").Enabled);
        Assert.Contains(f.Vm.Reports!.PayrollRows, x => x.Cells.Count == 8 && x.Cells[1].Text == notOverdueRef);

        Press(window, PhysicalKey.B, RawInputModifiers.Control);
        Assert.Equal(Screen.BasisOfValues, f.Vm.CurrentScreen);
        var panel = f.Vm.BasisOfValues!;
        Assert.True(panel.ShowTypeOfBills);
        Assert.False(panel.ShowScaleFactor);
        Assert.Equal(TypeOfBills.AllBills, panel.SelectedTypeOfBills!.Value);

        // The realised control, driven by the keyboard: Tab to the Type of Bills box, then the settled picker
        // gesture — Enter opens it, Down highlights, Enter commits and closes.
        var combo = Descendants(window).OfType<ComboBox>().Single(c => c.Name == "TypeOfBillsCombo");
        Assert.True(combo.IsEffectivelyVisible);
        Assert.Equal(new[] { "All Bills", "Overdue Bills" },
            combo.Items.Cast<TypeOfBillsOption>().Select(o => o.Display).ToArray());
        for (var i = 0; i < 12 && !combo.IsKeyboardFocusWithin; i++)
            Press(window, PhysicalKey.Tab);
        Assert.True(combo.IsKeyboardFocusWithin, "Tab never reached the Type of Bills box");
        Press(window, PhysicalKey.Enter);
        Assert.True(combo.IsDropDownOpen, "Enter did not open the focused Type of Bills box");
        Press(window, PhysicalKey.ArrowDown);
        Press(window, PhysicalKey.Enter);
        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(TypeOfBills.OverdueBills, panel.SelectedTypeOfBills!.Value);

        Press(window, PhysicalKey.A, RawInputModifiers.Control);

        Assert.Equal(Screen.Report, f.Vm.CurrentScreen);
        Assert.Null(f.Vm.BasisOfValues);
        Assert.Equal(TypeOfBills.OverdueBills, f.Vm.Reports!.TypeOfBills);
        Assert.DoesNotContain(f.Vm.Reports!.PayrollRows, x => x.Cells.Count == 8 && x.Cells[1].Text == notOverdueRef);
        Assert.Equal(overdueTotal, f.Vm.Reports!.PayrollRows.Single(x => x.IsTotal).Cells[5].Text);
        window.Close();
    }

    [AvaloniaFact]
    public void Ctrl_B_on_the_trial_balance_still_offers_only_the_scale_factor()
    {
        var (window, f) = Open("Ageing CtrlB TB", ReportKind.TrialBalance);
        Press(window, PhysicalKey.B, RawInputModifiers.Control);
        Assert.Equal(Screen.BasisOfValues, f.Vm.CurrentScreen);
        Assert.True(f.Vm.BasisOfValues!.ShowScaleFactor);
        Assert.False(f.Vm.BasisOfValues!.ShowTypeOfBills);
        Assert.Empty(f.Vm.BasisOfValues!.TypeOfBillsOptions);
        window.Close();
    }

    // ---------------------------------------------------------------- PDF parse

    /// <summary>The strings a PDF actually DREW (same parse as <c>OverdueExceptionReportsTests.DrawnStrings</c>).</summary>
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
