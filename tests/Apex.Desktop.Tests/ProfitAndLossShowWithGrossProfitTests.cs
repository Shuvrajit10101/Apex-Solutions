using System;
using System.Collections.Generic;
using System.Globalization;
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
/// Census 11.2 clause (4) — F12 <i>Show with Gross Profit</i> on the HORIZONTAL Profit &amp; Loss A/c (IV-333; wave 13,
/// track ia, IV-360, docs/invented-vs-cloned.md §2AH).
///
/// <para><b>Ground.</b> Vendor (TallyPrime), help.tallysolutions.com/profit-and-loss-statement-tally/ (updated
/// 2026-11-05), opened by content 2026-11-24: <i>"If you want to split the Profit &amp; Loss A/c to see the trading
/// profit, then set Show with Gross Profit to Yes."</i> and <i>"This option is available only in the horizontal form of
/// the Profit &amp; Loss A/c."</i> The page draws no rows and names no c/o / b/f lines; that layout is OURS (IV-360).</para>
///
/// <para>Odd-paisa inventory book, reloaded through a fresh <see cref="CompanyStorage"/>: purchase 100 @ 10.37 =
/// 1,037.00, sale 40 @ <c>rate</c>, derived closing 60 @ 10.37 = 622.20, Commission Received 12.34 (indirect income),
/// Rent 3.21 (indirect expense). Rate 14.21: Gross Profit 153.60, Net 162.73. Rate 5.03: Gross LOSS 213.60, Net Loss
/// 204.47.</para>
/// </summary>
public sealed class ProfitAndLossShowWithGrossProfitTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ProfitAndLossShowWithGrossProfitTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexIaGross_" + Guid.NewGuid().ToString("N"));
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

    private Company Reload(Company c)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var fresh = new CompanyStorage(_tempDir);
        return fresh.Load(fresh.ListCompanies().Single(e => e.Name == c.Name));
    }

    private Fixture Build(string name, decimal saleRate)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name + " " + Guid.NewGuid().ToString("N")[..6];
        vm.CreateCompany();
        var c = vm.Company!;
        var s = c.BooksBeginFrom;
        var inv = new InventoryService(c);
        var unit = inv.CreateSimpleUnit("Nos", "Numbers");
        var grp = c.FindStockGroupByName("Primary") ?? inv.CreateStockGroup("Primary");
        var item = inv.CreateStockItem("Widget GP", grp.Id, unit.Id);
        var main = c.MainLocation!;

        DomainLedger L(string n, string g, bool dr)
        {
            var l = new DomainLedger(Guid.NewGuid(), n, c.FindGroupByName(g)!.Id, Money.Zero, dr);
            c.AddLedger(l);
            return l;
        }

        var supplier = L("Supplier GP", "Sundry Creditors", false);
        var customer = L("Customer GP", "Sundry Debtors", true);
        var cash = L("Cash GP", "Cash-in-Hand", true);
        var purchases = L("Purchases GP", "Purchase Accounts", true);
        var sales = L("Sales GP", "Sales Accounts", false);
        var commission = L("Commission GP", "Indirect Incomes", false);
        var rent = L("Rent GP", "Indirect Expenses", true);
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

        void Journal(DomainLedger dr, DomainLedger cr, decimal amount, int day) =>
            svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Journal")!.Id, s.AddDays(day), new[]
            {
                new EntryLine(dr.Id, Money.FromRupees(amount), DrCr.Debit),
                new EntryLine(cr.Id, Money.FromRupees(amount), DrCr.Credit),
            }));

        Bill("Purchase", purchases, supplier, StockDirection.Inward, 100m, 10.37m, 3);
        Bill("Sales", sales, customer, StockDirection.Outward, 40m, saleRate, 9);
        Journal(cash, commission, 12.34m, 11);
        Journal(rent, cash, 3.21m, 12);
        _storage.Save(c);
        return new Fixture(vm, c, s.AddDays(60));
    }

    private ReportsViewModel Report(Fixture f, bool detailed = true)
    {
        var r = new ReportsViewModel(Reload(f.Company), ReportKind.ProfitAndLoss);
        r.SetAsOf(f.AsOf);
        r.ApplyConfiguration(hideZero: false, showPercentages: false, closingStock: ClosingStockMode.InventoryDerived);
        if (!detailed) r.ToggleDetailed();
        return r;
    }

    private static string[] Lines(ReportsViewModel r) =>
        r.Rows.Select(x => x.IsHeader ? $"[{x.Particulars}]" : $"{x.Particulars}={x.Amount}").ToArray();

    private static string Csv(ReportsViewModel r) =>
        Encoding.UTF8.GetString(CsvWriter.Write(ReportTabularProjector.Project(r)));

    private static List<(string Label, decimal? Amount)> Parse(string csv)
    {
        var rows = new List<(string, decimal?)>();
        foreach (var raw in csv.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            var comma = line.LastIndexOf(',');
            if (comma < 0) { rows.Add((line, null)); continue; }
            var tail = line[(comma + 1)..].Trim('"');
            rows.Add((line[..comma].Trim('"'),
                decimal.TryParse(tail, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null));
        }
        return rows;
    }

    /// <summary>
    /// Reads the EMITTED CSV of the split form and checks it foots by its own figures: in each of the two blocks the
    /// lines under each side sum to that side's total; the Trading Account's two totals are equal; and the Profit &amp;
    /// Loss A/c's Total Income - Total Expenses IS the Net Profit row (negative for Net Loss). Returns
    /// (trading total, gross c/o signed, P&amp;L income, P&amp;L expenses, net).
    /// </summary>
    private static (decimal Trading, decimal Gross, decimal Income, decimal Expenses, decimal Net) AssertFoots(string csv)
    {
        var rows = Parse(csv);
        var ta = rows.FindIndex(x => x.Label == "Trading Account");
        var pa = rows.FindIndex(x => x.Label == "Profit & Loss A/c");
        Assert.True(ta >= 0 && pa > ta, csv);

        decimal Side(int from, int to, string header, string total)
        {
            var s = rows.FindIndex(from, to - from, x => x.Label == header);
            var e = rows.FindIndex(from, to - from, x => x.Label == total);
            Assert.True(s >= 0 && e > s, $"{header}/{total} in [{from},{to})\n{csv}");
            var sum = rows.Skip(s + 1).Take(e - s - 1).Sum(x => x.Amount ?? 0m);
            Assert.True(sum == rows[e].Amount, $"{header} lines {sum} != {total} {rows[e].Amount}\n{csv}");
            return rows[e].Amount!.Value;
        }

        var ti = Side(ta, pa, "Income", "Total Income");
        var te = Side(ta, pa, "Expenses", "Total Expenses");
        Assert.True(ti == te, $"trading sides {ti} != {te}\n{csv}");
        var gp = rows.FindIndex(x => x.Label == "Gross Profit c/o");
        var gl = rows.FindIndex(x => x.Label == "Gross Loss c/o");
        Assert.True(gp >= 0 ^ gl >= 0, csv);
        Assert.True((gp >= 0 ? gp : gl) > ta && (gp >= 0 ? gp : gl) < pa, csv);
        var gross = gp >= 0 ? rows[gp].Amount!.Value : -rows[gl].Amount!.Value;

        var end = rows.Count;
        var pi = Side(pa, end, "Income", "Total Income");
        var pe = Side(pa, end, "Expenses", "Total Expenses");
        // b/f mirrors c/o and sits inside the P&L block.
        if (gross >= 0)
            Assert.Equal(gross, rows.Single(x => x.Label == "Gross Profit b/f").Amount);
        else
            Assert.Equal(-gross, rows.Single(x => x.Label == "Gross Loss b/f").Amount);
        Assert.True(rows.FindIndex(x => x.Label is "Gross Profit b/f" or "Gross Loss b/f") > pa, csv);

        var np = rows.FindIndex(x => x.Label == "Net Profit");
        var nl = rows.FindIndex(x => x.Label == "Net Loss");
        Assert.True(np >= 0 ^ nl >= 0, csv);
        var net = np >= 0 ? rows[np].Amount!.Value : -rows[nl].Amount!.Value;
        Assert.True(pi - pe == net, $"{pi}-{pe}!={net}\n{csv}");
        return (ti, gross, pi, pe, net);
    }

    [Theory]
    [InlineData(14.21, 1_190.60, 153.60, 162.73)]    // 568.40 + 622.20 | 1,037.00 + c/o 153.60
    [InlineData(5.03, 1_037.00, -213.60, -204.47)]   // 201.20 + 622.20 + Gross Loss c/o 213.60 | 1,037.00
    public void Show_with_Gross_Profit_splits_the_horizontal_form_and_both_blocks_foot_on_screen_csv_and_pdf(
        double rateD, double tradingD, double grossD, double netD)
    {
        var f = Build("GP Split " + rateD.ToString(CultureInfo.InvariantCulture), (decimal)rateD);
        var r = Report(f);
        Assert.True(r.SupportsShowWithGrossProfit);
        Assert.False(r.ShowWithGrossProfit);
        var plainNet = r.Rows.Single(x => x.IsTotal && x.Particulars is "Net Profit" or "Net Loss").Amount;
        Assert.DoesNotContain(r.Rows, x => x.Particulars.Contains("c/o"));

        r.SetShowWithGrossProfit(true);
        Assert.True(r.ShowWithGrossProfit);

        foreach (var detailed in new[] { true, false })
        {
            if (!detailed) r.ToggleDetailed();
            var csv = Csv(r);
            var (trading, gross, income, expenses, net) = AssertFoots(csv);
            Assert.Equal((decimal)tradingD, trading);
            Assert.Equal((decimal)grossD, gross);
            Assert.Equal((decimal)netD, net);
            Assert.Equal(net, income - expenses);
            // Same Net as the unsplit form, read off the screen.
            Assert.Equal(plainNet, r.Rows.Single(x => x.IsTotal && x.Particulars is "Net Profit" or "Net Loss").Amount);
            // The engine's own Gross Profit, which the vertical form also shows.
            var pl = ProfitAndLoss.Build(Reload(f.Company), f.AsOf, ClosingStockMode.InventoryDerived);
            Assert.Equal((decimal)grossD, pl.GrossProfit.Amount);
            // Indirect lines sit in the P&L block, never in Trading.
            var lines = Lines(r);
            var pa = Array.IndexOf(lines, "[Profit & Loss A/c]");
            Assert.True(pa > 0, string.Join("\n", lines));
            var commission = Array.FindIndex(lines, x => x.StartsWith(detailed ? "Commission GP=" : "Indirect Incomes="));
            Assert.True(commission > pa, string.Join("\n", lines));
            var closing = Array.FindIndex(lines, x => x.StartsWith("Closing Stock="));
            Assert.True(closing > 0 && closing < pa, string.Join("\n", lines));

            var pdf = Pdf(r);
            var label = (decimal)grossD >= 0 ? "Gross Profit" : "Gross Loss";
            Assert.Contains($"{label} c/o", pdf);
            Assert.Contains($"{label} b/f", pdf);
            Assert.Contains("Trading Account", pdf);
            Assert.Contains(Math.Abs((decimal)grossD).ToString("#,##0.00", CultureInfo.InvariantCulture), pdf);
        }

        // Off again: the default form, unchanged.
        r.SetShowWithGrossProfit(false);
        Assert.DoesNotContain(r.Rows, x => x.Particulars.Contains("c/o") || x.Particulars.Contains("b/f"));
    }

    /// <summary>
    /// Reviewer addition (wave 13 review of track ia): the split form with a NON-ZERO opening stock. Every other test in
    /// this class has opening stock 0, so dropping the Opening Stock line from the Trading Account survived them all
    /// (mutation R3) — and on any book that carries opening stock the Trading Account would then not foot. An Alt+F2
    /// window from day 5 puts the day-3 purchase (100 @ 10.37) before the window, so the derived opening is 1,037.00:
    /// Trading 568.40 + 622.20 = 1,190.60 | Opening Stock 1,037.00 + Gross Profit c/o 153.60; P&amp;L 153.60 + 12.34 -
    /// 3.21 = Net 162.73. Read off the emitted CSV of a reloaded book, detailed and Alt+F1.
    /// </summary>
    [Fact]
    public void Show_with_Gross_Profit_carries_a_non_zero_Opening_Stock_inside_the_Trading_Account()
    {
        var f = Build("GP Opening", 14.21m);
        var r = new ReportsViewModel(Reload(f.Company), ReportKind.ProfitAndLoss);
        r.SetAsOf(f.AsOf);
        r.SetPeriod(f.Company.BooksBeginFrom.AddDays(5), f.AsOf);
        r.ApplyConfiguration(hideZero: false, showPercentages: false, closingStock: ClosingStockMode.InventoryDerived);
        r.SetShowWithGrossProfit(true);
        Assert.True(r.ShowWithGrossProfit);

        foreach (var detailed in new[] { true, false })
        {
            if (!detailed) r.ToggleDetailed();
            var csv = Csv(r);
            var (trading, gross, _, _, net) = AssertFoots(csv);
            Assert.Equal(1_190.60m, trading);
            Assert.Equal(153.60m, gross);
            Assert.Equal(162.73m, net);
            var rows = Parse(csv);
            var ta = rows.FindIndex(x => x.Label == "Trading Account");
            var pa = rows.FindIndex(x => x.Label == "Profit & Loss A/c");
            var opening = rows.FindIndex(x => x.Label == "Opening Stock");
            Assert.True(opening > ta && opening < pa, csv);
            Assert.Equal(1_037.00m, rows[opening].Amount);
        }
    }

    [Fact]
    public void Show_with_Gross_Profit_is_horizontal_only_and_P_and_L_only()
    {
        var f = Build("GP Gate", 14.21m);
        var r = Report(f);
        r.SetShowVerticalProfitAndLoss(true);
        Assert.False(r.SupportsShowWithGrossProfit);
        r.SetShowWithGrossProfit(true);                      // inert on the vertical form
        Assert.False(r.ShowWithGrossProfit);
        Assert.DoesNotContain(r.Rows, x => x.Particulars.Contains("c/o"));

        r.SetShowVerticalProfitAndLoss(false);
        r.SetShowWithGrossProfit(true);
        Assert.Contains(r.Rows, x => x.Particulars == "Gross Profit c/o");
        // Vertical wins when both are set: the split rows go, the vertical form shows.
        r.SetShowVerticalProfitAndLoss(true);
        Assert.DoesNotContain(r.Rows, x => x.Particulars.Contains("c/o"));
        Assert.Contains(r.Rows, x => x.Particulars == "Gross Profit b/f");

        var bs = new ReportsViewModel(Reload(f.Company), ReportKind.BalanceSheet);
        Assert.False(bs.SupportsShowWithGrossProfit);
    }

    [Fact]
    public void Save_View_round_trips_Show_with_Gross_Profit_through_a_fresh_store()
    {
        var f = Build("GP View", 14.21m);
        var r = Report(f);
        r.SetShowWithGrossProfit(true);
        var expected = Lines(r);
        _storage.SaveView(f.Company, "Split PL", r.ToSavedView());

        var reloaded = Reload(f.Company);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var view = new CompanyStorage(_tempDir).GetView(reloaded, "Split PL");
        Assert.NotNull(view);
        Assert.True(view!.ShowWithGrossProfit);

        var again = new ReportsViewModel(reloaded, ReportKind.ProfitAndLoss);
        again.ApplySavedView(view);
        Assert.True(again.ShowWithGrossProfit);
        Assert.Equal(expected, Lines(again));
    }

    [AvaloniaFact]
    public void F12_Show_with_Gross_Profit_by_keyboard_splits_the_form_and_Vertical_hides_the_box()
    {
        var f = Build("GP Keys", 14.21m);
        var window = new MainWindow { DataContext = f.Vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);
        f.Vm.OpenReport(ReportKind.ProfitAndLoss);
        Pump(window);
        f.Vm.Reports!.SetAsOf(f.AsOf);
        Pump(window);
        Assert.DoesNotContain(f.Vm.Reports!.Rows, x => x.Particulars == "Gross Profit c/o");

        // F12, Space on the realised "Show with Gross Profit" box, Ctrl+A.
        window.KeyPressQwerty(PhysicalKey.F12, RawInputModifiers.None);
        Pump(window);
        Assert.Equal(Screen.ReportConfig, f.Vm.CurrentScreen);
        var box = Box(window, "Show with Gross Profit");
        Assert.NotNull(box);
        Assert.False(box!.IsChecked);
        Assert.True(box.Focus());
        // A CheckBox toggles on the Space key's RELEASE (Avalonia's ClickMode.Release), so send both halves.
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Pump(window);
        Assert.True(box.IsChecked);
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Pump(window);
        Assert.True(f.Vm.Reports!.ShowWithGrossProfit);
        // The window opens on the books (as-posted) basis, where no closing stock is posted on this book, so the
        // trading account shows a Gross LOSS: 568.40 - 1,037.00 = -468.60, carried down and brought forward.
        Assert.Contains(f.Vm.Reports!.Rows, x => x.Particulars == "Gross Loss c/o" && x.Amount == "468.60");
        Assert.Contains(f.Vm.Reports!.Rows, x => x.Particulars == "Gross Loss b/f" && x.Amount == "468.60");

        // Back in F12, ticking Vertical hides the Gross Profit box at once (horizontal-only).
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Pump(window);
        Assert.Equal(Screen.Report, f.Vm.CurrentScreen);
        window.KeyPressQwerty(PhysicalKey.F12, RawInputModifiers.None);
        Pump(window);
        Assert.Equal(Screen.ReportConfig, f.Vm.CurrentScreen);
        var vertical = Box(window, "Show Vertical Profit & Loss");
        Assert.NotNull(vertical);
        Assert.True(vertical!.Focus());
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Pump(window);
        Assert.True(vertical.IsChecked);
        Assert.Null(Box(window, "Show with Gross Profit"));
        window.Close();
    }

    private static CheckBox? Box(Visual root, string content) =>
        root.GetVisualDescendants().OfType<CheckBox>()
            .SingleOrDefault(cb => cb.IsEffectivelyVisible && cb.Content as string == content);

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
