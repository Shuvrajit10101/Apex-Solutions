using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// End-to-end coverage for the Interest Calculation UI surfaced in the cascade (catalog §7; plan.md §5):
/// the Ledger master's Interest-parameter block (Activate / Rate% / Per / On / Applicability / Style)
/// creates a ledger whose interest block survives a save+reload; the Interest Calculation report page
/// computes the accrued interest for a posted balance and totals it (figures right-aligned in the grid);
/// and the cascade stays correct — Interest Calculation nests under Reports → Statements of Accounts and
/// opens as ONE replacing page column. Drives the real shell VMs over a throwaway .db — no UI toolkit.
/// </summary>
public sealed class InterestViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public InterestViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexInterestTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

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

    // ---------------------------------------------------------------- (1) Ledger master interest block

    [Fact]
    public void Ledger_master_creates_an_interest_enabled_ledger_that_survives_reload()
    {
        const string companyName = "Interest Master Co";
        var vm = NewSeededCompany(companyName);

        vm.ShowLedgerMaster();
        Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);
        var master = vm.LedgerMaster!;

        // A loan ledger under Loans (Liability), with 18% p.a. simple interest on credit balances.
        master.Name = "Bank Loan";
        master.SelectedGroup = vm.Company!.FindGroupByName("Loans (Liability)");
        master.EnableInterest = true;
        master.InterestRateText = "18";
        master.SelectedPer = master.PerChoices.Single(c => c.Value == InterestPer.ThreeSixtyFiveDayYear);
        master.SelectedOnBalance = master.OnBalanceChoices.Single(c => c.Value == InterestOnBalance.CreditOnly);
        master.SelectedApplicability =
            master.ApplicabilityChoices.Single(c => c.Value == InterestApplicability.Always);
        master.SelectedStyle = master.StyleChoices.Single(c => c.Value == InterestStyle.Simple);

        Assert.True(master.Create());

        // The in-memory ledger carries the block; the list shows an interest summary.
        var created = vm.Company!.FindLedgerByName("Bank Loan")!;
        Assert.True(created.InterestEnabled);
        Assert.Equal(18m, created.Interest!.RatePercent);
        Assert.Equal(InterestPer.ThreeSixtyFiveDayYear, created.Interest.Per);
        Assert.Equal(InterestOnBalance.CreditOnly, created.Interest.OnBalance);
        Assert.Equal(InterestStyle.Simple, created.Interest.Style);
        var listed = Assert.Single(master.Existing, r => r.Name == "Bank Loan");
        Assert.Contains("18", listed.Interest);
        Assert.Contains("Simple", listed.Interest);

        // Persisted: the interest block survives a save+reload (schema v7).
        var reloaded = Reload(companyName);
        var reloadedLoan = reloaded.FindLedgerByName("Bank Loan")!;
        Assert.True(reloadedLoan.InterestEnabled);
        Assert.Equal(18m, reloadedLoan.Interest!.RatePercent);
        Assert.Equal(InterestPer.ThreeSixtyFiveDayYear, reloadedLoan.Interest.Per);
        Assert.Equal(InterestOnBalance.CreditOnly, reloadedLoan.Interest.OnBalance);
        Assert.Equal(InterestApplicability.Always, reloadedLoan.Interest.Applicability);
        Assert.Equal(InterestStyle.Simple, reloadedLoan.Interest.Style);
    }

    [Fact]
    public void Ledger_master_rejects_a_non_numeric_interest_rate_when_interest_is_activated()
    {
        var vm = NewSeededCompany("Interest Rate Reject Co");
        vm.ShowLedgerMaster();
        var master = vm.LedgerMaster!;

        master.Name = "Bad Rate Loan";
        master.SelectedGroup = vm.Company!.FindGroupByName("Loans (Liability)");
        master.EnableInterest = true;
        master.InterestRateText = "abc";

        Assert.False(master.Create());
        Assert.Contains("rate", master.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(vm.Company!.FindLedgerByName("Bad Rate Loan"));
    }

    [Fact]
    public void Ledger_master_without_activating_interest_creates_a_plain_ledger()
    {
        var vm = NewSeededCompany("Plain Ledger Co");
        vm.ShowLedgerMaster();
        var master = vm.LedgerMaster!;

        master.Name = "Rent";
        master.SelectedGroup = vm.Company!.FindGroupByName("Indirect Expenses");
        // EnableInterest stays false.
        Assert.True(master.Create());

        var rent = vm.Company!.FindLedgerByName("Rent")!;
        Assert.Null(rent.Interest);
        Assert.False(rent.InterestEnabled);
        var listed = Assert.Single(master.Existing, r => r.Name == "Rent");
        Assert.Equal(string.Empty, listed.Interest);
    }

    // ---------------------------------------------------------------- (2) Interest report computes interest

    [Fact]
    public void Interest_report_shows_the_computed_interest_for_a_posted_balance()
    {
        var vm = NewSeededCompany("Interest Report Co");
        var company = vm.Company!;

        // A loan drawn on the books-begin date (1-Apr), with 18% p.a. simple interest.
        var cash = company.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(1000000m);
        cash.OpeningIsDebit = true;

        var loan = new DomainLedger(Guid.NewGuid(), "Bank Loan",
            company.FindGroupByName("Loans (Liability)")!.Id, Money.Zero, openingIsDebit: false)
        {
            Interest = new InterestParameters(
                enabled: true, ratePercent: 18m, per: InterestPer.ThreeSixtyFiveDayYear),
        };
        company.AddLedger(loan);

        // Post the loan draw on the FY start and another voucher on FY start + 365 days so the report
        // window runs a full year and the interest is a round 18,000 (1,00,000 × 18% × 365/365).
        var fyStart = company.FinancialYearStart;          // 1-Apr-<year>
        var oneYearLater = fyStart.AddDays(365);
        var journal = company.FindVoucherTypeByName("Journal")!;
        var svc = new LedgerService(company);
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, fyStart, new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(100000m), DrCr.Debit),
            new EntryLine(loan.Id, Money.FromRupees(100000m), DrCr.Credit),
        }));
        // A later no-op-ish balanced voucher just to push the report's "as of" (last voucher date) out a year.
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, oneYearLater, new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(1m), DrCr.Debit),
            new EntryLine(loan.Id, Money.FromRupees(1m), DrCr.Credit),
        }));

        vm.OpenInterestReport();
        Assert.Equal(Screen.InterestReport, vm.CurrentScreen);
        var report = vm.InterestReport!;

        // One data row for the loan (its principal is 1,00,001 by report end; interest accrues on it) +
        // a Total row. We assert the loan row exists and the total is a positive, non-zero interest.
        var loanRow = Assert.Single(report.Rows, r => r.Ledger == "Bank Loan");
        Assert.Contains("18", loanRow.Rate);
        Assert.False(string.IsNullOrWhiteSpace(loanRow.Interest));
        Assert.Contains("Cr", loanRow.Principal);   // a credit (loan) balance

        var totalRow = Assert.Single(report.Rows, r => r.IsTotal);
        Assert.Equal("Total Interest", totalRow.Ledger);
        // The total interest is around 18,000 (a full-year accrual on ~1,00,000 at 18%). The grid formats
        // with Indian grouping ("18,000.00"); strip separators and parse invariantly.
        var total = decimal.Parse(totalRow.Interest.Replace(",", string.Empty),
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(total, 17000m, 19000m);
    }

    [Fact]
    public void Interest_report_shows_an_empty_message_when_no_ledger_has_interest()
    {
        var vm = NewSeededCompany("No Interest Co");
        vm.OpenInterestReport();
        var report = vm.InterestReport!;

        // No interest-enabled ledgers → an informational row + a zero total row.
        Assert.Contains(report.Rows, r => r.Ledger.Contains("No interest-enabled"));
        var totalRow = Assert.Single(report.Rows, r => r.IsTotal);
        Assert.Equal("Total Interest", totalRow.Ledger);
    }

    // ---------------------------------------------------------------- (3) cascade correctness

    [Fact]
    public void Interest_calculation_nests_under_statements_of_accounts_and_opens_one_page_column()
    {
        var vm = NewSeededCompany("Interest Nav Co");

        // Reports → Statements of Accounts lists an "Interest Calculation" PAGE item.
        vm.ShowStatementsOfAccountsMenu();
        Assert.Equal(GatewayMenu.StatementsOfAccounts, vm.CurrentGatewayMenu);
        var hub = vm.Columns[^1];
        Assert.True(hub.IsMenu);
        Assert.Contains(hub.Items, m => m.IsSelectable && m.Label == "Interest Calculation" && m.IsPage);

        // Highlight Interest Calculation and drill in → opens exactly ONE page column (the report).
        for (var i = 0; i < hub.Items.Count; i++)
            if (hub.Items[i].IsSelectable && hub.Items[i].Label == "Interest Calculation")
            {
                hub.SetSelected(i);
                break;
            }
        vm.DrillIn();
        Assert.Equal(Screen.InterestReport, vm.CurrentScreen);
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.NotNull(vm.InterestReport);
        Assert.Same(vm.InterestReport, vm.Columns[^1].InterestReport);

        // The menu columns to its left stay visible (root + Statements-of-Accounts hub).
        Assert.True(vm.Columns[0].IsMenu);
        Assert.Contains(vm.Columns, c => c.IsMenu && c.Title == "Statements of Accounts");

        // Opening another report REPLACES the Interest page — still exactly one page column.
        vm.OpenReport(ReportKind.BalanceSheet);
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.Null(vm.InterestReport);
        Assert.NotNull(vm.Reports);

        // Reopening the Interest report REPLACES the Balance Sheet page (never stacks).
        vm.OpenInterestReport();
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.NotNull(vm.InterestReport);
        Assert.Null(vm.Reports);

        // The root Gateway nests Statements of Accounts under REPORTS (professional hierarchy).
        vm.ShowGateway();
        var root = vm.Columns[0];
        var items = root.Items.ToList();
        var reportsHeaderIdx = items.FindIndex(i => i.IsHeader && i.Label == "Reports");
        var soaIdx = items.FindIndex(i => i.IsSelectable && i.Label == "Statements of Accounts");
        Assert.True(reportsHeaderIdx >= 0 && soaIdx > reportsHeaderIdx);
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
