using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
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
/// Census <b>11.2</b>, clause "No vertical form" (wave 11, track hd, IV-330..IV-333). Vendor (TallyPrime),
/// help.tallysolutions.com/profit-and-loss-statement-tally/, opened by content 2026-11-19: <i>"By default, the Profit
/// &amp; Loss A/c appears in the horizontal format."</i>; <i>"If your business prefers the vertical format, then you
/// can set the option Show Vertical Profit &amp; Loss to Yes."</i> (F12); in the vertical form the groups appear under
/// Trading Account and Income Statements.
///
/// <para>Fixture, odd paisa, read back off a book reloaded through a fresh <see cref="CompanyStorage"/>: Sales 12,345.67
/// and Sales Export 1,000.01 (Sales Accounts); Commission Received 432.19 (Indirect Incomes); Purchases 5,555.55
/// (Purchase Accounts); Freight Inward 111.11 (Direct Expenses); Rent 2,000.03 and Electricity 777.77 under a USER
/// sub-group of Indirect Expenses; opening stock 1,234.56 on a Stock-in-Hand ledger (balanced by Capital).
/// Gross Profit = 13,345.68 - 1,234.56 - 5,666.66 = 6,444.46; Net Profit = 6,444.46 + 432.19 - 2,777.80 = 4,098.85.</para>
/// </summary>
public sealed class ProfitAndLossVerticalTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ProfitAndLossVerticalTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexPlVertical_" + Guid.NewGuid().ToString("N"));
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

    private Fixture Build(string name, decimal purchases = 5_555.55m)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name + " " + Guid.NewGuid().ToString("N")[..6];
        vm.CreateCompany();
        var c = vm.Company!;
        var s = c.BooksBeginFrom;

        var office = new Group(Guid.NewGuid(), "Office Expenses", GroupNature.Expense,
            c.FindGroupByName("Indirect Expenses")!.Id);
        c.AddGroup(office);

        DomainLedger L(string ledgerName, Guid groupId, bool debit, decimal opening = 0m)
        {
            var l = new DomainLedger(Guid.NewGuid(), ledgerName, groupId,
                opening == 0m ? Money.Zero : Money.FromRupees(opening), debit);
            c.AddLedger(l);
            return l;
        }

        L("Opening Stock VPL", c.FindGroupByName("Stock-in-Hand")!.Id, true, 1_234.56m);
        L("Capital VPL", c.FindGroupByName("Capital Account")!.Id, false, 1_234.56m);
        var cash = L("Cash VPL", c.FindGroupByName("Cash-in-Hand")!.Id, true);
        var sales = L("Sales VPL", c.FindGroupByName("Sales Accounts")!.Id, false);
        var export = L("Sales Export VPL", c.FindGroupByName("Sales Accounts")!.Id, false);
        var commission = L("Commission Received", c.FindGroupByName("Indirect Incomes")!.Id, false);
        var purchase = L("Purchases VPL", c.FindGroupByName("Purchase Accounts")!.Id, true);
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
        Post(purchase, cash, purchases);
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

    private static string[] Headers(ReportsViewModel r) =>
        r.Rows.Where(x => x.IsHeader).Select(x => x.Particulars).ToArray();

    private static readonly string[] Horizontal =
    {
        "Sales VPL=12,345.67", "Sales Export VPL=1,000.01", "Commission Received=432.19", "Total Income=13,777.87",
        "Purchases VPL=5,555.55", "Freight Inward=111.11", "Rent=2,000.03", "Electricity=777.77",
        "Total Expenses=8,444.46", "Net Profit=4,098.85",
    };

    private static readonly string[] VerticalDetailed =
    {
        "Sales VPL=12,345.67", "Sales Export VPL=1,000.01", "Less: Opening Stock=1,234.56",
        "Less: Purchases VPL=5,555.55", "Less: Freight Inward=111.11", "Gross Profit=6,444.46",
        "Gross Profit b/f=6,444.46", "Commission Received=432.19", "Less: Rent=2,000.03", "Less: Electricity=777.77",
        "Net Profit=4,098.85",
    };

    private static readonly string[] VerticalCondensed =
    {
        "Sales Accounts=13,345.68", "Less: Opening Stock=1,234.56", "Less: Purchase Accounts=5,555.55",
        "Less: Direct Expenses=111.11", "Gross Profit=6,444.46", "Gross Profit b/f=6,444.46",
        "Indirect Incomes=432.19", "Less: Indirect Expenses=2,777.80", "Net Profit=4,098.85",
    };

    [Fact]
    public void Default_is_the_horizontal_form_and_the_switch_gives_the_trading_account_and_income_statement()
    {
        var f = Build("PL Vertical");
        var r = new ReportsViewModel(Reload(f.Company), ReportKind.ProfitAndLoss);
        r.SetAsOf(f.AsOf);

        Assert.True(r.SupportsVerticalProfitAndLoss);
        Assert.False(r.ShowVerticalProfitAndLoss);              // vendor: horizontal by default
        Assert.Equal(Horizontal, Lines(r));
        Assert.Equal(new[] { "Income", "Expenses" }, Headers(r));

        r.SetShowVerticalProfitAndLoss(true);

        Assert.Equal(new[] { "Trading Account", "Income Statement" }, Headers(r));
        Assert.Equal(VerticalDetailed, Lines(r));
        // A detailed ledger line still drills (RQ-7); the computed lines do not.
        var rent = Assert.Single(r.Rows, x => x.Particulars == "Less: Rent");
        Assert.NotEqual(Guid.Empty, rent.DrillLedgerId);
        Assert.Equal(Guid.Empty, Assert.Single(r.Rows, x => x.Particulars == "Gross Profit b/f").DrillLedgerId);

        r.SetShowVerticalProfitAndLoss(false);
        Assert.Equal(Horizontal, Lines(r));
    }

    [Fact]
    public void Condensed_vertical_form_rolls_each_block_up_to_primary_groups()
    {
        var f = Build("PL Vertical Condensed");
        var r = new ReportsViewModel(Reload(f.Company), ReportKind.ProfitAndLoss);
        r.SetAsOf(f.AsOf);
        r.SetShowVerticalProfitAndLoss(true);

        r.ToggleDetailed();

        Assert.Equal(VerticalCondensed, Lines(r));
        Assert.All(r.Rows.Where(x => !x.IsHeader), x => Assert.Equal(Guid.Empty, x.DrillLedgerId));
    }

    [Fact]
    public void A_gross_loss_is_carried_down_as_a_deduction_to_a_net_loss()
    {
        var f = Build("PL Vertical Loss", purchases: 15_555.55m);
        var r = new ReportsViewModel(Reload(f.Company), ReportKind.ProfitAndLoss);
        r.SetAsOf(f.AsOf);
        r.SetShowVerticalProfitAndLoss(true);

        var lines = Lines(r);
        // 13,345.68 - 1,234.56 - 15,666.66 = -3,555.54; -3,555.54 + 432.19 - 2,777.80 = -5,901.15.
        Assert.Contains("Gross Loss=3,555.54", lines);
        Assert.Contains("Less: Gross Loss b/f=3,555.54", lines);
        Assert.Equal("Net Loss=5,901.15", lines[^1]);
        Assert.DoesNotContain(lines, l => l.StartsWith("Gross Profit", StringComparison.Ordinal));
    }

    [Fact]
    public void Vertical_switch_is_offered_on_the_profit_and_loss_only_and_does_not_follow_to_another_report()
    {
        var f = Build("PL Vertical Scope");
        var r = new ReportsViewModel(Reload(f.Company), ReportKind.ProfitAndLoss);
        r.SetAsOf(f.AsOf);
        r.SetShowVerticalProfitAndLoss(true);

        r.Show(ReportKind.BalanceSheet);
        Assert.False(r.SupportsVerticalProfitAndLoss);
        Assert.False(r.ShowVerticalProfitAndLoss);
        r.SetShowVerticalProfitAndLoss(true);                    // inert off the P&L
        Assert.False(r.ShowVerticalProfitAndLoss);

        r.Show(ReportKind.ProfitAndLoss);
        Assert.Equal(new[] { "Income", "Expenses" }, Headers(r));
    }

    [Fact]
    public void Csv_and_pdf_carry_each_form_off_the_reloaded_book()
    {
        var f = Build("PL Vertical Egress");
        var r = new ReportsViewModel(Reload(f.Company), ReportKind.ProfitAndLoss);
        r.SetAsOf(f.AsOf);

        var hCsv = Csv(r);
        Assert.Contains("Total Income,13777.87", hCsv);
        Assert.DoesNotContain("Gross Profit", hCsv);
        var hPdf = Pdf(r);
        Assert.Contains("Total Expenses", hPdf);
        Assert.DoesNotContain("Trading Account", hPdf);

        r.SetShowVerticalProfitAndLoss(true);

        var vCsv = Csv(r);
        Assert.Contains("Trading Account", vCsv);
        Assert.Contains("Less: Opening Stock,1234.56", vCsv);
        Assert.Contains("Gross Profit,6444.46", vCsv);
        Assert.Contains("Income Statement", vCsv);
        Assert.Contains("Gross Profit b/f,6444.46", vCsv);
        Assert.Contains("Less: Electricity,777.77", vCsv);
        Assert.Contains("Net Profit,4098.85", vCsv);
        Assert.DoesNotContain("Total Income", vCsv);

        var vPdf = Pdf(r);
        Assert.Contains("Trading Account", vPdf);
        Assert.Contains("Income Statement", vPdf);
        Assert.Contains("Gross Profit", vPdf);
        Assert.Contains("6,444.46", vPdf);
        Assert.Contains("Net Profit", vPdf);
        Assert.Contains("4,098.85", vPdf);
        Assert.DoesNotContain("Total Income", vPdf);
    }

    [Fact]
    public void The_vertical_form_survives_a_saved_view_through_a_fresh_store_handle()
    {
        var f = Build("PL Vertical View");
        var r = new ReportsViewModel(f.Company, ReportKind.ProfitAndLoss);
        r.SetAsOf(f.AsOf);
        r.SetShowVerticalProfitAndLoss(true);
        _storage.SaveView(f.Company, "Vertical PL", r.ToSavedView());

        var reloaded = Reload(f.Company);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var view = new CompanyStorage(_tempDir).GetView(reloaded, "Vertical PL");
        Assert.NotNull(view);
        Assert.True(view!.ShowVerticalProfitAndLoss);

        var again = new ReportsViewModel(reloaded, ReportKind.ProfitAndLoss);
        again.ApplySavedView(view);
        Assert.True(again.ShowVerticalProfitAndLoss);
        Assert.Equal(VerticalDetailed, Lines(again));
    }

    [AvaloniaFact]
    public void F12_show_vertical_by_keyboard_then_Alt_F1_toggles_both_forms()
    {
        var f = Build("PL Vertical Keys");
        var window = new MainWindow { DataContext = f.Vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);
        f.Vm.OpenReport(ReportKind.ProfitAndLoss);
        Pump(window);
        f.Vm.Reports!.SetAsOf(f.AsOf);
        Pump(window);
        Assert.Equal(Horizontal, Lines(f.Vm.Reports!));

        // Alt+F1 on the horizontal form.
        window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.Alt);
        Pump(window);
        Assert.False(f.Vm.Reports!.Detailed);
        Assert.Contains("Indirect Expenses=2,777.80", Lines(f.Vm.Reports!));
        window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.Alt);
        Pump(window);
        Assert.True(f.Vm.Reports!.Detailed);

        // F12, Space on the realised "Show Vertical Profit & Loss" box, Ctrl+A.
        window.KeyPressQwerty(PhysicalKey.F12, RawInputModifiers.None);
        Pump(window);
        Assert.Equal(Screen.ReportConfig, f.Vm.CurrentScreen);
        var box = VerticalBox(window);
        Assert.False(box.IsChecked);
        Assert.True(box.Focus());
        // A CheckBox toggles on the Space key's RELEASE (Avalonia's ClickMode.Release), so send both halves.
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Pump(window);
        Assert.True(box.IsChecked);
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Pump(window);

        Assert.True(f.Vm.Reports!.ShowVerticalProfitAndLoss);
        Assert.Equal(VerticalDetailed, Lines(f.Vm.Reports!));

        // Alt+F1 on the vertical form, from the report itself.
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Pump(window);
        Assert.Equal(Screen.Report, f.Vm.CurrentScreen);
        window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.Alt);
        Pump(window);
        Assert.Equal(VerticalCondensed, Lines(f.Vm.Reports!));
        window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.Alt);
        Pump(window);
        Assert.Equal(VerticalDetailed, Lines(f.Vm.Reports!));
        window.Close();
    }

    private static CheckBox VerticalBox(Visual root) =>
        Assert.Single(root.GetVisualDescendants().OfType<CheckBox>()
            .Where(cb => cb.IsEffectivelyVisible && cb.Content as string == "Show Vertical Profit & Loss"));

    private static string Csv(ReportsViewModel r) =>
        Encoding.UTF8.GetString(CsvWriter.Write(ReportTabularProjector.Project(r)));

    private static List<string> Pdf(ReportsViewModel r) =>
        DrawnStrings(ReportPdf.Render(ReportPrintProjector.Project(r), new PageConfig
        {
            Size = PageSize.A4,
            Orientation = PageOrientation.Portrait,
            FooterText = "Apex Solutions  -  Page {page} of {pages}",
        }));

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
