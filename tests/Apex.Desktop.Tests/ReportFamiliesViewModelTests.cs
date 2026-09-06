using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Desktop.Tests;

/// <summary>
/// W2-12 UI wiring — the three ABSENT report-family rows of census area 11 made <b>reachable by a user</b>,
/// which is the only thing that moves a census row off ABSENT:
/// <list type="bullet">
///   <item><b>11.6</b> the five accounting registers, under Reports → Account Books → Registers, opening
///     month-wise and drilling to the voucher-wise listing and on to the voucher.</item>
///   <item><b>11.7</b> Group Summary and Group Vouchers, under Reports → Account Books → Groups, each
///     through a group picker, with the Group-Summary drill path
///     group → sub-group → ledger → Ledger Monthly Summary → ledger vouchers → voucher.</item>
///   <item><b>11.8</b> Statistics, under Reports → Statements of Accounts.</item>
/// </list>
/// The engine projections are trusted (<c>Apex.Ledger.Tests.ReportFamiliesTests</c> derives every figure by
/// hand); these tests pin the wiring and the reachability.
/// </summary>
public sealed class ReportFamiliesViewModelTests : IDisposable
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly QuarterEnd = new(2024, 6, 30);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ReportFamiliesViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexReportFamilies_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    /// <summary>
    /// The same book as the engine test's fixture, restated here so this file is readable alone:
    /// Sales ₹15,000.00 on 10-Apr (Acme), a CANCELLED ₹9,999.00 sale on 25-Apr, ₹25,500.50 on 05-May (Acme),
    /// ₹4,499.50 on 20-May (Cash); a ₹8,000.00 Purchase on 18-Apr; a ₹2,000.00 Journal on 02-Jun. Masters add
    /// the group "North Zone" under Sundry Debtors carrying "Gamma Ltd" (opening ₹5,000.00 Dr).
    /// </summary>
    private static Company Seed(out Domain.Ledger acme, out Group sundryDebtors)
    {
        var c = CompanyFactory.CreateSeeded("Register Co", FyStart);

        sundryDebtors = c.FindGroupByName("Sundry Debtors")!;
        var northZone = new Group(Guid.NewGuid(), "North Zone", sundryDebtors.Nature, sundryDebtors.Id);
        c.AddGroup(northZone);

        acme = new Domain.Ledger(Guid.NewGuid(), "Acme Traders", sundryDebtors.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(acme);

        var gamma = new Domain.Ledger(Guid.NewGuid(), "Gamma Ltd", northZone.Id,
            Money.FromRupees(5000m), openingIsDebit: true);
        c.AddLedger(gamma);

        var salesLedger = new Domain.Ledger(Guid.NewGuid(), "Sales A/c", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(salesLedger);

        var purchaseLedger = new Domain.Ledger(Guid.NewGuid(), "Purchase A/c",
            c.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(purchaseLedger);

        var beta = new Domain.Ledger(Guid.NewGuid(), "Beta Supplies", c.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(beta);

        var rent = new Domain.Ledger(Guid.NewGuid(), "Rent", c.FindGroupByName("Indirect Expenses")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(rent);

        var cash = c.FindLedgerByName("Cash")!;
        var svc = new LedgerService(c);
        var sales = c.FindVoucherTypeByName("Sales")!;

        svc.Post(new Voucher(Guid.NewGuid(), sales.Id, new DateOnly(2024, 4, 10), new[]
        {
            new EntryLine(acme.Id, Money.FromRupees(15000m), DrCr.Debit),
            new EntryLine(salesLedger.Id, Money.FromRupees(15000m), DrCr.Credit),
        }));

        var cancelled = svc.Post(new Voucher(Guid.NewGuid(), sales.Id, new DateOnly(2024, 4, 25), new[]
        {
            new EntryLine(acme.Id, Money.FromRupees(9999m), DrCr.Debit),
            new EntryLine(salesLedger.Id, Money.FromRupees(9999m), DrCr.Credit),
        }));
        svc.Cancel(cancelled.Id);

        svc.Post(new Voucher(Guid.NewGuid(), sales.Id, new DateOnly(2024, 5, 5), new[]
        {
            new EntryLine(acme.Id, Money.FromRupees(25500.50m), DrCr.Debit),
            new EntryLine(salesLedger.Id, Money.FromRupees(25500.50m), DrCr.Credit),
        }));

        svc.Post(new Voucher(Guid.NewGuid(), sales.Id, new DateOnly(2024, 5, 20), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(4499.50m), DrCr.Debit),
            new EntryLine(salesLedger.Id, Money.FromRupees(4499.50m), DrCr.Credit),
        }));

        svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Purchase")!.Id, new DateOnly(2024, 4, 18),
            new[]
            {
                new EntryLine(purchaseLedger.Id, Money.FromRupees(8000m), DrCr.Debit),
                new EntryLine(beta.Id, Money.FromRupees(8000m), DrCr.Credit),
            }));

        svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Journal")!.Id, new DateOnly(2024, 6, 2),
            new[]
            {
                new EntryLine(rent.Id, Money.FromRupees(2000m), DrCr.Debit),
                new EntryLine(beta.Id, Money.FromRupees(2000m), DrCr.Credit),
            }));

        return c;
    }

    // =============================================================== 11.6 — register report surface

    [Fact]
    public void Sales_register_opens_month_wise_on_the_accounting_grid()
    {
        var c = Seed(out _, out _);
        var vm = new ReportsViewModel(c, ReportKind.SalesRegister);
        vm.SetPeriod(FyStart, QuarterEnd);

        Assert.Equal("Sales Register", vm.Title);
        Assert.True(vm.IsAccountingReport);
        Assert.True(vm.ShowSingleAccountingGrid);

        // Three month rows — NOT a flat list of the three live sales vouchers.
        var months = vm.Rows.Where(r => !r.IsHeader && !r.IsTotal).ToList();
        Assert.Equal(3, months.Count);
        Assert.Equal("Apr-2024", months[0].Particulars);
        Assert.Equal(IndianFormat.Amount(Money.FromRupees(15000m)), months[0].Amount);
        Assert.Equal("May-2024", months[1].Particulars);
        Assert.Equal(IndianFormat.Amount(Money.FromRupees(30000m)), months[1].Amount);
        Assert.Equal("Jun-2024", months[2].Particulars);

        var total = vm.Rows.Single(r => r.IsTotal);
        Assert.Equal(IndianFormat.AmountAlways(Money.FromRupees(45000m)), total.Amount);
    }

    [Fact]
    public void Every_register_month_row_is_drillable_and_asks_for_that_months_voucher_listing()
    {
        var c = Seed(out _, out _);
        var vm = new ReportsViewModel(c, ReportKind.SalesRegister);
        vm.SetPeriod(FyStart, QuarterEnd);

        (ReportKind Kind, DateOnly From, DateOnly To)? asked = null;
        vm.DrillToRegisterMonthRequested += (k, f, t) => asked = (k, f, t);

        var may = vm.Rows.Single(r => r.Particulars == "May-2024");
        Assert.True(may.CanDrill);
        vm.Drill(may);

        Assert.NotNull(asked);
        Assert.Equal(ReportKind.SalesRegister, asked!.Value.Kind);
        Assert.Equal(new DateOnly(2024, 5, 1), asked.Value.From);
        Assert.Equal(new DateOnly(2024, 5, 31), asked.Value.To);

        // A total row carries no drill key — Enter on it is a safe no-op.
        asked = null;
        vm.Drill(vm.Rows.Single(r => r.IsTotal));
        Assert.Null(asked);
    }

    [Fact]
    public void The_drilled_register_month_lists_that_months_vouchers_each_drilling_to_its_voucher()
    {
        var c = Seed(out _, out _);
        var vm = new ReportsViewModel(c, ReportKind.SalesRegister,
            period: new PeriodRange(new DateOnly(2024, 5, 1), new DateOnly(2024, 5, 31)),
            registerVoucherLevel: true);

        Assert.Equal("Sales Register — May-2024", vm.Title);

        var rows = vm.Rows.Where(r => !r.IsHeader && !r.IsTotal).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains("05-May-2024", rows[0].Particulars);
        Assert.Equal("Acme Traders", rows[0].Secondary);
        Assert.Equal(IndianFormat.Amount(Money.FromRupees(25500.50m)), rows[0].Amount);
        Assert.Equal(IndianFormat.Amount(Money.FromRupees(4499.50m)), rows[1].Amount);

        Guid? drilled = null;
        vm.DrillToVoucherRequested += id => drilled = id;
        Assert.True(rows[0].CanDrill);
        vm.Drill(rows[0]);
        Assert.NotNull(drilled);

        // The drilled month foots to the month row it came from: ₹30,000.00.
        var total = vm.Rows.Single(r => r.IsTotal);
        Assert.Equal(IndianFormat.AmountAlways(Money.FromRupees(30000m)), total.Amount);
    }

    [Theory]
    [InlineData(ReportKind.PurchaseRegister, "Purchase Register")]
    [InlineData(ReportKind.JournalRegister, "Journal Register")]
    [InlineData(ReportKind.CreditNoteRegister, "Credit Note Register")]
    [InlineData(ReportKind.DebitNoteRegister, "Debit Note Register")]
    public void All_five_registers_open_with_their_own_title(ReportKind kind, string title)
    {
        var c = Seed(out _, out _);
        var vm = new ReportsViewModel(c, kind);
        vm.SetPeriod(FyStart, QuarterEnd);

        Assert.Equal(title, vm.Title);
        Assert.True(vm.IsAccountingReport);
        Assert.Equal(3, vm.Rows.Count(r => !r.IsHeader && !r.IsTotal));   // three months, always
    }

    // =============================================================== 11.7 — Group Summary / Group Vouchers

    [Fact]
    public void Group_summary_opens_scoped_to_a_group_with_dr_cr_columns()
    {
        var c = Seed(out _, out var sundryDebtors);
        var vm = new ReportsViewModel(c, ReportKind.GroupSummary, scopeMasterId: sundryDebtors.Id);
        vm.SetPeriod(FyStart, QuarterEnd);

        Assert.Equal("Group Summary — Sundry Debtors", vm.Title);
        Assert.True(vm.IsTwoColumn);

        var rows = vm.Rows.Where(r => !r.IsHeader && !r.IsTotal).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("North Zone", rows[0].Particulars);
        Assert.Equal("Acme Traders", rows[1].Particulars);

        // Closing 5,000.00 Dr + 40,500.50 Dr = ₹45,500.50 Dr on the debit column.
        var total = vm.Rows.Single(r => r.IsTotal);
        Assert.Equal(IndianFormat.AmountAlways(Money.FromRupees(45500.50m)), total.Debit);
    }

    [Fact]
    public void Group_summary_drills_a_sub_group_into_its_own_summary_and_a_ledger_into_its_monthly_summary()
    {
        var c = Seed(out var acme, out var sundryDebtors);
        var vm = new ReportsViewModel(c, ReportKind.GroupSummary, scopeMasterId: sundryDebtors.Id);
        vm.SetPeriod(FyStart, QuarterEnd);

        Guid? subGroup = null;
        (Guid Ledger, DateOnly From, DateOnly To)? monthly = null;
        vm.DrillToGroupSummaryRequested += id => subGroup = id;
        vm.DrillToLedgerMonthlyRequested += (id, f, t) => monthly = (id, f, t);

        vm.Drill(vm.Rows.Single(r => r.Particulars == "North Zone"));
        Assert.NotNull(subGroup);
        Assert.Null(monthly);

        vm.Drill(vm.Rows.Single(r => r.Particulars == "Acme Traders"));
        Assert.NotNull(monthly);
        Assert.Equal(acme.Id, monthly!.Value.Ledger);
        Assert.Equal(FyStart, monthly.Value.From);
        Assert.Equal(QuarterEnd, monthly.Value.To);
    }

    [Fact]
    public void Ledger_monthly_summary_is_the_missing_level_and_drills_a_month_into_the_ledger_vouchers()
    {
        var c = Seed(out var acme, out _);
        var vm = new ReportsViewModel(c, ReportKind.LedgerMonthlySummary, scopeMasterId: acme.Id);
        vm.SetPeriod(FyStart, QuarterEnd);

        Assert.Equal("Ledger Monthly Summary — Acme Traders", vm.Title);

        var months = vm.Rows.Where(r => !r.IsHeader && !r.IsTotal).ToList();
        Assert.Equal(3, months.Count);
        Assert.Equal("Apr-2024", months[0].Particulars);
        Assert.Equal(IndianFormat.Amount(Money.FromRupees(15000m)), months[0].Debit);
        Assert.Equal(IndianFormat.Amount(Money.FromRupees(25500.50m)), months[1].Debit);

        (Guid Ledger, DateOnly From, DateOnly To, bool Movement)? asked = null;
        vm.DrillToLedgerRequested += (id, f, t, m) => asked = (id, f, t, m);
        vm.Drill(months[1]);

        Assert.NotNull(asked);
        Assert.Equal(acme.Id, asked!.Value.Ledger);
        Assert.Equal(new DateOnly(2024, 5, 1), asked.Value.From);
        Assert.Equal(new DateOnly(2024, 5, 31), asked.Value.To);
    }

    [Fact]
    public void Group_vouchers_lists_the_group_touching_vouchers_and_drills_each_to_its_voucher()
    {
        var c = Seed(out _, out var sundryDebtors);
        var vm = new ReportsViewModel(c, ReportKind.GroupVouchers, scopeMasterId: sundryDebtors.Id);
        vm.SetPeriod(FyStart, QuarterEnd);

        Assert.Equal("Group Vouchers — Sundry Debtors", vm.Title);

        var rows = vm.Rows.Where(r => !r.IsHeader && !r.IsTotal).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains("10-Apr-2024", rows[0].Particulars);
        Assert.Equal(IndianFormat.Amount(Money.FromRupees(15000m)), rows[0].Debit);

        Guid? drilled = null;
        vm.DrillToVoucherRequested += id => drilled = id;
        vm.Drill(rows[1]);
        Assert.NotNull(drilled);

        var total = vm.Rows.Single(r => r.IsTotal);
        Assert.Equal(IndianFormat.AmountAlways(Money.FromRupees(40500.50m)), total.Debit);
    }

    // =============================================================== 11.8 — Statistics

    [Fact]
    public void Statistics_shows_the_two_sections_with_counts()
    {
        var c = Seed(out _, out _);
        var vm = new ReportsViewModel(c, ReportKind.Statistics);
        vm.SetPeriod(FyStart, QuarterEnd);

        Assert.Equal("Statistics", vm.Title);
        Assert.True(vm.IsAccountingReport);

        var headers = vm.Rows.Where(r => r.IsHeader).Select(r => r.Particulars).ToList();
        Assert.Contains("Types of Vouchers", headers);
        Assert.Contains("Types of Accounts", headers);

        // Sales: 4 entered in the quarter, of which 1 cancelled.
        var sales = vm.Rows.Single(r => r.Particulars == "Sales");
        Assert.Equal("4", sales.Amount);
        Assert.Contains("1 cancelled", sales.Secondary);

        // A never-used type still appears with a zero.
        Assert.Equal("0", vm.Rows.Single(r => r.Particulars == "Contra").Amount);

        // Masters: 28 seeded groups + our "North Zone" = 29; 2 seeded ledgers + our 6 = 8.
        Assert.Equal("29", vm.Rows.Single(r => r.Particulars == "Groups").Amount);
        Assert.Equal("8", vm.Rows.Single(r => r.Particulars == "Ledgers").Amount);
    }

    // =============================================================== reachability through the real cascade

    [Fact]
    public void Account_books_nests_the_books_the_registers_and_the_group_reports_under_named_sections()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();

        vm.ShowAccountBooksMenu();

        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        Assert.Equal(GatewayMenu.AccountBooks, vm.CurrentGatewayMenu);

        // Never a flat dump — three named sections (the professional-hierarchy rule).
        var headers = vm.Menu.Where(m => m.IsHeader).Select(m => m.Label).ToArray();
        Assert.Equal(new[] { "Account Books", "Registers", "Groups" }, headers);

        var items = vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Equal(
            new[]
            {
                "Cash Book", "Bank Book", "Ledger",
                "Sales Register", "Purchase Register", "Journal Register",
                "Credit Note Register", "Debit Note Register",
                "Group Summary", "Group Vouchers",
            },
            items);
    }

    [Theory]
    [InlineData("Sales Register", ReportKind.SalesRegister)]
    [InlineData("Purchase Register", ReportKind.PurchaseRegister)]
    [InlineData("Journal Register", ReportKind.JournalRegister)]
    [InlineData("Credit Note Register", ReportKind.CreditNoteRegister)]
    [InlineData("Debit Note Register", ReportKind.DebitNoteRegister)]
    public void Activating_a_register_row_opens_that_register(string label, ReportKind expected)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();
        vm.ShowAccountBooksMenu();

        while (vm.Menu[vm.SelectedIndex].Label != label) vm.MoveDown();
        vm.ActivateSelected();

        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.NotNull(vm.Reports);
        Assert.Equal(expected, vm.Reports!.Kind);
    }

    [Fact]
    public void Group_summary_is_reachable_through_a_group_picker_and_opens_scoped()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();
        vm.ShowAccountBooksMenu();

        while (vm.Menu[vm.SelectedIndex].Label != "Group Summary") vm.MoveDown();
        vm.ActivateSelected();

        // The picker is a data-driven column of the company's own groups.
        Assert.Equal(GatewayMenu.GroupSummaryPicker, vm.CurrentGatewayMenu);
        var groups = vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Contains("Sundry Debtors", groups);

        while (vm.Menu[vm.SelectedIndex].Label != "Sundry Debtors") vm.MoveDown();
        vm.ActivateSelected();

        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.Equal(ReportKind.GroupSummary, vm.Reports!.Kind);
        Assert.Equal("Group Summary — Sundry Debtors", vm.Reports.Title);
    }

    [Fact]
    public void Group_vouchers_is_reachable_through_its_own_group_picker()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();
        vm.ShowAccountBooksMenu();

        while (vm.Menu[vm.SelectedIndex].Label != "Group Vouchers") vm.MoveDown();
        vm.ActivateSelected();

        Assert.Equal(GatewayMenu.GroupVouchersPicker, vm.CurrentGatewayMenu);
        while (vm.Menu[vm.SelectedIndex].Label != "Sundry Debtors") vm.MoveDown();
        vm.ActivateSelected();

        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.Equal(ReportKind.GroupVouchers, vm.Reports!.Kind);
    }

    [Fact]
    public void Statistics_is_reachable_under_statements_of_accounts()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();
        vm.ShowStatementsOfAccountsMenu();

        var items = vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        // W2-32 (census 12.6) appended "Multi-Account Printing" to this hub. The assertion stays an exact
        // ORDERED list — it is not weakened to a Contains — so it still fails if a leaf is dropped or reordered.
        Assert.Equal(
            new[]
            {
                "Outstandings", "Cost Centres", "Budgets", "Interest Calculation", "Forex Gain/Loss",
                "Statistics", "Multi-Account Printing",
            },
            items);

        while (vm.Menu[vm.SelectedIndex].Label != "Statistics") vm.MoveDown();
        vm.ActivateSelected();

        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.Equal(ReportKind.Statistics, vm.Reports!.Kind);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}
