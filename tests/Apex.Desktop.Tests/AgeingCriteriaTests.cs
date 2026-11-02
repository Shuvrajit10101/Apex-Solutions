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
/// Census <b>11.13</b> (advancing 11.9 / T2-24) — the <b>F6 ageing criteria</b>: operator-set ageing windows on Bills
/// Receivable / Payable and Overdue Receivables / Payables (IV-305..IV-308).
///
/// <para>Vendor (TallyPrime), opened by content 2026-11-02:
/// help.tallysolutions.com/manage-receivables-outstanding-tally/ — <i>"Press F6 (Ageing Method) to configure the
/// report view as per ageing by bill dates or by due dates and set the ageing windows."</i>;
/// help.tallysolutions.com/tally-prime/analysis-verification/outstandings-tally/ — <i>"Specify the required ageing
/// criteria and press Enter."</i></para>
///
/// <para>Figures are read off a book RELOADED through a fresh <see cref="CompanyStorage"/>; every amount ends in an
/// odd paisa, and each custom window puts a bill EXACTLY on an edge. Fixture (as-of = books-begin + 120): INV-A
/// 6,667.04 pending (80 overdue, 110 old); INV-B 2,500.01 (due ON the as-of date, 10 old); INV-C 1,234.57 (no due
/// date, 60 either way); INV-D 999.99 (15 overdue, 100 old); PB-9 7,777.77 (20 overdue, 70 old); PB-10 3,000.03 (not
/// due, 5 old).</para>
/// </summary>
public sealed class AgeingCriteriaTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public AgeingCriteriaTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexAgeingCrit_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Due basis: 1-15 / 16-60 / 61+ (INV-D on 15, INV-C on 60).</summary>
    private static readonly AgeingPeriod[] DuePeriods = { new(1, 15), new(16, 60), new(61, null) };

    /// <summary>Bill basis: 0-10 / 11-59 / 60-100, closed, so "Over 100 days" is appended (INV-B on 10, INV-C on 60,
    /// INV-D on 100).</summary>
    private static readonly AgeingPeriod[] BillPeriods = { new(0, 10), new(11, 59), new(60, 100) };

    private static string[] SecondBand(ReportsViewModel r) =>
        r.PayrollRows2.Select(row => $"{row.Cells[0].Text}={row.Cells[1].Text}").ToArray();

    private static Dictionary<string, string> AgeingCells(ReportsViewModel r) =>
        r.PayrollRows.Where(row => !row.IsTotal && row.Cells.Count == 8)
            .ToDictionary(row => row.Cells[1].Text, row => row.Cells[7].Text);

    private static string[] OverdueBand(ReportsViewModel r)
    {
        var rows = r.Rows.ToList();
        var total = rows.FindIndex(x => x.IsTotal);
        return rows.Skip(total + 2).Select(x => $"{x.Particulars}={x.Amount}").ToArray();
    }

    private static string SecondaryOf(ReportsViewModel r, string reference) =>
        r.Rows.Single(x => x.Particulars.StartsWith(reference, StringComparison.Ordinal)).Secondary;

    // ================================================================ figures off the RELOADED book

    [Fact]
    public void Bills_receivable_custom_windows_bucket_and_label_every_bill_under_both_bases_off_the_reloaded_book()
    {
        var f = Build("Crit Receivable");
        var r = new ReportsViewModel(Reload(f.Company), ReportKind.ReceivablesOutstanding);
        r.SetAsOf(f.AsOf);

        Assert.Equal(string.Empty, r.SetAgeingCriteria(AgeingMethod.ByDueDate, DuePeriods));
        Assert.Equal(new[] { "Not due=2,500.01", "1-15 days=999.99", "16-60 days=1,234.57", "61+ days=6,667.04" },
            SecondBand(r));
        var cells = AgeingCells(r);
        Assert.Equal("61+ days", cells["INV-A"]);
        Assert.Equal("Not due", cells["INV-B"]);
        Assert.Equal("16-60 days", cells["INV-C"]);
        Assert.Equal("1-15 days", cells["INV-D"]);

        Assert.Equal(string.Empty, r.SetAgeingCriteria(AgeingMethod.ByBillDate, BillPeriods));
        Assert.Equal(new[] { "0-10 days=2,500.01", "11-59 days=0.00", "60-100 days=2,234.56", "Over 100 days=6,667.04" },
            SecondBand(r));
        cells = AgeingCells(r);
        Assert.Equal("Over 100 days", cells["INV-A"]);
        Assert.Equal("0-10 days", cells["INV-B"]);
        Assert.Equal("60-100 days", cells["INV-C"]);
        Assert.Equal("60-100 days", cells["INV-D"]);
        Assert.Equal("11,401.61", r.PayrollRows.Single(x => x.IsTotal).Cells[5].Text);
    }

    [Fact]
    public void Bills_payable_custom_windows_under_both_bases_off_the_reloaded_book()
    {
        var f = Build("Crit Payable");
        var r = new ReportsViewModel(Reload(f.Company), ReportKind.PayablesOutstanding);
        r.SetAsOf(f.AsOf);

        r.SetAgeingCriteria(AgeingMethod.ByDueDate, DuePeriods);
        Assert.Equal(new[] { "Not due=3,000.03", "1-15 days=0.00", "16-60 days=7,777.77", "61+ days=0.00" },
            SecondBand(r));
        Assert.Equal("16-60 days", AgeingCells(r)["PB-9"]);

        r.SetAgeingCriteria(AgeingMethod.ByBillDate, BillPeriods);
        Assert.Equal(new[] { "0-10 days=3,000.03", "11-59 days=0.00", "60-100 days=7,777.77", "Over 100 days=0.00" },
            SecondBand(r));
        Assert.Equal("0-10 days", AgeingCells(r)["PB-10"]);
    }

    [Fact]
    public void Overdue_views_take_the_same_custom_windows_under_both_bases_off_the_reloaded_book()
    {
        var f = Build("Crit Overdue");
        var r = new ReportsViewModel(Reload(f.Company), ReportKind.OverdueReceivables);
        r.SetAsOf(f.AsOf);

        r.SetAgeingCriteria(AgeingMethod.ByDueDate, DuePeriods);
        Assert.Equal(new[] { "Not due=0.00", "1-15 days=999.99", "16-60 days=1,234.57", "61+ days=6,667.04" },
            OverdueBand(r));
        Assert.EndsWith("1-15 days", SecondaryOf(r, "INV-D"));
        Assert.EndsWith("61+ days", SecondaryOf(r, "INV-A"));

        r.SetAgeingCriteria(AgeingMethod.ByBillDate, BillPeriods);
        Assert.Equal(new[] { "0-10 days=0.00", "11-59 days=0.00", "60-100 days=2,234.56", "Over 100 days=6,667.04" },
            OverdueBand(r));
        Assert.EndsWith("60-100 days", SecondaryOf(r, "INV-D"));
        Assert.EndsWith("Over 100 days", SecondaryOf(r, "INV-A"));
        Assert.Equal("8,901.60", r.Rows.Single(x => x.IsTotal).Amount);

        var p = new ReportsViewModel(Reload(f.Company), ReportKind.OverduePayables);
        p.SetAsOf(f.AsOf);
        p.SetAgeingCriteria(AgeingMethod.ByDueDate, DuePeriods);
        Assert.Equal(new[] { "Not due=0.00", "1-15 days=0.00", "16-60 days=7,777.77", "61+ days=0.00" }, OverdueBand(p));
        p.SetAgeingCriteria(AgeingMethod.ByBillDate, BillPeriods);
        Assert.Equal(new[] { "0-10 days=0.00", "11-59 days=0.00", "60-100 days=7,777.77", "Over 100 days=0.00" },
            OverdueBand(p));
    }

    [Fact]
    public void Refused_windows_leave_the_report_exactly_as_it_was()
    {
        var f = Build("Crit Refused");
        var r = new ReportsViewModel(Reload(f.Company), ReportKind.ReceivablesOutstanding);
        r.SetAsOf(f.AsOf);
        r.SetAgeingCriteria(AgeingMethod.ByDueDate, DuePeriods);
        var before = SecondBand(r);

        var error = r.SetAgeingCriteria(AgeingMethod.ByBillDate, new AgeingPeriod[] { new(0, 10), new(12, null) });
        Assert.Contains("Period 2 must start from 11 days", error, StringComparison.Ordinal);
        Assert.Equal(AgeingMethod.ByDueDate, r.AgeingMethod);
        Assert.Equal(before, SecondBand(r));
    }

    // ================================================================ egress: CSV and PDF bytes

    private static PageConfig ShippedDefault() => new()
    {
        Size = PageSize.A4,
        Orientation = PageOrientation.Portrait,
        FooterText = "Apex Solutions  -  Page {page} of {pages}",
    };

    [Fact]
    public void Bills_receivable_csv_and_pdf_carry_the_custom_windows_under_both_bases()
    {
        var f = Build("Crit Egress");
        var r = new ReportsViewModel(Reload(f.Company), ReportKind.ReceivablesOutstanding);
        r.SetAsOf(f.AsOf);

        var csvDefault = Encoding.UTF8.GetString(CsvWriter.Write(ReportTabularProjector.Project(r)));
        Assert.DoesNotContain("1-15 days", csvDefault);
        Assert.DoesNotContain("set on F6", csvDefault);

        r.SetAgeingCriteria(AgeingMethod.ByDueDate, DuePeriods);
        var csvDue = Encoding.UTF8.GetString(CsvWriter.Write(ReportTabularProjector.Project(r)));
        var pdfDue = string.Join("\n", DrawnStrings(ReportPdf.Render(ReportPrintProjector.Project(r), ShippedDefault())));
        Assert.Contains("1-15 days,999.99", csvDue);
        Assert.Contains("16-60 days,1234.57", csvDue);
        Assert.Contains("61+ days,6667.04", csvDue);
        Assert.Contains("The ageing windows were set on F6", csvDue);
        Assert.Contains("16-60 days", pdfDue);
        Assert.Contains("1,234.57", pdfDue);
        Assert.DoesNotContain("0-30 days", pdfDue);

        r.SetAgeingCriteria(AgeingMethod.ByBillDate, BillPeriods);
        var csvBill = Encoding.UTF8.GetString(CsvWriter.Write(ReportTabularProjector.Project(r)));
        var pdfBill = string.Join("\n", DrawnStrings(ReportPdf.Render(ReportPrintProjector.Project(r), ShippedDefault())));
        Assert.Contains("60-100 days,2234.56", csvBill);
        Assert.Contains("Over 100 days,6667.04", csvBill);
        Assert.Contains("Over 100 days", pdfBill);
        Assert.Contains("2,234.56", pdfBill);
        Assert.DoesNotContain("2,234.56", pdfDue);
    }

    [Fact]
    public void Overdue_receivables_csv_and_pdf_carry_the_custom_windows_under_both_bases()
    {
        var f = Build("Crit Overdue Egress");
        var r = new ReportsViewModel(Reload(f.Company), ReportKind.OverdueReceivables);
        r.SetAsOf(f.AsOf);

        r.SetAgeingCriteria(AgeingMethod.ByDueDate, DuePeriods);
        var csvDue = Encoding.UTF8.GetString(CsvWriter.Write(ReportTabularProjector.Project(r)));
        var pdfDue = DrawnStrings(ReportPdf.Render(ReportPrintProjector.Project(r), ShippedDefault()));
        Assert.Contains("16-60 days", csvDue);
        Assert.Contains("1234.57", csvDue);
        Assert.Contains(pdfDue, d => d.Contains("61+ days", StringComparison.Ordinal));

        r.SetAgeingCriteria(AgeingMethod.ByBillDate, BillPeriods);
        var csvBill = Encoding.UTF8.GetString(CsvWriter.Write(ReportTabularProjector.Project(r)));
        var pdfBill = DrawnStrings(ReportPdf.Render(ReportPrintProjector.Project(r), ShippedDefault()));
        Assert.Contains("Over 100 days", csvBill);
        Assert.Contains("2234.56", csvBill);
        Assert.Contains(pdfBill, d => d.Contains("60-100 days", StringComparison.Ordinal));
        Assert.Contains(pdfBill, d => d.Contains("2,234.56", StringComparison.Ordinal));
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

    private static void Type(MainWindow w, string text)
    {
        foreach (var ch in text)
        {
            w.KeyTextInput(ch.ToString());
            Pump(w);
        }
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

    private static TextBox Focused(MainWindow w) =>
        Assert.IsType<TextBox>(w.FocusManager?.GetFocusedElement());

    /// <summary>Clears the focused box with End then Backspaces, as an operator would, and types
    /// <paramref name="text"/>.</summary>
    private static void Retype(MainWindow w, string text)
    {
        var box = Focused(w);
        Press(w, PhysicalKey.End);
        for (var i = 0; i < 8 && (box.Text ?? string.Empty).Length > 0; i++)
            Press(w, PhysicalKey.Backspace);
        Assert.Equal(string.Empty, box.Text ?? string.Empty);
        Type(w, text);
    }

    [AvaloniaFact]
    public void F6_then_typed_windows_and_Enter_re_age_bills_receivable_by_keyboard()
    {
        var (window, f) = Open("Crit Keys", ReportKind.ReceivablesOutstanding);

        Press(window, PhysicalKey.F6);
        Assert.Equal(Screen.AgeingMethodPicker, f.Vm.CurrentScreen);
        Press(window, PhysicalKey.Enter);   // the highlight is on Ageing by Due Date

        Assert.Equal(Screen.AgeingCriteria, f.Vm.CurrentScreen);
        Assert.NotNull(f.Vm.AgeingCriteria);
        Assert.Equal(AgeingMethod.ByDueDate, f.Vm.AgeingCriteria!.Method);
        // Nothing re-aged yet: the criteria have not been accepted.
        Assert.Equal("0-30 days=999.99", SecondBand(f.Vm.Reports!)[1]);

        // The caret starts in period 1's From box, so the operator types at once.
        var from1 = Focused(window);
        Assert.Contains("ageingFrom", from1.Classes);
        Assert.Equal("1", from1.Text);

        Press(window, PhysicalKey.Tab);       // period 1 To
        Retype(window, "15");
        Press(window, PhysicalKey.Tab);       // period 2 From
        Retype(window, "16");
        Assert.Equal("15", f.Vm.AgeingCriteria!.Rows[0].To);
        Assert.Equal("16", f.Vm.AgeingCriteria!.Rows[1].From);

        Press(window, PhysicalKey.Enter);

        Assert.Equal(Screen.Report, f.Vm.CurrentScreen);
        Assert.Null(f.Vm.AgeingCriteria);
        var r = f.Vm.Reports!;
        Assert.Equal(new[]
            {
                "Not due=2,500.01", "1-15 days=999.99", "16-60 days=1,234.57", "61-90 days=6,667.04", "91+ days=0.00",
            },
            SecondBand(r));
        Assert.Equal("1-15 days", AgeingCells(r)["INV-D"]);
        window.Close();
    }

    [AvaloniaFact]
    public void A_refused_set_stays_on_the_criteria_with_the_reason_and_Escape_leaves_the_report_unchanged()
    {
        var (window, f) = Open("Crit Keys Refused", ReportKind.OverdueReceivables);
        var before = OverdueBand(f.Vm.Reports!);

        Press(window, PhysicalKey.F6);
        Press(window, PhysicalKey.ArrowDown);    // Ageing by Bill Date
        Press(window, PhysicalKey.Enter);
        Assert.Equal(Screen.AgeingCriteria, f.Vm.CurrentScreen);
        Assert.Equal(AgeingMethod.ByBillDate, f.Vm.AgeingCriteria!.Method);
        Assert.Equal("0", Focused(window).Text);

        Press(window, PhysicalKey.Tab);
        Press(window, PhysicalKey.Tab);          // period 2 From (31)
        Retype(window, "33");
        Press(window, PhysicalKey.Enter);

        Assert.Equal(Screen.AgeingCriteria, f.Vm.CurrentScreen);
        Assert.Contains("Period 2 must start from 31 days", f.Vm.AgeingCriteria!.Status, StringComparison.Ordinal);
        Assert.Equal(AgeingMethod.ByDueDate, f.Vm.Reports!.AgeingMethod);

        Press(window, PhysicalKey.Escape);
        Assert.Equal(Screen.Report, f.Vm.CurrentScreen);
        Assert.Null(f.Vm.AgeingCriteria);
        Assert.Equal(AgeingMethod.ByDueDate, f.Vm.Reports!.AgeingMethod);
        Assert.Null(f.Vm.Reports!.AgeingPeriods);
        Assert.Equal(before, OverdueBand(f.Vm.Reports!));
        window.Close();
    }

    [AvaloniaFact]
    public void Ctrl_A_accepts_the_criteria_and_a_second_F6_reseeds_with_the_windows_in_force()
    {
        var (window, f) = Open("Crit Keys CtrlA", ReportKind.PayablesOutstanding);

        Press(window, PhysicalKey.F6);
        Press(window, PhysicalKey.Enter);
        Press(window, PhysicalKey.Tab);
        Retype(window, "15");
        Press(window, PhysicalKey.Tab);
        Retype(window, "16");
        Press(window, PhysicalKey.A, RawInputModifiers.Control);

        Assert.Equal(Screen.Report, f.Vm.CurrentScreen);
        Assert.Equal("16-60 days=7,777.77", SecondBand(f.Vm.Reports!)[2]);

        Press(window, PhysicalKey.F6);
        Press(window, PhysicalKey.Enter);
        Assert.Equal("15", f.Vm.AgeingCriteria!.Rows[0].To);
        Assert.Equal("16", f.Vm.AgeingCriteria!.Rows[1].From);
        Press(window, PhysicalKey.Escape);
        Assert.Equal(Screen.Report, f.Vm.CurrentScreen);
        window.Close();
    }

    // ---------------------------------------------------------------- PDF parse

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
