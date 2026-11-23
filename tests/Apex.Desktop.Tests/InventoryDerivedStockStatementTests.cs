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
/// T2-177 (HIGH) and T2-178 part 1 — census 11.2, wave 12, track he, IV-335..IV-337 (docs/invented-vs-cloned.md §2AF).
///
/// <para><b>Ground.</b> Vendor (TallyPrime), help.tallysolutions.com/balance-sheet-tally/ (updated 2026-11-05), opened
/// by content 2026-11-22: <i>"TallyPrime considers your stock summary balance as your closing stock."</i> — a manual
/// closing stock is the route only once <i>Integrate Accounts and Inventory</i> is off. The P&amp;L page
/// (help.tallysolutions.com/profit-and-loss-statement-tally/, same day) does not speak about stock at all, so the
/// exact charging rules below are OURS and recorded as divergences.</para>
///
/// <para>Every figure is read off the EMITTED CSV (and the realised screen rows) of a book saved and RELOADED through a
/// fresh <see cref="CompanyStorage"/>. The odd-paisa book: item-master opening 10 @ 10.37 = 103.70, purchase 100 @
/// 10.37 = 1,037.00, sale 40 @ 14.21 = 568.40; closing stock 70 @ 10.37 = 725.90. Accounting truth on the
/// inventory-derived basis: 568.40 + 725.90 - 103.70 - 1,037.00 = <b>Net Profit 153.60</b>, whatever is also posted to
/// a Stock-in-Hand ledger.</para>
/// </summary>
public sealed class InventoryDerivedStockStatementTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public InventoryDerivedStockStatementTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexHeStock_" + Guid.NewGuid().ToString("N"));
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

    // ------------------------------------------------------------------ book + artefact helpers

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

    /// <summary>Screen: the realised Net Profit / Net Loss row of the report.</summary>
    private static decimal ScreenNet(ReportsViewModel r)
    {
        var row = Assert.Single(r.Rows, x => x.IsTotal && x.Particulars is "Net Profit" or "Net Loss");
        return Num(row.Amount) * (row.Particulars == "Net Loss" ? -1m : 1m);
    }

    private sealed record Horizontal(decimal Income, decimal Expenses, decimal Net, string Csv,
        List<(string Label, decimal? Amount)> Rows);

    private Horizontal HorizontalOf(Company c, DateOnly asOf, ClosingStockMode mode, bool detailed,
        PeriodRange? window = null)
    {
        var r = new ReportsViewModel(Reload(c), ReportKind.ProfitAndLoss);
        r.SetAsOf(asOf);
        if (window is { } w) r.SetPeriod(w.From, w.To);
        r.ApplyConfiguration(hideZero: false, showPercentages: false, closingStock: mode);
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

    private decimal VerticalOf(Company c, DateOnly asOf, ClosingStockMode mode, out string csv)
    {
        var r = new ReportsViewModel(Reload(c), ReportKind.ProfitAndLoss);
        r.SetAsOf(asOf);
        r.ApplyConfiguration(hideZero: false, showPercentages: false, closingStock: mode);
        r.SetShowVerticalProfitAndLoss(true);
        csv = Csv(r);
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
        return net;
    }

    private sealed record Sheet(decimal NetProfit, decimal Liabilities, decimal Assets, string Csv,
        List<(string Label, decimal? Amount)> Rows);

    private Sheet BalanceSheetOf(Company c, DateOnly asOf, ClosingStockMode mode, bool detailed = true)
    {
        var r = new ReportsViewModel(Reload(c), ReportKind.BalanceSheet);
        r.SetAsOf(asOf);
        r.ApplyConfiguration(hideZero: false, showPercentages: false, closingStock: mode);
        if (!detailed) r.ToggleDetailed();
        var csv = Csv(r);
        var rows = Parse(csv);
        var np = detailed ? rows.Single(x => x.Label.StartsWith("Net Profit (period)")).Amount!.Value : 0m;
        var tl = rows.Single(x => x.Label == "Total Liabilities").Amount!.Value;
        var ta = rows.Single(x => x.Label == "Total Assets").Amount!.Value;
        // Screen totals agree with the CSV.
        Assert.Equal(tl, Num(Assert.Single(r.Rows, x => x.IsTotal && x.Particulars == "Total Liabilities").Amount));
        Assert.Equal(ta, Num(Assert.Single(r.Rows, x => x.IsTotal && x.Particulars == "Total Assets").Amount));
        return new Sheet(np, tl, ta, csv, rows);
    }

    private sealed record Book(Company C, DateOnly AsOf);

    private Book New(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name + " " + Guid.NewGuid().ToString("N")[..6];
        vm.CreateCompany();
        return new Book(vm.Company!, vm.Company!.BooksBeginFrom.AddDays(60));
    }

    private static DomainLedger L(Company c, string n, string g, bool dr, decimal opening = 0m)
    {
        var l = new DomainLedger(Guid.NewGuid(), n, c.FindGroupByName(g)!.Id,
            opening == 0m ? Money.Zero : Money.FromRupees(opening), dr);
        c.AddLedger(l);
        return l;
    }

    /// <summary>
    /// The inventory book. <paramref name="ledgerOpening"/> = the Stock-in-Hand ledger ALSO carries 103.70 opening
    /// against Capital (P2/P4 shapes); otherwise the opening exists only on the item master (P3 shape).
    /// <paramref name="postedClosingAdj"/> = a closing-stock journal Dr Stock-in-Hand / Cr Direct-Income adjustment
    /// 725.90 on day 20 (P4 shape).
    /// </summary>
    private Book InventoryBook(string tag, bool ledgerOpening, bool postedClosingAdj)
    {
        var b = New($"He Inv {tag}");
        var c = b.C;
        var s = c.BooksBeginFrom;
        var inv = new InventoryService(c);
        var unit = inv.CreateSimpleUnit("Nos", "Numbers");
        var grp = c.FindStockGroupByName("Primary") ?? inv.CreateStockGroup("Primary");
        var item = inv.CreateStockItem("Widget He", grp.Id, unit.Id);
        var main = c.MainLocation!;
        inv.AddOpeningBalance(item.Id, main.Id, 10m, Money.FromRupees(10.37m));
        var stock = ledgerOpening
            ? L(c, "Stock He", "Stock-in-Hand", true, 103.70m)
            : L(c, "Stock He", "Stock-in-Hand", true);
        if (ledgerOpening) L(c, "Capital He", "Capital Account", false, 103.70m);
        var sup = L(c, "Supplier He", "Sundry Creditors", false);
        var cus = L(c, "Customer He", "Sundry Debtors", true);
        var pur = L(c, "Purchases He", "Purchase Accounts", true);
        var sal = L(c, "Sales He", "Sales Accounts", false);
        var svc = new LedgerService(c);
        var n = 0;
        void Bill(string type, DomainLedger nom, DomainLedger party, StockDirection dir, decimal q, decimal rate, int day)
        {
            var v = Money.FromRupees(q * rate);
            var inward = dir == StockDirection.Inward;
            svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName(type)!.Id, s.AddDays(day),
                new[]
                {
                    new EntryLine(nom.Id, v, inward ? DrCr.Debit : DrCr.Credit),
                    new EntryLine(party.Id, v, inward ? DrCr.Credit : DrCr.Debit),
                },
                number: ++n, partyId: party.Id,
                inventoryLines: new[] { new VoucherInventoryLine(item.Id, main.Id, q, Money.FromRupees(rate), dir) }));
        }
        Bill("Purchase", pur, sup, StockDirection.Inward, 100m, 10.37m, 3);
        Bill("Sales", sal, cus, StockDirection.Outward, 40m, 14.21m, 9);
        if (postedClosingAdj)
        {
            var adj = L(c, "Closing Stock Adj He", "Direct Incomes", false);
            svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Journal")!.Id, s.AddDays(20),
                new[]
                {
                    new EntryLine(stock.Id, Money.FromRupees(725.90m), DrCr.Debit),
                    new EntryLine(adj.Id, Money.FromRupees(725.90m), DrCr.Credit),
                }));
        }
        _storage.Save(c);
        return b;
    }

    // ------------------------------------------------------------------ T2-177 (a) + (b): the four shapes

    [Theory]
    [InlineData("P3", false, false)] // item-master opening only (finding 2: was 257.30)
    [InlineData("P3adj", false, true)] // item-master opening only + a posted closing-stock journal
    [InlineData("P4", true, true)] // ledger + item opening + posted closing-stock journal (finding 3: was 879.50)
    [InlineData("P2", true, false)] // ledger + item opening, nothing posted: must not charge the opening twice
    public void Inventory_derived_statement_nets_153_60_and_the_sheet_balances_on_every_shape(
        string tag, bool ledgerOpening, bool postedClosingAdj)
    {
        var b = InventoryBook(tag, ledgerOpening, postedClosingAdj);
        const ClosingStockMode mode = ClosingStockMode.InventoryDerived;

        var hd = HorizontalOf(b.C, b.AsOf, mode, detailed: true);
        var hc = HorizontalOf(b.C, b.AsOf, mode, detailed: false);
        Assert.Equal(153.60m, hd.Net);
        Assert.Equal(568.40m + 725.90m, hd.Income);           // sales + derived closing stock, nothing else
        Assert.Equal(103.70m + 1_037.00m, hd.Expenses);       // item-master opening stock + purchases
        Assert.Equal((hd.Income, hd.Expenses, hd.Net), (hc.Income, hc.Expenses, hc.Net));
        Assert.Contains(hd.Rows, x => x.Label == "Opening Stock" && x.Amount == 103.70m);
        Assert.Equal(1, hd.Rows.Count(x => x.Amount == 725.90m)); // closing stock appears ONCE
        Assert.DoesNotContain("Closing Stock Adj He", hd.Csv);    // the posted adjustment is not income here

        Assert.Equal(153.60m, VerticalOf(b.C, b.AsOf, mode, out var vcsv));
        Assert.Contains("Gross Profit,153.60", vcsv);

        var bs = BalanceSheetOf(b.C, b.AsOf, mode);
        Assert.Equal(153.60m, bs.NetProfit);
        Assert.True(bs.Liabilities == bs.Assets, bs.Csv);
        Assert.Equal(568.40m + 725.90m, bs.Assets);
        var bsc = BalanceSheetOf(b.C, b.AsOf, mode, detailed: false);
        Assert.True(bsc.Liabilities == bsc.Assets, bsc.Csv);

        // P3 shapes: the item opening had no ledger/capital counterpart, so the sheet carries it as the opening
        // difference (IV-337). P2/P4: openings agree, no such line.
        // A detailed Balance-Sheet CSV row reads "<name>  <group>"; the synthetic line uses its label for both.
        var diff = bs.Rows.Where(x => x.Label.StartsWith(BalanceSheet.OpeningDifferenceLine, StringComparison.Ordinal)).ToList();
        if (ledgerOpening) Assert.Empty(diff);
        else Assert.Equal(103.70m, Assert.Single(diff).Amount);
    }

    [Fact]
    public void Inventory_derived_opening_stock_is_window_aware_under_Alt_F2()
    {
        // Window [day 5 .. day 60]: the purchase (day 3) is before it, so the stock carried INTO the window is
        // 110 @ 10.37 = 1,140.70 — not the item-master 103.70. In-window: sale 568.40, closing 725.90, and the
        // in-window closing-stock journal is still not income. 568.40 + 725.90 - 1,140.70 = 153.60.
        var b = InventoryBook("Win", ledgerOpening: true, postedClosingAdj: true);
        var window = new PeriodRange(b.C.BooksBeginFrom.AddDays(5), b.AsOf);
        var h = HorizontalOf(b.C, b.AsOf, ClosingStockMode.InventoryDerived, detailed: true, window);
        Assert.Contains(h.Rows, x => x.Label == "Opening Stock" && x.Amount == 1_140.70m);
        Assert.DoesNotContain(h.Rows, x => x.Label == "Purchases He");
        Assert.Equal(153.60m, h.Net);
    }

    [Fact]
    public void Books_basis_is_untouched_by_the_inventory_derived_rules()
    {
        // The same P4 book on the books (as-posted) basis keeps its posted figures: the ledger opening 103.70 is the
        // opening stock and the adjustment ledger 725.90 is the closing stock, counted once. Net = 153.60 too.
        var b = InventoryBook("Books", ledgerOpening: true, postedClosingAdj: true);
        var h = HorizontalOf(b.C, b.AsOf, ClosingStockMode.AsPostedLedger, detailed: true);
        Assert.Equal(153.60m, h.Net);
        Assert.Contains(h.Rows, x => x.Label == "Closing Stock Adj He" && x.Amount == 725.90m);
        Assert.DoesNotContain(h.Rows, x => x.Label == "Closing Stock");
        var bs = BalanceSheetOf(b.C, b.AsOf, ClosingStockMode.AsPostedLedger);
        Assert.True(bs.Liabilities == bs.Assets, bs.Csv);
        Assert.DoesNotContain(bs.Rows, x => x.Label.StartsWith(BalanceSheet.OpeningDifferenceLine, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ T2-178 part 1: books-basis guards

    /// <summary>The reviewer's P1 template: opening stock 1,000.11 on the ledger, closing stock 1,500.37 posted through
    /// a Direct Income adjustment ledger, journal-only (no inventory).</summary>
    private Book BooksBook(decimal sales)
    {
        var b = New("He Books " + sales.ToString(CultureInfo.InvariantCulture));
        var c = b.C;
        var s = c.BooksBeginFrom;
        var stock = L(c, "Stock He", "Stock-in-Hand", true, 1_000.11m);
        L(c, "Capital He", "Capital Account", false, 1_000.11m);
        var cash = L(c, "Cash He", "Cash-in-Hand", true);
        var sal = L(c, "Sales He", "Sales Accounts", false);
        var pur = L(c, "Purchases He", "Purchase Accounts", true);
        var adj = L(c, "Closing Stock Adj He", "Direct Incomes", false);
        var rent = L(c, "Rent He", "Indirect Expenses", true);
        var svc = new LedgerService(c);
        var j = c.FindVoucherTypeByName("Journal")!;
        var d = 3;
        void P(DomainLedger dr, DomainLedger cr, decimal a) => svc.Post(new Voucher(Guid.NewGuid(), j.Id, s.AddDays(d++),
            new[] { new EntryLine(dr.Id, Money.FromRupees(a), DrCr.Debit), new EntryLine(cr.Id, Money.FromRupees(a), DrCr.Credit) }));
        P(cash, sal, sales);
        P(pur, cash, 5_000.29m);
        P(rent, cash, 300.07m);
        P(stock, adj, 1_500.37m);
        _storage.Save(c);
        return b;
    }

    [Theory]
    [InlineData(7_000.53, 2_200.43)] // 7000.53 + 1500.37 - 1000.11 - 5000.29 - 300.07
    [InlineData(3_000.53, -1_799.57)] // loss book
    public void Books_basis_posted_closing_stock_is_counted_once_on_screen_csv_and_Alt_C(double salesD, double expectedD)
    {
        var sales = (decimal)salesD;
        var expected = (decimal)expectedD;
        var b = BooksBook(sales);
        const ClosingStockMode mode = ClosingStockMode.AsPostedLedger;

        // ReportsViewModel.HorizontalProfitAndLossSides guard: no synthetic Closing Stock on this basis.
        var hd = HorizontalOf(b.C, b.AsOf, mode, detailed: true);
        var hc = HorizontalOf(b.C, b.AsOf, mode, detailed: false);
        Assert.Equal(expected, hd.Net);
        Assert.Equal(sales + 1_500.37m, hd.Income);
        Assert.Equal(1_000.11m + 5_000.29m + 300.07m, hd.Expenses);
        Assert.Equal((hd.Income, hd.Expenses, hd.Net), (hc.Income, hc.Expenses, hc.Net));
        Assert.DoesNotContain(hd.Rows, x => x.Label == "Closing Stock");
        Assert.Equal(1, hd.Rows.Count(x => x.Amount == 1_500.37m));

        // Vertical form guard (BuildVerticalProfitAndLoss): no "Add: Closing Stock" on this basis.
        Assert.Equal(expected, VerticalOf(b.C, b.AsOf, mode, out var vcsv));
        Assert.DoesNotContain("Add: Closing Stock", vcsv);

        var bs = BalanceSheetOf(b.C, b.AsOf, mode);
        Assert.Equal(expected, bs.NetProfit);
        Assert.True(bs.Liabilities == bs.Assets, bs.Csv);

        // ComparativeReport.BuildProfitAndLoss guard (Alt+C): two columns, each with the adjustment once and no
        // synthetic Closing Stock row; every column foots to the same Net.
        var r = new ReportsViewModel(Reload(b.C), ReportKind.ProfitAndLoss);
        r.SetAsOf(b.AsOf);
        r.ApplyConfiguration(hideZero: false, showPercentages: false, closingStock: mode);
        Assert.True(r.AddComparisonColumn("Whole period",
            new PeriodRange(b.C.BooksBeginFrom, b.AsOf), scenario: null));
        Assert.True(r.IsComparative);
        Assert.DoesNotContain(r.ComparativeRows, x => x.Label == "Closing Stock");
        var adj = Assert.Single(r.ComparativeRows, x => x.Label == "Closing Stock Adj He");
        Assert.All(adj.Cells, cell => Assert.Equal(1_500.37m, Num(cell)));
        for (var i = 0; i < r.ComparativeColumns.Count; i++)
        {
            decimal Cell(string s) => string.IsNullOrWhiteSpace(s) ? 0m : Num(s);
            var income = r.ComparativeRows.Where(x => x.GroupName == "Income").Sum(x => Cell(x.Cells[i]));
            var expenses = r.ComparativeRows.Where(x => x.GroupName == "Expenses").Sum(x => Cell(x.Cells[i]));
            Assert.True(income == sales + 1_500.37m,
                $"column {i}: income {income}, expected {sales + 1_500.37m} (closing stock counted once)");
            Assert.Equal(expected, income - expenses);
        }
        var ccsv = Csv(r);
        Assert.DoesNotContain("Closing Stock (Income)", ccsv);
    }
}
