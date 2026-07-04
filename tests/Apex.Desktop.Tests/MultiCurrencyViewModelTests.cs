using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// End-to-end coverage for the Multi-currency UI surfaced in the cascade (catalog §2/§20; plan.md §10 C-1):
/// the Currency master creates + persists a foreign currency and dated Rates of Exchange (surviving a
/// reload); the Ledger master's Currency picker makes a forex ledger; a voucher line whose ledger is a
/// foreign currency captures forex amount + rate and posts the correct base amount through the engine; the
/// Forex Gain/Loss report shows a gain/loss on a rate change and books a balanced adjusting Journal; and
/// every path keeps the cascade correct (Currency under Masters → Create, Forex Gain/Loss under Reports →
/// Statements of Accounts, pages open as ONE replacing page column). Drives the real shell VMs over a
/// throwaway .db — no UI toolkit.
/// </summary>
public sealed class MultiCurrencyViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public MultiCurrencyViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexForexTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- helpers

    private MainWindowViewModel NewSeededCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    /// <summary>Creates a USD currency + a rate quote through the Currency master, returns the master.</summary>
    private static CurrencyMasterViewModel AddUsdWithRate(
        MainWindowViewModel vm, DateOnly date, decimal standard)
    {
        vm.ShowCurrencyMaster();
        var master = vm.CurrencyMaster!;
        master.Symbol = "$";
        master.FormalName = "USD";
        master.DecimalPlacesText = "2";
        Assert.True(master.CreateCurrency());

        master.RateDateText = Fmt(date);
        master.StandardRateText = standard.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(master.CreateRate());
        return master;
    }

    private static string Fmt(DateOnly d) =>
        d.ToString("dd-MMM-yyyy", System.Globalization.CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- (1) currency + rate + reload

    [Fact]
    public void Currency_master_creates_a_currency_and_rate_that_survive_reload()
    {
        const string companyName = "Forex Master Co";
        var vm = NewSeededCompany(companyName);

        vm.ShowCurrencyMaster();
        Assert.Equal(Screen.CurrencyMaster, vm.CurrentScreen);
        var master = vm.CurrencyMaster!;

        // Base ₹/INR is already listed; no foreign currency yet.
        Assert.Contains(master.Currencies, r => r.FormalName == "INR" && r.Kind == "Base");
        Assert.False(master.HasForeignCurrencies);

        // Create a USD currency.
        master.Symbol = "$";
        master.FormalName = "USD";
        master.DecimalPlacesText = "2";
        Assert.True(master.CreateCurrency());
        Assert.Contains(master.Currencies, r => r.FormalName == "USD" && r.Kind == "Foreign");
        Assert.True(master.HasForeignCurrencies);
        Assert.NotNull(master.RateCurrency);      // rate form auto-points at the new currency

        // Add a dated rate (₹83.25 per US$1, with directional rates).
        master.RateDateText = "01-Apr-2024";
        master.StandardRateText = "83.25";
        master.SellingRateText = "83.75";
        master.BuyingRateText = "82.75";
        Assert.True(master.CreateRate());
        Assert.Contains(master.Rates, r => r.Currency.Contains("USD") && r.Standard == "83.25");

        // Persisted: the currency + rate survive a reload.
        var reloaded = Reload(companyName);
        var usd = reloaded.FindCurrencyByName("USD")!;
        Assert.NotNull(usd);
        Assert.Equal("$", usd.Symbol);
        Assert.Equal(2, usd.DecimalPlaces);
        var rate = reloaded.RateInForce(usd.Id, new DateOnly(2024, 4, 1))!;
        Assert.Equal(83.25m, rate.StandardRate);
        Assert.Equal(83.75m, rate.RateOf(ExchangeRateKind.Selling));
        Assert.Equal(82.75m, rate.RateOf(ExchangeRateKind.Buying));
    }

    [Fact]
    public void Currency_master_rejects_a_blank_or_duplicate_currency_and_a_bad_rate()
    {
        var vm = NewSeededCompany("Forex Reject Co");
        vm.ShowCurrencyMaster();
        var master = vm.CurrencyMaster!;

        master.Symbol = "  ";
        master.FormalName = "USD";
        Assert.False(master.CreateCurrency());
        Assert.Contains("symbol is required", master.CurrencyMessage);

        master.Symbol = "$";
        master.FormalName = "USD";
        Assert.True(master.CreateCurrency());
        // Duplicate formal name is rejected.
        master.Symbol = "US$";
        master.FormalName = "USD";
        Assert.False(master.CreateCurrency());
        Assert.Contains("already exists", master.CurrencyMessage);

        // A non-numeric / non-positive rate is rejected.
        master.RateDateText = "01-Apr-2024";
        master.StandardRateText = "abc";
        Assert.False(master.CreateRate());
        Assert.Contains("Standard rate", master.RateMessage);
    }

    // ---------------------------------------------------------------- (2) ledger currency picker

    [Fact]
    public void Ledger_master_currency_picker_creates_a_foreign_currency_ledger()
    {
        const string companyName = "Forex Ledger Co";
        var vm = NewSeededCompany(companyName);
        AddUsdWithRate(vm, vm.Company!.FinancialYearStart, 83m);

        vm.ShowLedgerMaster();
        var master = vm.LedgerMaster!;
        // The picker offers base + the created USD.
        Assert.Contains(master.CurrencyChoices, c => c.CurrencyId is null);           // base
        var usdChoice = master.CurrencyChoices.Single(c => c.Display.Contains("USD"));
        Assert.NotNull(usdChoice.CurrencyId);

        master.Name = "US Customer";
        master.SelectedGroup = vm.Company!.FindGroupByName("Sundry Debtors");
        master.SelectedCurrency = usdChoice;
        Assert.True(master.Create());
        Assert.Contains(master.Existing, r => r.Name == "US Customer" && r.Currency == "USD");

        // Persisted: the ledger carries the USD currency id after a reload.
        var reloaded = Reload(companyName);
        var ledger = reloaded.FindLedgerByName("US Customer")!;
        var usd = reloaded.FindCurrencyByName("USD")!;
        Assert.True(ledger.IsForeignCurrency);
        Assert.Equal(usd.Id, ledger.CurrencyId);
    }

    // ---------------------------------------------------------------- (3) voucher forex line posts base = forex × rate

    [Fact]
    public void A_foreign_ledger_voucher_line_captures_forex_and_posts_the_correct_base_amount()
    {
        const string companyName = "Forex Voucher Co";
        var vm = NewSeededCompany(companyName);
        var fyStart = vm.Company!.FinancialYearStart;
        AddUsdWithRate(vm, fyStart, 83m);
        var usd = vm.Company!.FindCurrencyByName("USD")!;

        // A USD export-sales ledger + a base sales ledger.
        var exportSales = new DomainLedger(Guid.NewGuid(), "Export Sales",
            vm.Company!.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false,
            currencyId: usd.Id);
        var cash = vm.Company!.FindLedgerByName("Cash")!;
        vm.Company!.AddLedger(exportSales);

        // Enter a Receipt: Cash Dr ₹83,000; Export Sales Cr (US$1,000 @ 83).
        var voucherDate = fyStart.AddDays(9);
        vm.OpenVoucher(VoucherBaseType.Receipt);
        var entry = vm.VoucherEntry!;
        entry.Date = voucherDate;

        entry.Lines[0].SelectedLedger = cash;
        entry.Lines[0].Side = DrCr.Debit;
        entry.Lines[0].AmountText = "83000";

        var forexLine = entry.Lines[1];
        forexLine.SelectedLedger = exportSales;   // foreign currency ⇒ forex panel turns on
        forexLine.Side = DrCr.Credit;
        Assert.True(forexLine.IsForexLine);
        Assert.Equal("USD", forexLine.ForexCurrencyCode);
        // The rate defaulted from the rate in force (₹83 on/before the voucher date).
        Assert.Equal("83", forexLine.ForexRateText);

        forexLine.ForexAmountText = "1000";       // US$1,000 × 83 → base ₹83,000 (auto-computed)
        Assert.Equal("83000", forexLine.AmountText);
        Assert.Equal(83000m, forexLine.ParsedAmount);
        Assert.Contains("83,000", forexLine.ForexBaseText);

        Assert.True(entry.Accept());

        // The posted line carries the forex detail and the exact base amount.
        var posted = vm.Company!.Vouchers.Single();
        var line = posted.Lines.Single(l => l.HasForex);
        Assert.Equal(usd.Id, line.Forex!.CurrencyId);
        Assert.Equal(Money.FromRupees(1000m), line.Forex.ForexAmount);
        Assert.Equal(83m, line.Forex.Rate);
        Assert.Equal(Money.FromRupees(83000m), line.Amount);

        // The base ledger engine sees ₹83,000 Cr on Export Sales.
        var bal = LedgerBalances.Closing(vm.Company!, exportSales, fyStart.AddMonths(1));
        Assert.Equal(DrCr.Credit, bal.Side);
        Assert.Equal(Money.FromRupees(83000m), bal.Amount);

        // Persisted: the forex line survives a reload.
        var reloaded = Reload(companyName);
        var reloadedLine = reloaded.Vouchers.Single().Lines.Single(l => l.HasForex);
        Assert.Equal(Money.FromRupees(1000m), reloadedLine.Forex!.ForexAmount);
        Assert.Equal(83m, reloadedLine.Forex.Rate);
    }

    [Fact]
    public void A_forex_line_with_a_missing_rate_is_incomplete_and_blocks_accept()
    {
        var vm = NewSeededCompany("Forex Half Co");
        // A USD currency but NO rate quote → the rate does not auto-default.
        vm.ShowCurrencyMaster();
        vm.CurrencyMaster!.Symbol = "$";
        vm.CurrencyMaster!.FormalName = "USD";
        Assert.True(vm.CurrencyMaster!.CreateCurrency());
        var usd = vm.Company!.FindCurrencyByName("USD")!;

        var exportSales = new DomainLedger(Guid.NewGuid(), "Export Sales",
            vm.Company!.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false,
            currencyId: usd.Id);
        var cash = vm.Company!.FindLedgerByName("Cash")!;
        vm.Company!.AddLedger(exportSales);

        vm.OpenVoucher(VoucherBaseType.Receipt);
        var entry = vm.VoucherEntry!;
        entry.Date = new DateOnly(2024, 4, 10);
        entry.Lines[0].SelectedLedger = cash;
        entry.Lines[0].Side = DrCr.Debit;
        entry.Lines[0].AmountText = "83000";

        var forexLine = entry.Lines[1];
        forexLine.SelectedLedger = exportSales;
        forexLine.Side = DrCr.Credit;
        forexLine.ForexAmountText = "1000";       // forex amount but NO rate → base stays blank
        Assert.Equal(string.Empty, forexLine.ForexRateText);
        Assert.False(forexLine.IsComplete);
        Assert.False(forexLine.ForexOk);

        Assert.False(entry.Accept());
        Assert.Empty(vm.Company!.Vouchers);
    }

    [Fact]
    public void Switching_a_forex_line_back_to_a_base_ledger_clears_the_forex_panel()
    {
        var vm = NewSeededCompany("Forex Switch Co");
        var fyStart = vm.Company!.FinancialYearStart;
        AddUsdWithRate(vm, fyStart, 83m);
        var usd = vm.Company!.FindCurrencyByName("USD")!;

        var exportSales = new DomainLedger(Guid.NewGuid(), "Export Sales",
            vm.Company!.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false,
            currencyId: usd.Id);
        var domesticSales = new DomainLedger(Guid.NewGuid(), "Domestic Sales",
            vm.Company!.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
        var cash = vm.Company!.FindLedgerByName("Cash")!;
        vm.Company!.AddLedger(exportSales);
        vm.Company!.AddLedger(domesticSales);

        vm.OpenVoucher(VoucherBaseType.Receipt);
        var entry = vm.VoucherEntry!;
        entry.Date = fyStart.AddDays(9);
        entry.Lines[0].SelectedLedger = cash;
        entry.Lines[0].Side = DrCr.Debit;
        entry.Lines[0].AmountText = "83000";

        var line = entry.Lines[1];
        line.Side = DrCr.Credit;
        line.SelectedLedger = exportSales;   // forex panel on
        line.ForexAmountText = "1000";
        Assert.True(line.IsForexLine);
        Assert.Equal("83000", line.AmountText);

        // Switch to a base ledger → the forex panel + fields are cleared (no stale forex detail).
        line.SelectedLedger = domesticSales;
        Assert.False(line.IsForexLine);
        Assert.Equal(string.Empty, line.ForexAmountText);
        Assert.Equal(string.Empty, line.ForexRateText);
        Assert.Equal(string.Empty, line.ForexBaseText);
        Assert.Null(line.ToForexInfo());

        // The base line stays usable and posts with NO forex detail.
        line.AmountText = "83000";
        Assert.True(entry.Accept(), entry.Message);
        var posted = vm.Company!.Vouchers.Single().Lines.Single(l => l.LedgerId == domesticSales.Id);
        Assert.False(posted.HasForex);
    }

    // ---------------------------------------------------------------- (4) forex gain/loss report

    [Fact]
    public void Forex_report_shows_a_gain_on_a_rate_change_and_books_a_balanced_adjustment()
    {
        const string companyName = "Forex Report Co";
        var vm = NewSeededCompany(companyName);
        var fyStart = vm.Company!.FinancialYearStart;
        var fyEnd = fyStart.AddYears(1).AddDays(-1);   // 31-Mar of the FY
        // Rates: ₹80 at FY start, ₹83 at FY end.
        var master = AddUsdWithRate(vm, fyStart, 80m);
        master.RateDateText = Fmt(fyEnd);
        master.StandardRateText = "83";
        Assert.True(master.CreateRate());
        var usd = vm.Company!.FindCurrencyByName("USD")!;

        // Book a US$1,000 export receivable @ ₹80 (Debtor Dr ₹80,000; Sales Cr ₹80,000).
        var debtor = new DomainLedger(Guid.NewGuid(), "US Customer",
            vm.Company!.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, openingIsDebit: true,
            currencyId: usd.Id);
        var sales = new DomainLedger(Guid.NewGuid(), "Export Sales",
            vm.Company!.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
        vm.Company!.AddLedger(debtor);
        vm.Company!.AddLedger(sales);

        vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = vm.VoucherEntry!;
        entry.Date = fyStart.AddDays(9);
        entry.Lines[0].SelectedLedger = debtor;
        entry.Lines[0].Side = DrCr.Debit;
        entry.Lines[0].ForexAmountText = "1000";   // rate defaults to ₹80
        Assert.Equal("80", entry.Lines[0].ForexRateText);
        entry.Lines[1].SelectedLedger = sales;
        entry.Lines[1].Side = DrCr.Credit;
        entry.Lines[1].AmountText = "80000";
        Assert.True(entry.Accept());

        // Open the Forex Gain/Loss report and revalue at FY end (rate ₹83).
        vm.OpenForexReport();
        Assert.Equal(Screen.ForexReport, vm.CurrentScreen);
        var report = vm.ForexReport!;
        report.AsOfText = Fmt(fyEnd);              // triggers Recompute

        var line = report.Rows.Single(r => r.Ledger == "US Customer");
        Assert.Equal("80,000.00", line.BookedBase);
        Assert.Equal("83,000.00", line.RevaluedBase);
        Assert.Equal("Gain", line.Direction);
        Assert.Contains("3,000", line.GainLoss);
        Assert.True(report.IsNetGain);
        Assert.Contains("gain", report.NetSummary);
        Assert.True(report.CanBook);

        // Book the adjustment: a balanced Journal posts, moving the debtor to ₹83,000 and the gain to Forex Gain/Loss.
        var adj = report.BookAdjustment();
        Assert.NotNull(adj);
        Assert.Equal(adj!.TotalDebit, adj.TotalCredit);
        var debtorBal = LedgerBalances.Closing(vm.Company!, debtor, fyEnd);
        Assert.Equal(Money.FromRupees(83000m), debtorBal.Amount);
        var forexGl = vm.Company!.FindLedgerByName(ForexGainLoss.ForexGainLossLedgerName)!;
        var glBal = LedgerBalances.Closing(vm.Company!, forexGl, fyEnd);
        Assert.Equal(DrCr.Credit, glBal.Side);      // a gain = income (credit)
        Assert.Equal(Money.FromRupees(3000m), glBal.Amount);

        // After booking, re-revaluing at 83 nets to nil (the balances now match the as-of rate).
        Assert.Contains("nil", report.NetSummary, StringComparison.OrdinalIgnoreCase);

        // Persisted: the adjusting Journal survives a reload.
        var reloaded = Reload(companyName);
        var reloadedGl = reloaded.FindLedgerByName(ForexGainLoss.ForexGainLossLedgerName)!;
        Assert.Equal(Money.FromRupees(3000m), LedgerBalances.Closing(reloaded, reloadedGl, fyEnd).Amount);
    }

    [Fact]
    public void Forex_report_with_no_foreign_exposure_shows_nothing_to_revalue()
    {
        var vm = NewSeededCompany("No Forex Co");
        vm.OpenForexReport();
        var report = vm.ForexReport!;
        Assert.Equal("No forex exposure", report.NetSummary);
        Assert.False(report.CanBook);
        Assert.Null(report.BookAdjustment());
        Assert.Contains("Nothing to book", report.Message);
    }

    // ---------------------------------------------------------------- (5) cascade correctness

    [Fact]
    public void Currency_nests_under_create_and_forex_report_under_statements_and_pages_replace()
    {
        var vm = NewSeededCompany("Forex Nav Co");

        // Masters → Create lists a "Currency" page item under a Multi-Currency section.
        vm.ShowCreateMenu();
        var create = vm.Columns[^1];
        Assert.Contains(create.Items, m => m.IsHeader && m.Label == "Multi-Currency");
        Assert.Contains(create.Items, m => m.IsSelectable && m.Label == "Currency" && m.IsPage);

        // Opening the Currency master is exactly ONE page column.
        vm.ShowCurrencyMaster();
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.NotNull(vm.CurrencyMaster);
        Assert.Same(vm.CurrencyMaster, vm.Columns[^1].CurrencyMaster);

        // Reports → Statements of Accounts lists a "Forex Gain/Loss" page item.
        vm.ShowStatementsOfAccountsMenu();
        var soa = vm.Columns[^1];
        Assert.Contains(soa.Items, m => m.IsSelectable && m.Label == "Forex Gain/Loss" && m.IsPage);

        // Opening the Forex report REPLACES the page column — still exactly one page column.
        vm.OpenForexReport();
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.Null(vm.CurrencyMaster);
        Assert.NotNull(vm.ForexReport);
        Assert.Same(vm.ForexReport, vm.Columns[^1].ForexReport);

        // Esc pops the page back to the Statements-of-Accounts menu column (cascade intact).
        vm.Back();
        Assert.Equal(0, vm.Columns.Count(c => c.IsPage));
        Assert.Equal(GatewayMenu.StatementsOfAccounts, vm.CurrentGatewayMenu);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }
}
