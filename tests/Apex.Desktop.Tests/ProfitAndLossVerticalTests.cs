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

    // The DEFAULT form FOOTS (IV-333, wave 11 hd fix pass): the opening stock the engine charges is a line on the
    // expense side and inside Total Expenses, so 13,777.87 - 9,679.02 = 4,098.85 is the Net Profit row. Before the fix
    // this array pinned Total Expenses 8,444.46 with no opening-stock line, a statement whose sides did not reach
    // its own Net Profit.
    private static readonly string[] Horizontal =
    {
        "Sales VPL=12,345.67", "Sales Export VPL=1,000.01", "Commission Received=432.19", "Total Income=13,777.87",
        "Opening Stock=1,234.56", "Purchases VPL=5,555.55", "Freight Inward=111.11", "Rent=2,000.03",
        "Electricity=777.77", "Total Expenses=9,679.02", "Net Profit=4,098.85",
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

        Assert.Equal(new[] { "Trading Account", "Income Statements" }, Headers(r));
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
        Assert.Contains("Opening Stock,1234.56", hCsv);
        Assert.Contains("Total Expenses,9679.02", hCsv);
        Assert.DoesNotContain("Gross Profit", hCsv);
        var hPdf = Pdf(r);
        Assert.Contains("Total Expenses", hPdf);
        Assert.Contains("Opening Stock", hPdf);
        Assert.Contains("9,679.02", hPdf);
        Assert.DoesNotContain("Trading Account", hPdf);

        r.SetShowVerticalProfitAndLoss(true);

        var vCsv = Csv(r);
        Assert.Contains("Trading Account", vCsv);
        Assert.Contains("Less: Opening Stock,1234.56", vCsv);
        Assert.Contains("Gross Profit,6444.46", vCsv);
        Assert.Contains("Income Statements", vCsv);
        Assert.Contains("Gross Profit b/f,6444.46", vCsv);
        Assert.Contains("Less: Electricity,777.77", vCsv);
        Assert.Contains("Net Profit,4098.85", vCsv);
        Assert.DoesNotContain("Total Income", vCsv);

        var vPdf = Pdf(r);
        Assert.Contains("Trading Account", vPdf);
        Assert.Contains("Income Statements", vPdf);
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

    /// <summary>
    /// An inventory book for the inventory-derived closing-stock basis (RQ-6), odd paisa: Purchase 100 Widgets at 10.37
    /// (1,037.00) and Sales 40 at 14.21 (568.40), so 60 remain and the derived Closing Stock is 622.20. Commission
    /// Received 12.34 is an indirect income, so the income side carries a line AFTER the trading block. Net Profit =
    /// 568.40 + 622.20 - 1,037.00 + 12.34 = 165.94; Gross Profit = 153.60.
    /// </summary>
    private Fixture BuildInventory(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name + " " + Guid.NewGuid().ToString("N")[..6];
        vm.CreateCompany();
        var c = vm.Company!;
        var s = c.BooksBeginFrom;
        var inventory = new InventoryService(c);
        var unit = inventory.CreateSimpleUnit("Nos", "Numbers");
        var group = c.FindStockGroupByName("Primary") ?? inventory.CreateStockGroup("Primary");
        var item = inventory.CreateStockItem("Widget INV", group.Id, unit.Id);
        var main = c.MainLocation!;

        DomainLedger L(string n, string g, bool dr)
        {
            var l = new DomainLedger(Guid.NewGuid(), n, c.FindGroupByName(g)!.Id, Money.Zero, dr);
            c.AddLedger(l);
            return l;
        }

        var supplier = L("Supplier INV", "Sundry Creditors", false);
        var customer = L("Customer INV", "Sundry Debtors", true);
        var cash = L("Cash INV", "Cash-in-Hand", true);
        var purchases = L("Purchases INV", "Purchase Accounts", true);
        var sales = L("Sales INV", "Sales Accounts", false);
        var commission = L("Commission INV", "Indirect Incomes", false);
        var svc = new LedgerService(c);
        var number = 0;

        void Bill(string type, DomainLedger nominal, DomainLedger party, StockDirection dir, decimal qty, decimal rate,
            int day)
        {
            var value = Money.FromRupees(qty * rate);
            var inward = dir == StockDirection.Inward;
            svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName(type)!.Id, s.AddDays(day),
                new[]
                {
                    new EntryLine(nominal.Id, value, inward ? DrCr.Debit : DrCr.Credit),
                    new EntryLine(party.Id, value, inward ? DrCr.Credit : DrCr.Debit),
                },
                number: ++number, partyId: party.Id,
                inventoryLines: new[] { new VoucherInventoryLine(item.Id, main.Id, qty, Money.FromRupees(rate), dir) }));
        }

        Bill("Purchase", purchases, supplier, StockDirection.Inward, 100m, 10.37m, 3);
        Bill("Sales", sales, customer, StockDirection.Outward, 40m, 14.21m, 9);
        svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Journal")!.Id, s.AddDays(11), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(12.34m), DrCr.Debit),
            new EntryLine(commission.Id, Money.FromRupees(12.34m), DrCr.Credit),
        }));

        _storage.Save(c);
        return new Fixture(vm, c, s.AddDays(60));
    }

    private ReportsViewModel InventoryDerivedReport(Fixture f)
    {
        var r = new ReportsViewModel(Reload(f.Company), ReportKind.ProfitAndLoss);
        r.SetAsOf(f.AsOf);
        r.ApplyConfiguration(hideZero: false, showPercentages: false, closingStock: ClosingStockMode.InventoryDerived);
        Assert.Equal(ClosingStockMode.InventoryDerived, r.ClosingStock);
        return r;
    }

    /// <summary>
    /// Reads the EMITTED CSV of a horizontal P&amp;L and checks it foots by its own figures: the lines under each side
    /// sum to that side's total, and Total Income - Total Expenses is the Net Profit row (a Net Loss row is the
    /// negative). Returns (total income, total expenses, net) for the caller to pin.
    /// </summary>
    private static (decimal Income, decimal Expenses, decimal Net) AssertHorizontalCsvFoots(string csv)
    {
        var rows = new List<(string Label, decimal? Amount)>();
        foreach (var raw in csv.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            var comma = line.LastIndexOf(',');
            if (comma < 0) { rows.Add((line, null)); continue; }
            var tail = line[(comma + 1)..].Trim('"');
            rows.Add((line[..comma].Trim('"'),
                decimal.TryParse(tail, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null));
        }

        decimal Side(string header, string total)
        {
            var start = rows.FindIndex(x => x.Label == header);
            var end = rows.FindIndex(x => x.Label == total);
            Assert.True(start >= 0 && end > start, $"'{header}'..'{total}' not found in:\n{csv}");
            var sum = rows.Skip(start + 1).Take(end - start - 1).Sum(x => x.Amount ?? 0m);
            var stated = rows[end].Amount ?? throw new Xunit.Sdk.XunitException($"no amount on '{total}'");
            Assert.True(sum == stated, $"{header} lines sum to {sum} but {total} says {stated}:\n{csv}");
            return stated;
        }

        var income = Side("Income", "Total Income");
        var expenses = Side("Expenses", "Total Expenses");
        var profit = rows.FindIndex(x => x.Label == "Net Profit");
        var loss = rows.FindIndex(x => x.Label == "Net Loss");
        Assert.True(profit >= 0 ^ loss >= 0, $"exactly one of Net Profit / Net Loss expected:\n{csv}");
        var net = profit >= 0 ? rows[profit].Amount!.Value : -rows[loss].Amount!.Value;
        Assert.True(income - expenses == net,
            $"Total Income {income} - Total Expenses {expenses} = {income - expenses}, but the statement says {net}:\n{csv}");
        return (income, expenses, net);
    }

    [Fact]
    public void Horizontal_form_foots_on_the_books_basis_in_both_views_off_the_reloaded_book()
    {
        foreach (var purchases in new[] { 5_555.55m, 15_555.55m })   // a profit book and a loss book
        {
            var f = Build("PL Foots Books " + purchases, purchases);
            var r = new ReportsViewModel(Reload(f.Company), ReportKind.ProfitAndLoss);
            r.SetAsOf(f.AsOf);
            Assert.Equal(ClosingStockMode.AsPostedLedger, r.ClosingStock);

            var detailed = AssertHorizontalCsvFoots(Csv(r));
            Assert.Equal(purchases == 5_555.55m ? 4_098.85m : -5_901.15m, detailed.Net);
            Assert.Equal(purchases + 111.11m + 2_777.80m + 1_234.56m, detailed.Expenses);

            r.ToggleDetailed();                                       // Alt+F1: primary-group roll-ups
            Assert.Equal(detailed, AssertHorizontalCsvFoots(Csv(r)));
            Assert.Contains("Opening Stock=1,234.56", Lines(r));
        }
    }

    [Fact]
    public void Horizontal_form_foots_on_the_inventory_derived_basis_with_the_closing_stock_on_the_income_side()
    {
        var f = BuildInventory("PL Foots Inventory");
        var r = InventoryDerivedReport(f);

        Assert.Equal(new[]
        {
            "Sales INV=568.40", "Closing Stock=622.20", "Commission INV=12.34", "Total Income=1,202.94",
            "Purchases INV=1,037.00", "Total Expenses=1,037.00", "Net Profit=165.94",
        }, Lines(r));
        Assert.Equal(Guid.Empty, Assert.Single(r.Rows, x => x.Particulars == "Closing Stock").DrillLedgerId);

        var csv = Csv(r);
        Assert.Equal((1_202.94m, 1_037.00m, 165.94m), AssertHorizontalCsvFoots(csv));
        Assert.Contains("Closing Stock,622.20", csv);
        var pdf = Pdf(r);
        Assert.Contains("Closing Stock", pdf);
        Assert.Contains("1,202.94", pdf);

        r.ToggleDetailed();
        Assert.Equal((1_202.94m, 1_037.00m, 165.94m), AssertHorizontalCsvFoots(Csv(r)));
        Assert.Contains("Closing Stock=622.20", Lines(r));
    }

    [Fact]
    public void Vertical_form_adds_the_inventory_derived_closing_stock_in_the_trading_account()
    {
        var f = BuildInventory("PL Vertical Inventory");
        var r = InventoryDerivedReport(f);
        r.SetShowVerticalProfitAndLoss(true);

        // 568.40 + 622.20 - 1,037.00 = Gross Profit 153.60; 153.60 + 12.34 = Net Profit 165.94.
        Assert.Equal(new[]
        {
            "Sales INV=568.40", "Add: Closing Stock=622.20", "Less: Purchases INV=1,037.00", "Gross Profit=153.60",
            "Gross Profit b/f=153.60", "Commission INV=12.34", "Net Profit=165.94",
        }, Lines(r));
        var csv = Csv(r);
        Assert.Contains("Add: Closing Stock,622.20", csv);
        Assert.Contains("Gross Profit,153.60", csv);
        Assert.Contains("Net Profit,165.94", csv);
    }

    /// <summary>
    /// Every Alt+C column of the realised comparative grid foots: its Income cells minus its Expenses cells is that
    /// column's own Net Profit / Net Loss caption. Returns the per-column nets.
    /// </summary>
    private static decimal[] AssertComparativeColumnsFoot(ReportsViewModel r)
    {
        Assert.True(r.IsComparative);
        static decimal Parse(string s) => string.IsNullOrWhiteSpace(s)
            ? 0m
            : decimal.Parse(s.Trim(), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture);
        var nets = new decimal[r.ComparativeColumns.Count];
        for (var i = 0; i < r.ComparativeColumns.Count; i++)
        {
            var income = r.ComparativeRows.Where(x => x.GroupName == "Income").Sum(x => Parse(x.Cells[i]));
            var expenses = r.ComparativeRows.Where(x => x.GroupName == "Expenses").Sum(x => Parse(x.Cells[i]));
            var caption = r.ComparativeColumns[i].TotalText;
            var m = System.Text.RegularExpressions.Regex.Match(caption, @"Net (Profit|Loss)\s+([\d,]+\.\d{2})");
            Assert.True(m.Success, $"no Net Profit / Net Loss in column caption '{caption}'");
            var net = Parse(m.Groups[2].Value) * (m.Groups[1].Value == "Loss" ? -1m : 1m);
            Assert.True(income - expenses == net,
                $"column {i}: income {income} - expenses {expenses} = {income - expenses}, but its caption says {caption}");
            nets[i] = net;
        }
        return nets;
    }

    [Fact]
    public void Comparative_columns_foot_with_the_stock_lines_on_both_bases_off_the_reloaded_book()
    {
        var books = Build("PL Foots Alt C");
        var r = new ReportsViewModel(Reload(books.Company), ReportKind.ProfitAndLoss);
        r.SetAsOf(books.AsOf);
        Assert.True(r.AddComparisonColumn("Whole period",
            new PeriodRange(books.Company.BooksBeginFrom, books.AsOf), scenario: null));
        Assert.All(AssertComparativeColumnsFoot(r), n => Assert.Equal(4_098.85m, n));
        var opening = Assert.Single(r.ComparativeRows, x => x.Label == "Opening Stock");
        Assert.All(opening.Cells, c => Assert.Equal("1,234.56", c));
        Assert.Contains("Opening Stock (Expenses)", Csv(r));

        var inv = BuildInventory("PL Foots Alt C Inventory");
        var ri = InventoryDerivedReport(inv);
        Assert.True(ri.AddComparisonColumn("Whole period",
            new PeriodRange(inv.Company.BooksBeginFrom, inv.AsOf), scenario: null));
        Assert.All(AssertComparativeColumnsFoot(ri), n => Assert.Equal(165.94m, n));
        var closing = Assert.Single(ri.ComparativeRows, x => x.Label == "Closing Stock");
        Assert.All(closing.Cells, c => Assert.Equal("622.20", c));
        Assert.Contains("Closing Stock (Income)", Csv(ri));
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
