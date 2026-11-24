using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// T2-180 (HIGH) — wave 13, track ia, IV-358/IV-359 (docs/invented-vs-cloned.md §2AH). On the inventory-derived basis a
/// Purchase item invoice whose VALUE leg is a Stock-in-Hand ledger (Dr Stock-in-Hand / Cr Supplier) was charged
/// nowhere: the P&amp;L skipped every Stock-in-Hand ledger and the Balance Sheet suppressed it, so Net Profit read
/// 1,190.60 (truth 153.60) and the sheet was out by 1,037.00; with the credit split Supplier 1,000.00 + Discount
/// Received 37.00 the IV-336 contra also stripped the discount (1,190.60 vs 190.60).
///
/// <para><b>Ground.</b> Vendor (TallyPrime), help.tallysolutions.com/stock-valuation-faq/ (updated 2026-06-05), opened
/// by content 2026-11-24: <i>"Any ledger created under the group Stock-in-Hand is used only for entering the opening and
/// closing values"</i> — the vendor never posts a purchase to one, so how such a posting is charged on the derived basis
/// is OURS (IV-358: charged as purchases; IV-359: the contra only for a posting whose every other leg is P&amp;L).</para>
///
/// <para>Every figure is read off the EMITTED CSV and the realised screen rows of a book saved and RELOADED through a
/// fresh <see cref="CompanyStorage"/>. Odd-paisa book: purchase 100 @ 10.37 = 1,037.00 to Stock-in-Hand, sale 40 @
/// 14.21 = 568.40; derived closing 60 @ 10.37 = 622.20. Truth: 568.40 + 622.20 - 1,037.00 = <b>153.60</b>; with the
/// 37.00 discount received, <b>190.60</b>.</para>
/// </summary>
public sealed class InventoryDerivedStockLegPurchaseTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public InventoryDerivedStockLegPurchaseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexIaStockLeg_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private Company Reload(Company c)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var fresh = new CompanyStorage(_tempDir);
        return fresh.Load(fresh.ListCompanies().Single(e => e.Name == c.Name));
    }

    private static string Csv(ReportsViewModel r) =>
        Encoding.UTF8.GetString(CsvWriter.Write(ReportTabularProjector.Project(r)));

    private static decimal Num(string s) =>
        decimal.Parse(s.Trim().Trim('"'), NumberStyles.Number, CultureInfo.InvariantCulture);

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

    private static decimal NetOf(List<(string Label, decimal? Amount)> rows, string csv)
    {
        var p = rows.FindIndex(x => x.Label == "Net Profit");
        var l = rows.FindIndex(x => x.Label == "Net Loss");
        Assert.True(p >= 0 ^ l >= 0, csv);
        return p >= 0 ? rows[p].Amount!.Value : -rows[l].Amount!.Value;
    }

    private static decimal ScreenNet(ReportsViewModel r)
    {
        var row = Assert.Single(r.Rows, x => x.IsTotal && x.Particulars is "Net Profit" or "Net Loss");
        return Num(row.Amount) * (row.Particulars == "Net Loss" ? -1m : 1m);
    }

    private sealed record Horizontal(decimal Income, decimal Expenses, decimal Net, string Csv,
        List<(string Label, decimal? Amount)> Rows);

    private Horizontal HorizontalOf(Company c, DateOnly asOf, bool detailed, PeriodRange? window = null)
    {
        var r = new ReportsViewModel(Reload(c), ReportKind.ProfitAndLoss);
        r.SetAsOf(asOf);
        if (window is { } w) r.SetPeriod(w.From, w.To);
        r.ApplyConfiguration(hideZero: false, showPercentages: false, closingStock: ClosingStockMode.InventoryDerived);
        if (!detailed) r.ToggleDetailed();
        var csv = Csv(r);
        var rows = Parse(csv);
        decimal Side(string h, string t)
        {
            var s = rows.FindIndex(x => x.Label == h);
            var e = rows.FindIndex(x => x.Label == t);
            Assert.True(s >= 0 && e > s, csv);
            var sum = rows.Skip(s + 1).Take(e - s - 1).Sum(x => x.Amount ?? 0m);
            Assert.True(sum == rows[e].Amount, $"{h} lines {sum} != {t} {rows[e].Amount}\n{csv}");
            return rows[e].Amount!.Value;
        }
        var inc = Side("Income", "Total Income");
        var exp = Side("Expenses", "Total Expenses");
        var net = NetOf(rows, csv);
        Assert.True(inc - exp == net, $"{inc}-{exp}!={net}\n{csv}");
        Assert.Equal(net, ScreenNet(r));
        return new Horizontal(inc, exp, net, csv, rows);
    }

    /// <summary>The vertical form: the Trading Account block must foot to Gross Profit, the Income Statements block
    /// to Net Profit. Returns (gross, net, csv).</summary>
    private (decimal Gross, decimal Net, string Csv) VerticalOf(Company c, DateOnly asOf)
    {
        var r = new ReportsViewModel(Reload(c), ReportKind.ProfitAndLoss);
        r.SetAsOf(asOf);
        r.ApplyConfiguration(hideZero: false, showPercentages: false, closingStock: ClosingStockMode.InventoryDerived);
        r.SetShowVerticalProfitAndLoss(true);
        var csv = Csv(r);
        var rows = Parse(csv);
        var ta = rows.FindIndex(x => x.Label == "Trading Account");
        var g = rows.FindIndex(x => x.Label is "Gross Profit" or "Gross Loss");
        var gsum = rows.Skip(ta + 1).Take(g - ta - 1).Sum(x => (x.Label.StartsWith("Less: ") ? -1 : 1) * (x.Amount ?? 0m));
        var gross = rows[g].Label == "Gross Profit" ? rows[g].Amount!.Value : -rows[g].Amount!.Value;
        Assert.True(gsum == gross, $"vertical trading {gsum} != {gross}\n{csv}");
        var isx = rows.FindIndex(x => x.Label == "Income Statements");
        var n = rows.FindIndex(x => x.Label is "Net Profit" or "Net Loss");
        var nsum = rows.Skip(isx + 1).Take(n - isx - 1).Sum(x => (x.Label.StartsWith("Less: ") ? -1 : 1) * (x.Amount ?? 0m));
        var net = NetOf(rows, csv);
        Assert.True(nsum == net, $"vertical income statements {nsum} != {net}\n{csv}");
        Assert.Equal(net, ScreenNet(r));
        return (gross, net, csv);
    }

    private sealed record Sheet(decimal NetProfit, decimal Liabilities, decimal Assets, string Csv);

    private Sheet BalanceSheetOf(Company c, DateOnly asOf, bool detailed)
    {
        var r = new ReportsViewModel(Reload(c), ReportKind.BalanceSheet);
        r.SetAsOf(asOf);
        r.ApplyConfiguration(hideZero: false, showPercentages: false, closingStock: ClosingStockMode.InventoryDerived);
        if (!detailed) r.ToggleDetailed();
        var csv = Csv(r);
        var rows = Parse(csv);
        var np = detailed ? rows.Single(x => x.Label.StartsWith("Net Profit (period)")).Amount!.Value : 0m;
        var tl = rows.Single(x => x.Label == "Total Liabilities").Amount!.Value;
        var ta = rows.Single(x => x.Label == "Total Assets").Amount!.Value;
        Assert.Equal(tl, Num(Assert.Single(r.Rows, x => x.IsTotal && x.Particulars == "Total Liabilities").Amount));
        Assert.Equal(ta, Num(Assert.Single(r.Rows, x => x.IsTotal && x.Particulars == "Total Assets").Amount));
        return new Sheet(np, tl, ta, csv);
    }

    private static DomainLedger L(Company c, string n, string g, bool dr)
    {
        var l = new DomainLedger(Guid.NewGuid(), n, c.FindGroupByName(g)!.Id, Money.Zero, dr);
        c.AddLedger(l);
        return l;
    }

    private sealed record Book(Company C, DateOnly AsOf);

    /// <summary>
    /// The T2-180 book. <paramref name="discount"/> splits the purchase credit Supplier 1,000.00 + Discount Received
    /// 37.00. <paramref name="closingJournal"/> ALSO posts the IV-336 closing-stock journal Dr Stock-in-Hand / Cr a
    /// Direct-Income adjustment 622.20 (a genuine closing-stock posting — its contra must still be removed).
    /// </summary>
    private Book StockLegBook(string tag, bool discount, bool closingJournal)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "Ia StockLeg " + tag + " " + Guid.NewGuid().ToString("N")[..6];
        vm.CreateCompany();
        var c = vm.Company!;
        var s = c.BooksBeginFrom;
        var inv = new InventoryService(c);
        var unit = inv.CreateSimpleUnit("Nos", "Numbers");
        var grp = c.FindStockGroupByName("Primary") ?? inv.CreateStockGroup("Primary");
        var item = inv.CreateStockItem("Widget Ia", grp.Id, unit.Id);
        var main = c.MainLocation!;
        var stock = L(c, "Stock Ia", "Stock-in-Hand", true);
        var sup = L(c, "Supplier Ia", "Sundry Creditors", false);
        var cus = L(c, "Customer Ia", "Sundry Debtors", true);
        var sal = L(c, "Sales Ia", "Sales Accounts", false);
        var disc = L(c, "Discount Received Ia", "Indirect Incomes", false);
        var svc = new LedgerService(c);

        var credit = discount
            ? new[]
            {
                new EntryLine(sup.Id, Money.FromRupees(1_000.00m), DrCr.Credit),
                new EntryLine(disc.Id, Money.FromRupees(37.00m), DrCr.Credit),
            }
            : new[] { new EntryLine(sup.Id, Money.FromRupees(1_037.00m), DrCr.Credit) };
        svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Purchase")!.Id, s.AddDays(3),
            new[] { new EntryLine(stock.Id, Money.FromRupees(1_037.00m), DrCr.Debit) }.Concat(credit).ToArray(),
            number: 1, partyId: sup.Id,
            inventoryLines: new[]
            {
                new VoucherInventoryLine(item.Id, main.Id, 100m, Money.FromRupees(10.37m), StockDirection.Inward),
            }));
        svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Sales")!.Id, s.AddDays(9),
            new[]
            {
                new EntryLine(sal.Id, Money.FromRupees(568.40m), DrCr.Credit),
                new EntryLine(cus.Id, Money.FromRupees(568.40m), DrCr.Debit),
            },
            number: 2, partyId: cus.Id,
            inventoryLines: new[]
            {
                new VoucherInventoryLine(item.Id, main.Id, 40m, Money.FromRupees(14.21m), StockDirection.Outward),
            }));
        if (closingJournal)
        {
            var adj = L(c, "Closing Stock Adj Ia", "Direct Incomes", false);
            svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Journal")!.Id, s.AddDays(20),
                new[]
                {
                    new EntryLine(stock.Id, Money.FromRupees(622.20m), DrCr.Debit),
                    new EntryLine(adj.Id, Money.FromRupees(622.20m), DrCr.Credit),
                }));
        }
        _storage.Save(c);
        return new Book(c, s.AddDays(60));
    }

    [Theory]
    [InlineData("Plain", false, false, 153.60)]   // was 1,190.60, sheet out by 1,037.00
    [InlineData("Discount", true, false, 190.60)] // was 1,190.60 (discount stripped by the contra)
    [InlineData("Journal", false, true, 153.60)]  // + IV-336 closing-stock journal: contra still removed, charge unchanged
    public void A_stock_in_hand_value_leg_is_charged_as_purchases_and_the_sheet_balances(
        string tag, bool discount, bool closingJournal, double expectedD)
    {
        var expected = (decimal)expectedD;
        var b = StockLegBook(tag, discount, closingJournal);
        var discountIncome = discount ? 37.00m : 0m;

        // Horizontal, detailed and condensed: the stock leg is a 1,037.00 purchase; the discount stays income.
        var hd = HorizontalOf(b.C, b.AsOf, detailed: true);
        Assert.Equal(expected, hd.Net);
        Assert.Equal(568.40m + 622.20m + discountIncome, hd.Income);
        Assert.Equal(1_037.00m, hd.Expenses);
        Assert.Contains(hd.Rows, x => x.Label == "Stock Ia" && x.Amount == 1_037.00m);
        if (discount) Assert.Contains(hd.Rows, x => x.Label == "Discount Received Ia" && x.Amount == 37.00m);
        Assert.DoesNotContain("Closing Stock Adj Ia", hd.Csv); // the genuine closing-stock contra is still removed
        Assert.Equal(1, hd.Rows.Count(x => x.Amount == 622.20m));

        var hc = HorizontalOf(b.C, b.AsOf, detailed: false);
        Assert.Equal((hd.Income, hd.Expenses, hd.Net), (hc.Income, hc.Expenses, hc.Net));
        Assert.Contains(hc.Rows, x => x.Label == "Purchase Accounts" && x.Amount == 1_037.00m);

        // Vertical: the charge sits above the Gross Profit line.
        var v = VerticalOf(b.C, b.AsOf);
        Assert.Equal(568.40m + 622.20m - 1_037.00m, v.Gross);
        Assert.Equal(expected, v.Net);
        Assert.Contains("Less: Stock Ia,1037.00", v.Csv);

        // Balance Sheet, detailed and condensed: sides equal, Net Profit folded = the P&L's.
        var bs = BalanceSheetOf(b.C, b.AsOf, detailed: true);
        Assert.Equal(expected, bs.NetProfit);
        Assert.True(bs.Liabilities == bs.Assets, bs.Csv);
        Assert.Equal(568.40m + 622.20m, bs.Assets);
        var bsc = BalanceSheetOf(b.C, b.AsOf, detailed: false);
        Assert.True(bsc.Liabilities == bsc.Assets, bsc.Csv);
        Assert.Equal(568.40m + 622.20m, bsc.Assets);
    }

    /// <summary>
    /// Reviewer addition (wave 13 review of track ia). An ACCOUNTING-ONLY posting to a Stock-in-Hand ledger against a
    /// Balance-Sheet ledger is a value adjustment, not a purchase, so it must NOT be charged. Two shapes, each with an
    /// ordinary purchase 100 @ 10.37 to a Purchase Accounts ledger and a sale 40 @ 14.21:
    /// <list type="bullet">
    /// <item><b>ProfitAndLossLedger</b> — the closing-stock journal Dr Stock-in-Hand 622.20 / Cr the seeded
    /// <i>Profit &amp; Loss A/c</i> ledger. Derived closing 60 @ 10.37 = 622.20; truth 568.40 + 622.20 - 1,037.00 =
    /// <b>153.60</b>. Charging it as a purchase read a 468.60 Net LOSS.</item>
    /// <item><b>Capital</b> — an item-master opening 10 @ 10.37 = 103.70 whose accounting side is booked as a journal
    /// Dr Stock-in-Hand 103.70 / Cr Capital. Derived opening 103.70, closing 70 @ 10.37 = 725.90; truth 568.40 + 725.90
    /// - 103.70 - 1,037.00 = <b>153.60</b>. Charging it charged the opening twice (Net 49.90).</item>
    /// </list>
    /// Only the Net Profit and the absence of a charge line are asserted, on the horizontal (detailed and Alt+F1) and
    /// vertical forms of the reloaded book. The Balance Sheet of these shapes is NOT asserted: the Balance-Sheet
    /// counter-leg has no derived-basis counterpart, which is a pre-existing gap and must not be pinned either way.
    /// </summary>
    [Theory]
    [InlineData("ProfitAndLossLedger")]
    [InlineData("Capital")]
    public void An_accounting_only_stock_posting_against_a_Balance_Sheet_ledger_is_not_charged_as_a_purchase(string shape)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "Ia ValueAdj " + shape + " " + Guid.NewGuid().ToString("N")[..6];
        vm.CreateCompany();
        var c = vm.Company!;
        var s = c.BooksBeginFrom;
        var inv = new InventoryService(c);
        var unit = inv.CreateSimpleUnit("Nos", "Numbers");
        var grp = c.FindStockGroupByName("Primary") ?? inv.CreateStockGroup("Primary");
        var item = inv.CreateStockItem("Widget Adj", grp.Id, unit.Id);
        var main = c.MainLocation!;
        var stock = L(c, "Stock Adj", "Stock-in-Hand", true);
        var sup = L(c, "Supplier Adj", "Sundry Creditors", false);
        var cus = L(c, "Customer Adj", "Sundry Debtors", true);
        var pur = L(c, "Purchases Adj", "Purchase Accounts", true);
        var sal = L(c, "Sales Adj", "Sales Accounts", false);
        var svc = new LedgerService(c);

        DomainLedger counter;
        decimal amount;
        if (shape == "Capital")
        {
            inv.AddOpeningBalance(item.Id, main.Id, 10m, Money.FromRupees(10.37m));
            counter = L(c, "Capital Adj", "Capital Account", false);
            amount = 103.70m;
        }
        else
        {
            counter = c.Ledgers.Single(l => l.Name == "Profit & Loss A/c");
            amount = 622.20m;
        }

        svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Purchase")!.Id, s.AddDays(3),
            new[]
            {
                new EntryLine(pur.Id, Money.FromRupees(1_037.00m), DrCr.Debit),
                new EntryLine(sup.Id, Money.FromRupees(1_037.00m), DrCr.Credit),
            },
            number: 1, partyId: sup.Id,
            inventoryLines: new[]
            {
                new VoucherInventoryLine(item.Id, main.Id, 100m, Money.FromRupees(10.37m), StockDirection.Inward),
            }));
        svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Sales")!.Id, s.AddDays(9),
            new[]
            {
                new EntryLine(sal.Id, Money.FromRupees(568.40m), DrCr.Credit),
                new EntryLine(cus.Id, Money.FromRupees(568.40m), DrCr.Debit),
            },
            number: 2, partyId: cus.Id,
            inventoryLines: new[]
            {
                new VoucherInventoryLine(item.Id, main.Id, 40m, Money.FromRupees(14.21m), StockDirection.Outward),
            }));
        svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Journal")!.Id, s.AddDays(20),
            new[]
            {
                new EntryLine(stock.Id, Money.FromRupees(amount), DrCr.Debit),
                new EntryLine(counter.Id, Money.FromRupees(amount), DrCr.Credit),
            }));
        _storage.Save(c);
        var asOf = s.AddDays(60);

        var hd = HorizontalOf(c, asOf, detailed: true);
        Assert.Equal(153.60m, hd.Net);
        Assert.DoesNotContain(hd.Rows, x => x.Label == "Stock Adj");
        var hc = HorizontalOf(c, asOf, detailed: false);
        Assert.Equal(153.60m, hc.Net);
        Assert.Contains(hc.Rows, x => x.Label == "Purchase Accounts" && x.Amount == 1_037.00m);
        var v = VerticalOf(c, asOf);
        Assert.Equal(153.60m, v.Net);
        Assert.Equal(153.60m, v.Gross);
    }

    [Fact]
    public void A_purchase_before_an_Alt_F2_window_is_opening_stock_not_a_second_charge()
    {
        // Window [day 5 .. day 60]: the day-3 purchase is before it, so the stock carried in is 100 @ 10.37 =
        // 1,037.00 as OPENING stock, and no in-window charge. 568.40 + 622.20 - 1,037.00 = 153.60.
        var b = StockLegBook("Win", discount: false, closingJournal: false);
        var h = HorizontalOf(b.C, b.AsOf, detailed: true, new PeriodRange(b.C.BooksBeginFrom.AddDays(5), b.AsOf));
        Assert.Contains(h.Rows, x => x.Label == "Opening Stock" && x.Amount == 1_037.00m);
        Assert.DoesNotContain(h.Rows, x => x.Label == "Stock Ia");
        Assert.Equal(153.60m, h.Net);
    }
}
