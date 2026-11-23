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
/// T2-177 / IV-337, wave 12 track he — reviewer's pin for the ASSETS-side branch of the Balance Sheet's
/// "Difference in Opening Balances" line on the inventory-derived basis. The builder's fixtures only reach the
/// Liabilities-side branch (item-master opening with no ledger counterpart); a book whose Stock-in-Hand LEDGER opening
/// exceeds its item-master openings (ledger opening entered against Capital, item openings smaller or absent) takes
/// the other branch, and removing that branch left every builder test green.
///
/// <para>Odd-paisa book, saved and reloaded through a fresh <see cref="CompanyStorage"/>: Stock-in-Hand ledger opening
/// 103.70 Dr against Capital 103.70 Cr; item-master opening <c>q</c> @ 10.37; purchase 100 @ 10.37 = 1,037.00; sale
/// 40 @ 14.21 = 568.40. On the inventory-derived basis the stock summary governs (vendor,
/// help.tallysolutions.com/balance-sheet-tally/: "TallyPrime considers your stock summary balance as your closing
/// stock"), so the P&amp;L charges the item opening 10.37q and credits the derived closing (60 + q) @ 10.37: Net Profit
/// = 568.40 + 10.37(60 + q) - 10.37q - 1,037.00 = <b>153.60</b> for every q. The ledger opening 103.70 exceeds the item
/// opening by 103.70 - 10.37q, which the sheet carries as an ASSET-side difference so that it still balances at
/// Total 1,294.30 (capital 103.70 + creditor 1,037.00 + profit 153.60).</para>
/// </summary>
public sealed class InventoryDerivedOpeningDifferenceAssetSideTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ApexHeRevAsset_" + Guid.NewGuid().ToString("N"));
    private readonly CompanyStorage _storage;

    public InventoryDerivedOpeningDifferenceAssetSideTests() => _storage = new CompanyStorage(_dir);

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private Company Reload(Company c)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var fresh = new CompanyStorage(_dir);
        return fresh.Load(fresh.ListCompanies().Single(e => e.Name == c.Name));
    }

    private static string Csv(ReportsViewModel r) =>
        Encoding.UTF8.GetString(CsvWriter.Write(ReportTabularProjector.Project(r)));

    private static decimal Num(string s) =>
        decimal.Parse(s.Trim().Trim('"'), NumberStyles.Number, CultureInfo.InvariantCulture);

    private static List<(string Label, decimal? Amount)> Rows(string csv)
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

    private static DomainLedger L(Company c, string n, string g, bool dr, decimal opening = 0m)
    {
        var l = new DomainLedger(Guid.NewGuid(), n, c.FindGroupByName(g)!.Id,
            opening == 0m ? Money.Zero : Money.FromRupees(opening), dr);
        c.AddLedger(l);
        return l;
    }

    private (Company C, DateOnly AsOf) Book(decimal itemOpeningQty)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "He Rev Asset " + Guid.NewGuid().ToString("N")[..6];
        vm.CreateCompany();
        var c = vm.Company!;
        var s = c.BooksBeginFrom;
        var inv = new InventoryService(c);
        var unit = inv.CreateSimpleUnit("Nos", "Numbers");
        var grp = c.FindStockGroupByName("Primary") ?? inv.CreateStockGroup("Primary");
        var item = inv.CreateStockItem("Widget Rev", grp.Id, unit.Id);
        var main = c.MainLocation!;
        if (itemOpeningQty > 0m) inv.AddOpeningBalance(item.Id, main.Id, itemOpeningQty, Money.FromRupees(10.37m));
        L(c, "Stock Rev", "Stock-in-Hand", true, 103.70m);
        L(c, "Capital Rev", "Capital Account", false, 103.70m);
        var sup = L(c, "Supplier Rev", "Sundry Creditors", false);
        var cus = L(c, "Customer Rev", "Sundry Debtors", true);
        var pur = L(c, "Purchases Rev", "Purchase Accounts", true);
        var sal = L(c, "Sales Rev", "Sales Accounts", false);
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
        _storage.Save(c);
        return (c, s.AddDays(60));
    }

    [Theory]
    [InlineData(5)] // item opening 51.85 < ledger opening 103.70: difference 51.85 on the Assets side
    [InlineData(0)] // no item opening at all (ledger-only opening): difference 103.70 on the Assets side
    public void A_stock_ledger_opening_above_the_item_openings_balances_through_an_asset_side_difference(int qty)
    {
        var (c, asOf) = Book(qty);
        var itemOpening = 10.37m * qty;
        var difference = 103.70m - itemOpening;
        const ClosingStockMode mode = ClosingStockMode.InventoryDerived;

        // P&L: the stock summary's opening is charged, not the ledger's; Net 153.60 on screen and in the CSV.
        var pl = new ReportsViewModel(Reload(c), ReportKind.ProfitAndLoss);
        pl.SetAsOf(asOf);
        pl.ApplyConfiguration(hideZero: false, showPercentages: false, closingStock: mode);
        var plCsv = Csv(pl);
        var plRows = Rows(plCsv);
        Assert.Contains(plRows, x => x.Label == "Net Profit" && x.Amount == 153.60m);
        var net = Assert.Single(pl.Rows, x => x.IsTotal && x.Particulars == "Net Profit");
        Assert.Equal(153.60m, Num(net.Amount));
        if (qty > 0) Assert.Contains(plRows, x => x.Label == "Opening Stock" && x.Amount == itemOpening);
        else Assert.DoesNotContain(plRows, x => x.Label == "Opening Stock");

        foreach (var detailed in new[] { true, false })
        {
            var bs = new ReportsViewModel(Reload(c), ReportKind.BalanceSheet);
            bs.SetAsOf(asOf);
            bs.ApplyConfiguration(hideZero: false, showPercentages: false, closingStock: mode);
            if (!detailed) bs.ToggleDetailed();
            var csv = Csv(bs);
            var rows = Rows(csv);
            var tl = rows.Single(x => x.Label == "Total Liabilities").Amount;
            var ta = rows.Single(x => x.Label == "Total Assets").Amount;
            Assert.True(tl == 1_294.30m && ta == 1_294.30m, $"{(detailed ? "detailed" : "Alt+F1")}: {tl} / {ta}\n{csv}");
            Assert.Equal(ta, Num(Assert.Single(bs.Rows, x => x.IsTotal && x.Particulars == "Total Assets").Amount));

            // The difference sits between the Assets header and Total Assets, never on the Liabilities side.
            var assetsAt = rows.FindIndex(x => x.Label == "Assets");
            var diffAt = rows.FindIndex(x => x.Label.StartsWith(BalanceSheet.OpeningDifferenceLine, StringComparison.Ordinal));
            Assert.True(assetsAt >= 0 && diffAt > assetsAt, csv);
            Assert.Equal(difference, rows[diffAt].Amount);
            Assert.Equal(1, rows.Count(x => x.Label.StartsWith(BalanceSheet.OpeningDifferenceLine, StringComparison.Ordinal)));
        }
    }
}
