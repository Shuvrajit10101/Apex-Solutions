using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
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
/// Census <b>11.2</b> — the Profit &amp; Loss A/c's <b>condensed (Alt+F1) view rolls up to one row per primary
/// group</b>, not one row per side (IV-309). Vendor (TallyPrime), opened by content 2026-11-02:
/// help.tallysolutions.com/working-with-reports/ — <i>"Press Alt+F1 or Alt+F5 (Detailed/Condensed) to view the next
/// level of details for all line entries"</i>; help.tallysolutions.com/profit-and-loss-statement-tally/ —
/// <i>"Indirect Expenses will be deducted from the Gross Profit to show the Nett Profit."</i>
///
/// <para>Fixture, odd paisa: Sales 12,345.67 and Sales Export 1,000.01 (Sales Accounts); Commission Received 432.19
/// (Indirect Incomes); Purchases 5,555.55 (Purchase Accounts); Freight Inward 111.11 (Direct Expenses); Rent 2,000.03
/// and Electricity 777.77 under a USER sub-group "Office Expenses" of Indirect Expenses, so the roll-up must climb
/// to the PRIMARY group. Figures are read off a book reloaded through a fresh <see cref="CompanyStorage"/>.</para>
/// </summary>
public sealed class ProfitAndLossCondensedTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ProfitAndLossCondensedTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexPlCondensed_" + Guid.NewGuid().ToString("N"));
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

    private sealed record Fixture(MainWindowViewModel Vm, Company Company, DateOnly AsOf);

    private Fixture Build(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name + " " + Guid.NewGuid().ToString("N")[..6];
        vm.CreateCompany();
        var c = vm.Company!;
        var s = c.BooksBeginFrom;

        var office = new Group(Guid.NewGuid(), "Office Expenses", GroupNature.Expense,
            c.FindGroupByName("Indirect Expenses")!.Id);
        c.AddGroup(office);

        DomainLedger L(string ledgerName, Guid groupId, bool debit)
        {
            var l = new DomainLedger(Guid.NewGuid(), ledgerName, groupId, Money.Zero, debit);
            c.AddLedger(l);
            return l;
        }

        var cash = L("Cash PL", c.FindGroupByName("Cash-in-Hand")!.Id, true);
        var sales = L("Sales PL", c.FindGroupByName("Sales Accounts")!.Id, false);
        var export = L("Sales Export PL", c.FindGroupByName("Sales Accounts")!.Id, false);
        var commission = L("Commission Received", c.FindGroupByName("Indirect Incomes")!.Id, false);
        var purchases = L("Purchases PL", c.FindGroupByName("Purchase Accounts")!.Id, true);
        var freight = L("Freight Inward", c.FindGroupByName("Direct Expenses")!.Id, true);
        var rent = L("Rent", office.Id, true);
        var power = L("Electricity", office.Id, true);

        var svc = new LedgerService(c);
        var journal = c.FindVoucherTypeByName("Journal")!;
        var day = 5;
        void Post(DomainLedger dr, DomainLedger cr, decimal amount) =>
            svc.Post(new Voucher(Guid.NewGuid(), journal.Id, s.AddDays(day++), new[]
            {
                new EntryLine(dr.Id, Money.FromRupees(amount), DrCr.Debit),
                new EntryLine(cr.Id, Money.FromRupees(amount), DrCr.Credit),
            }));

        Post(cash, sales, 12_345.67m);
        Post(cash, export, 1_000.01m);
        Post(cash, commission, 432.19m);
        Post(purchases, cash, 5_555.55m);
        Post(freight, cash, 111.11m);
        Post(rent, cash, 2_000.03m);
        Post(power, cash, 777.77m);

        _storage.Save(c);
        return new Fixture(vm, c, s.AddDays(60));
    }

    private Company Reload(Company c)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var fresh = new CompanyStorage(_tempDir);
        return fresh.Load(fresh.ListCompanies().Single(e => e.Name == c.Name));
    }

    private static string[] Lines(ReportsViewModel r) =>
        r.Rows.Where(x => !x.IsHeader).Select(x => $"{x.Particulars}={x.Amount}").ToArray();

    private static readonly string[] Condensed =
    {
        "Sales Accounts=13,345.68", "Indirect Incomes=432.19", "Total Income=13,777.87",
        "Purchase Accounts=5,555.55", "Direct Expenses=111.11", "Indirect Expenses=2,777.80", "Total Expenses=8,444.46",
        "Net Profit=5,333.41",
    };

    [Fact]
    public void Condensed_view_rolls_each_side_up_to_its_primary_groups_off_the_reloaded_book()
    {
        var f = Build("PL Condensed");
        var r = new ReportsViewModel(Reload(f.Company), ReportKind.ProfitAndLoss);
        r.SetAsOf(f.AsOf);
        Assert.True(r.Detailed);
        Assert.Contains("Rent=2,000.03", Lines(r));
        Assert.Contains("Electricity=777.77", Lines(r));

        r.ToggleDetailed();

        Assert.False(r.Detailed);
        Assert.Equal(Condensed, Lines(r));
        // A rolled-up group row is not one ledger: it never drills into a ledger's vouchers.
        Assert.All(r.Rows.Where(x => !x.IsHeader && !x.IsTotal), x => Assert.Equal(Guid.Empty, x.DrillLedgerId));
        // No sub-group or ledger name leaks into the condensed view.
        Assert.DoesNotContain(r.Rows, x => x.Particulars is "Office Expenses" or "Rent" or "Income" or "Expenses" && !x.IsHeader);
    }

    [Fact]
    public void Condensed_csv_and_pdf_carry_one_row_per_primary_group()
    {
        var f = Build("PL Condensed Egress");
        var r = new ReportsViewModel(Reload(f.Company), ReportKind.ProfitAndLoss);
        r.SetAsOf(f.AsOf);
        r.ToggleDetailed();

        var csv = Encoding.UTF8.GetString(CsvWriter.Write(ReportTabularProjector.Project(r)));
        Assert.Contains("Sales Accounts,13345.68", csv);
        Assert.Contains("Indirect Incomes,432.19", csv);
        Assert.Contains("Purchase Accounts,5555.55", csv);
        Assert.Contains("Direct Expenses,111.11", csv);
        Assert.Contains("Indirect Expenses,2777.80", csv);
        Assert.DoesNotContain("Office Expenses", csv);
        Assert.DoesNotContain("Rent", csv);

        var pdf = DrawnStrings(ReportPdf.Render(ReportPrintProjector.Project(r), new PageConfig
        {
            Size = PageSize.A4,
            Orientation = PageOrientation.Portrait,
            FooterText = "Apex Solutions  -  Page {page} of {pages}",
        }));
        Assert.Contains("Indirect Expenses", pdf);
        Assert.Contains("2,777.80", pdf);
        Assert.Contains("Sales Accounts", pdf);
        Assert.Contains("13,345.68", pdf);
        Assert.DoesNotContain("Electricity", pdf);
    }

    [AvaloniaFact]
    public void Alt_F1_on_the_profit_and_loss_toggles_the_primary_group_view_by_keyboard()
    {
        var f = Build("PL Condensed Keys");
        var window = new MainWindow { DataContext = f.Vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);
        f.Vm.OpenReport(ReportKind.ProfitAndLoss);
        Pump(window);
        f.Vm.Reports!.SetAsOf(f.AsOf);
        Pump(window);
        Assert.Equal(Screen.Report, f.Vm.CurrentScreen);
        Assert.True(f.Vm.Reports!.Detailed);

        window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.Alt);
        Pump(window);

        Assert.False(f.Vm.Reports!.Detailed);
        Assert.Equal(Condensed, Lines(f.Vm.Reports!));

        window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.Alt);
        Pump(window);
        Assert.True(f.Vm.Reports!.Detailed);
        Assert.Contains("Rent=2,000.03", Lines(f.Vm.Reports!));
        window.Close();
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

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
