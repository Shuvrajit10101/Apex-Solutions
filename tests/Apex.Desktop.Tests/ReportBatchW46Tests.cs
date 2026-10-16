using System;
using System.IO;
using System.Linq;
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
/// Wave 46, census rows <b>11.10</b> and <b>11.13</b> — reach, not presence.
///
/// <para><b>11.13 (cluster C2).</b> Enter was a silent no-op on the Memorandum Register and the Reversing
/// Journal Register. <c>ReportsViewModel.Drill</c> has no <c>default</c> arm, so a kind with no case falls
/// straight out of the switch and the keystroke does nothing. The memorandum projection already set the row's
/// voucher id (census 4.17) and STILL could not be drilled — carrying the id lights up the verbs that resolve
/// it through the shell, never Enter. The reversing-journal projection never copied the id its engine row has
/// always carried, so neither half worked there. Vendor: <i>"You can drill down on any line by simply pressing
/// Enter"</i> and <i>"Enter – To drill-down on a selected line in a report"</i>
/// (<c>help.tallysolutions.com/working-with-reports/</c>, by content 2026-10-13).</para>
///
/// <para><b>11.10.</b> The Cost Centre Monthly Summary, which the census recorded as absent "because that
/// primitive does not exist (T1-32)". Vendor: <i>"You can view the monthly breakup of income and expense
/// details along with the closing balance for the selected Cost Centre"</i>
/// (<c>help.tallysolutions.com/tally-prime/financial-controlling/cost-centre-or-profit-centre-tally/</c>).</para>
///
/// <para>🔴 <b>EVERY Enter HERE IS THE REAL KEY ON A SHOWN <see cref="MainWindow"/>.</b> Calling
/// <c>Reports.Drill(row)</c> directly would be green on a tree where the window never routes Enter to the
/// drill at all, which is exactly the hole this file exists to close — and it is the hole a shipped comment
/// claimed closed for the Job Work registers while Enter did nothing.</para>
/// </summary>
public sealed class ReportBatchW46Tests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ReportBatchW46Tests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexW46Batch_" + Guid.NewGuid().ToString("N"));
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

    // ---------------------------------------------------------------- scaffolding

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    private MainWindowViewModel NewCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name + " " + Guid.NewGuid().ToString("N")[..6];
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    private static DomainLedger Ledger(Company c, string name, string groupName, bool openingIsDebit = true)
    {
        if (c.FindLedgerByName(name) is { } existing) return existing;
        var created = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id,
            Money.Zero, openingIsDebit);
        c.AddLedger(created);
        return created;
    }

    /// <summary>Posts one balanced MEMORANDUM and one balanced REVERSING JOURNAL, each on its own day, and
    /// returns them. Both are provisional: neither touches the real books.</summary>
    private static (Voucher Memo, Voucher Reversing) PostProvisionalPair(Company c)
    {
        var svc = new LedgerService(c);
        var dr = Ledger(c, "Suspense Dr", "Cash-in-Hand");
        var cr = Ledger(c, "Suspense Cr", "Cash-in-Hand", openingIsDebit: false);
        var day = c.BooksBeginFrom;

        var memo = svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Memorandum")!.Id, day, new[]
        {
            new EntryLine(dr.Id, Money.FromRupees(4_500m), DrCr.Debit),
            new EntryLine(cr.Id, Money.FromRupees(4_500m), DrCr.Credit),
        }, narration: "Advance to be regularised"));

        var reversing = svc.Post(new Voucher(
            Guid.NewGuid(), c.FindVoucherTypeByName("Reversing Journal")!.Id, day.AddDays(1), new[]
            {
                new EntryLine(dr.Id, Money.FromRupees(7_250m), DrCr.Debit),
                new EntryLine(cr.Id, Money.FromRupees(7_250m), DrCr.Credit),
            }, narration: "Accrual, reverses after month end", applicableUpto: day.AddDays(20)));

        return (memo, reversing);
    }

    /// <summary>A company with a cost category, two centres, and both an expense and an income allocated to the
    /// first centre — so the monthly summary has a Dr figure, a Cr figure and a non-trivial closing.</summary>
    private MainWindowViewModel CostCentreFixture(
        string name, out Company company, out CostCentre head, out CostCentre branch, out DateOnly month)
    {
        var vm = NewCompany(name);
        var c = vm.Company!;
        var svc = new LedgerService(c);
        var journal = c.FindVoucherTypeByName("Journal")!;
        var cash = Ledger(c, "Cash", "Cash-in-Hand");
        var rent = Ledger(c, "Rent", "Indirect Expenses");
        var commission = Ledger(c, "Commission Received", "Indirect Incomes", openingIsDebit: false);

        var category = c.FindCostCategoryByName("Primary Cost Category")!;
        head = new CostCentre(Guid.NewGuid(), "Head Office", category.Id);
        branch = new CostCentre(Guid.NewGuid(), "Branch Office", category.Id);
        c.AddCostCentre(head);
        c.AddCostCentre(branch);

        month = c.FinancialYearStart.AddMonths(1);

        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, month, new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(5_000m), DrCr.Debit, billAllocations: null,
                costAllocations: new[] { new CostAllocation(category.Id, head.Id, Money.FromRupees(5_000m)) }),
            new EntryLine(cash.Id, Money.FromRupees(5_000m), DrCr.Credit),
        }));

        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, month.AddDays(2), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(1_800m), DrCr.Debit),
            new EntryLine(commission.Id, Money.FromRupees(1_800m), DrCr.Credit, billAllocations: null,
                costAllocations: new[] { new CostAllocation(category.Id, head.Id, Money.FromRupees(1_800m)) }),
        }));

        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, month, new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(900m), DrCr.Debit, billAllocations: null,
                costAllocations: new[] { new CostAllocation(category.Id, branch.Id, Money.FromRupees(900m)) }),
            new EntryLine(cash.Id, Money.FromRupees(900m), DrCr.Credit),
        }));

        _storage.Save(c);
        company = c;
        return vm;
    }

    // ================================================================ 11.13 — the two register drills

    /// <summary>
    /// 🔴 <b>THE CENTRAL 11.13 TEST.</b> On BOTH provisional registers, the real Enter on the highlighted row
    /// must open the voucher that row NAMES — not the current highlight, not nothing.
    ///
    /// <para>Fails on main: <c>Drill</c> has no arm for either kind, so Enter leaves the report where it is and
    /// <c>vm.VoucherDetail</c> stays null.</para>
    /// </summary>
    [AvaloniaTheory]
    [InlineData(ReportKind.MemorandumRegister)]
    [InlineData(ReportKind.ReversingJournalRegister)]
    public void Enter_on_a_provisional_register_row_opens_the_voucher_that_row_names(ReportKind kind)
    {
        var vm = NewCompany("Provisional Drill");
        var c = vm.Company!;
        var (memo, reversing) = PostProvisionalPair(c);
        var expected = kind == ReportKind.MemorandumRegister ? memo : reversing;

        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        vm.OpenReport(kind);
        Pump(window);
        Assert.Equal(Screen.Report, vm.CurrentScreen);

        // 🔴 The id must be on the ROW, not merely on the engine record. A register whose rows all carry
        // Guid.Empty cannot address anything, whatever the Drill switch says.
        var row = Assert.Single(vm.Reports!.Rows.Where(r => r.DrillVoucherId != Guid.Empty));
        Assert.Equal(expected.Id, row.DrillVoucherId);
        Assert.True(row.IsDrillable);

        vm.Reports!.SelectedRow = row;
        Pump(window);
        var before = vm.Columns.Count;

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);   // the real drill key
        Pump(window);

        Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen);
        Assert.NotNull(vm.VoucherDetail);
        Assert.Equal(expected.Id, vm.VoucherDetail!.VoucherId);
        Assert.Equal(before + 1, vm.Columns.Count);                          // added, not replaced
        Assert.Contains(vm.VoucherDetail!.Rows, r => r.Particulars == "Suspense Dr");

        window.Close();
    }

    /// <summary>
    /// The drill must not have been bought by turning either register into a book entry. A memorandum and a
    /// reversing journal are PROVISIONAL (<c>LedgerBalances.IsProvisionalBaseType</c>): drilling into one and
    /// coming back must leave the Trial Balance exactly where it was, or the "fix" has changed the books.
    /// </summary>
    [AvaloniaFact]
    public void Drilling_a_provisional_register_leaves_the_books_untouched()
    {
        var vm = NewCompany("Provisional Books");
        var c = vm.Company!;
        PostProvisionalPair(c);

        var before = TrialBalance.Build(c, new ReportOptions());
        Assert.Equal(before.TotalDebit, before.TotalCredit);

        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        foreach (var kind in new[] { ReportKind.MemorandumRegister, ReportKind.ReversingJournalRegister })
        {
            vm.OpenReport(kind);
            Pump(window);
            vm.Reports!.SelectedRow = vm.Reports!.Rows.First(r => r.DrillVoucherId != Guid.Empty);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen);
            vm.Back();
            Pump(window);
            vm.Back();
            Pump(window);
        }

        var after = TrialBalance.Build(c, new ReportOptions());
        Assert.Equal(before.TotalDebit, after.TotalDebit);
        Assert.Equal(before.TotalCredit, after.TotalCredit);
        // And neither provisional voucher leaked into the suspense ledger's own closing.
        var dr = c.FindLedgerByName("Suspense Dr")!;
        Assert.Equal(Money.Zero, LedgerBalances.Closing(c, dr, c.BooksBeginFrom.AddYears(1)).Amount);

        window.Close();
    }

    /// <summary>
    /// Enter on a non-drillable row of either register — the footing Total — must stay a safe no-op. The new
    /// arms resolve on <c>DrillVoucherId != Guid.Empty</c>, and a Total carries none; an arm that fired anyway
    /// would open "the voucher Guid.Empty".
    /// </summary>
    [AvaloniaFact]
    public void Enter_on_a_provisional_register_total_row_is_a_safe_no_op()
    {
        var vm = NewCompany("Provisional NoOp");
        PostProvisionalPair(vm.Company!);

        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        vm.OpenReport(ReportKind.MemorandumRegister);
        Pump(window);
        var total = Assert.Single(vm.Reports!.Rows.Where(r => r.IsTotal));
        Assert.False(total.IsDrillable);
        vm.Reports!.SelectedRow = total;
        Pump(window);
        var before = vm.Columns.Count;

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump(window);

        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.Null(vm.VoucherDetail);
        Assert.Equal(before, vm.Columns.Count);

        window.Close();
    }

    // ================================================================ 11.10 — the Cost Centre Monthly Summary

    /// <summary>
    /// 🔴 <b>REACH FIRST.</b> The report must be openable from its own MENU ROW under Reports → Statements of
    /// Accounts → Cost Centres, by name, like the three cost reports beside it. A kind reachable only by
    /// <c>OpenReport</c> from a test is not a capability.
    /// </summary>
    [Fact]
    public void The_cost_centre_monthly_summary_has_a_menu_row_under_cost_centres()
    {
        var vm = CostCentreFixture("CC Menu", out _, out _, out _, out _);

        vm.ShowGateway();
        ArrowToAndDrill(vm, "Statements of Accounts");
        ArrowToAndDrill(vm, "Cost Centres");

        // The row exists beside its three siblings …
        var labels = vm.Columns[^1].Items.Select(i => i.Label).ToList();
        Assert.Contains("Category Summary", labels);
        Assert.Contains("Cost Centre Break-up", labels);
        Assert.Contains("Ledger Break-up", labels);
        Assert.Contains("Cost Centre Monthly Summary", labels);

        // … and activating it opens the report, not a dead row.
        ArrowToAndDrill(vm, "Cost Centre Monthly Summary");
        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.Equal(ReportKind.CostCentreMonthlySummary, vm.Reports!.Kind);
    }

    /// <summary>
    /// The figures. Month rows, income and expense in the two columns, the closing carried down, and the
    /// heading naming the centre — all read off a book posted through <see cref="LedgerService"/>.
    /// </summary>
    [Fact]
    public void The_cost_centre_monthly_summary_renders_the_picked_centres_months_and_closing()
    {
        var vm = CostCentreFixture("CC Figures", out var c, out var head, out var branch, out var month);

        vm.OpenReport(ReportKind.CostCentreMonthlySummary);
        var reports = vm.Reports!;
        reports.SetPeriod(c.FinancialYearStart, month.AddMonths(1).AddDays(-1));

        // The picker is live and defaults to a real centre — the FIRST in (category, centre) name order, which
        // is "Branch Office" on this fixture, NOT the first one posted. The default is asserted for what it is
        // rather than for what a reader might expect; pinning "Head Office" here would pin the wrong answer.
        Assert.True(reports.ShowCostCentrePicker);
        Assert.NotNull(reports.SelectedCostCentre);
        Assert.Contains("Branch Office", reports.SelectedCostCentre!.Display);
        Assert.Equal(2, reports.CostCentreOptions.Count);

        // Now pick Head Office explicitly — the centre that has BOTH an expense and an income on it.
        reports.SelectedCostCentre =
            reports.CostCentreOptions.Single(o => o.Display.StartsWith("Head Office", StringComparison.Ordinal));
        Assert.Equal(head.Id, reports.SelectedCostCentre!.CentreId);
        Assert.Contains("Head Office", reports.SelectedCostCentre!.Display);
        Assert.Equal("Cost Centre Monthly Summary — Head Office", reports.Title);
        // 🔴 RULING 18: the heading carries a master name the user typed, so it must be flagged for verbatim
        // emission. Without the flag a centre legitimately named after a vendor would be scrubbed on paper.
        Assert.True(reports.TitleCarriesMasterName);

        var rows = reports.Rows;
        Assert.Contains(rows, r => r.Particulars == "Opening Balance" && r.IsHeader);
        var monthRow = Assert.Single(rows.Where(r => r.Particulars == MonthLabel(month)));
        Assert.Contains("5,000.00", monthRow.Debit);
        Assert.Contains("1,800.00", monthRow.Credit);
        Assert.Contains("closing", monthRow.Secondary);
        Assert.Contains("3,200.00", monthRow.Secondary);                 // 5,000 Dr − 1,800 Cr
        Assert.Contains("Dr", monthRow.Secondary);

        var closing = Assert.Single(rows.Where(r => r.IsTotal));
        Assert.Equal("Closing Balance", closing.Particulars);
        Assert.Contains("3,200.00", closing.Secondary);

        // 🔴 The picker is the scope, and it must actually re-project: Branch Office spent 900 and earned
        // nothing, so switching to it must change every figure above.
        reports.SelectedCostCentre =
            reports.CostCentreOptions.Single(o => o.Display.StartsWith("Branch Office", StringComparison.Ordinal));
        Assert.Equal("Cost Centre Monthly Summary — Branch Office", reports.Title);
        var branchMonth = Assert.Single(reports.Rows.Where(r => r.Particulars == MonthLabel(month)));
        Assert.Contains("900.00", branchMonth.Debit);
        Assert.Equal(string.Empty, branchMonth.Credit);
        Assert.Contains("900.00", Assert.Single(reports.Rows.Where(r => r.IsTotal)).Secondary);
        Assert.Equal(branch.Id, reports.SelectedCostCentre!.CentreId);
        Assert.NotEqual(head.Id, reports.SelectedCostCentre!.CentreId);
    }

    /// <summary>
    /// 🔴 <b>THE EGRESS CONTRACT.</b> Being on the shared report surface is the whole point of the re-home: the
    /// new kind must PRINT and EXPORT with populated column captions and its own figures, not as a blank band.
    /// Three reports once printed pages of blank cells, which is why this is asserted on the artefact.
    /// </summary>
    [Fact]
    public void The_cost_centre_monthly_summary_prints_and_exports_with_captions_and_figures()
    {
        var vm = CostCentreFixture("CC Egress", out var c, out _, out _, out var month);

        vm.OpenReport(ReportKind.CostCentreMonthlySummary);
        var reports = vm.Reports!;
        reports.SetPeriod(c.FinancialYearStart, month.AddMonths(1).AddDays(-1));
        reports.SelectedCostCentre =
            reports.CostCentreOptions.Single(o => o.Display.StartsWith("Head Office", StringComparison.Ordinal));

        var print = ReportPrintProjector.Project(reports);
        Assert.NotEmpty(print.Columns);
        Assert.All(print.Columns, col => Assert.False(string.IsNullOrWhiteSpace(col.Header)));
        Assert.Contains("Head Office", print.Title);
        Assert.True(print.TitleCarriesMasterName);
        Assert.Contains(print.Rows, r => r.Cells.Any(cell => cell.Contains("5,000.00", StringComparison.Ordinal)));

        var export = ReportTabularProjector.Project(reports);
        Assert.NotEmpty(export.Columns);
        Assert.All(export.Columns, col => Assert.False(string.IsNullOrWhiteSpace(col.Header)));
        Assert.Contains("Head Office", TabularExport.TitleText(export));
        Assert.Contains(export.Rows, r => r.Cells.Any(cell => cell.HasNumber && cell.NumberValue == 5000m));
    }

    /// <summary>
    /// 🔴 <b>THE SAVED-VIEW DOOR.</b> A saved view stores a kind token and re-opens that kind directly, walking
    /// past the menu row's F11 condition — which is how a company with Payroll OFF once reached the Pay Sheet.
    /// The new kind carries <c>ReportFeatureGate.CostCentres</c>, so switching the feature off must shut BOTH
    /// doors, and switching it back on must reopen them (a gate, not a wall).
    /// </summary>
    [Fact]
    public void A_saved_view_cannot_reach_the_cost_centre_monthly_summary_with_cost_centres_off()
    {
        var vm = CostCentreFixture("CC Gate", out var c, out _, out _, out _);

        vm.OpenReport(ReportKind.CostCentreMonthlySummary);
        var savedView = vm.Reports!.ToSavedView();
        vm.Back();

        c.EnableCostCentres = false;

        // The menu group goes …
        vm.ShowGateway();
        ArrowToAndDrill(vm, "Statements of Accounts");
        Assert.DoesNotContain(vm.Columns[^1].Items, i => i.Label == "Cost Centres");

        // … and so does the saved view.
        vm.ApplySavedView(savedView);
        Assert.NotEqual(Screen.Report, vm.CurrentScreen);
        Assert.Contains("switched off", vm.Message);

        // A gate, not a wall.
        c.EnableCostCentres = true;
        vm.ApplySavedView(savedView);
        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.Equal(ReportKind.CostCentreMonthlySummary, vm.Reports!.Kind);
    }

    /// <summary>
    /// With cost centres switched off the projection itself must refuse OUT LOUD on this grid. The shipped
    /// <c>CostCentresOff()</c> helper drives the wide MATRIX's empty state, which renders nothing on the Dr/Cr
    /// grid — so a report that reused it would come out silently blank with no explanation, which is the
    /// blank-page defect class this project has already shipped three times.
    /// </summary>
    [Fact]
    public void With_cost_centres_off_the_monthly_summary_says_so_on_its_own_rows()
    {
        var vm = CostCentreFixture("CC Off", out var c, out _, out _, out _);
        c.EnableCostCentres = false;

        var reports = new ReportsViewModel(c, ReportKind.CostCentreMonthlySummary);

        Assert.NotEmpty(reports.Rows);
        Assert.Contains(reports.Rows, r => r.Particulars.Contains("Cost Centres are not enabled", StringComparison.Ordinal));
        // No figure leaked onto the screen beside the refusal.
        Assert.DoesNotContain(reports.Rows, r => r.Debit.Contains("5,000", StringComparison.Ordinal));
    }

    /// <summary>
    /// The vendor's next level is a <i>Cost Centre Vouchers</i> report this product does not have. Enter on a
    /// month row must therefore be a no-op rather than drilling somewhere the vendor does not send the operator.
    /// This pins the DIVERGENCE so a later pass cannot quietly point it at the Day Book and call it fidelity.
    /// </summary>
    [AvaloniaFact]
    public void A_month_row_of_the_cost_centre_monthly_summary_carries_no_drill_target()
    {
        var vm = CostCentreFixture("CC Month Drill", out var c, out _, out _, out var month);

        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        vm.OpenReport(ReportKind.CostCentreMonthlySummary);
        Pump(window);
        vm.Reports!.SetPeriod(c.FinancialYearStart, month.AddMonths(1).AddDays(-1));
        Pump(window);

        var monthRow = Assert.Single(vm.Reports!.Rows.Where(r => r.Particulars == MonthLabel(month)));
        Assert.Equal(Guid.Empty, monthRow.DrillVoucherId);
        Assert.Equal(Guid.Empty, monthRow.DrillLedgerId);
        Assert.Null(monthRow.DrillPeriod);

        vm.Reports!.SelectedRow = monthRow;
        Pump(window);
        var before = vm.Columns.Count;
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump(window);

        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.Equal(before, vm.Columns.Count);
        Assert.Null(vm.VoucherDetail);
        Assert.Null(vm.LedgerVouchers);

        window.Close();
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The culture-free month label the shared <c>MonthAxis</c> renders, e.g. <c>May-2024</c>. Read
    /// from the axis itself rather than formatted here, so the test cannot pass against a label the report does
    /// not actually print.</summary>
    private static string MonthLabel(DateOnly d) =>
        MonthAxis.Months(new DateOnly(d.Year, d.Month, 1), new DateOnly(d.Year, d.Month, 28))[0].Label;

    /// <summary>Arrows the cascade highlight down onto the row with this label and drills into it — the keyboard
    /// route, not a direct call, because the menu row is itself the capability under test. Same shape as
    /// <c>RehomedReportSurfaceTests.ArrowToAndDrill</c>.</summary>
    private static void ArrowToAndDrill(MainWindowViewModel vm, string label)
    {
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            if (vm.Menu[vm.SelectedIndex].Label == label) { vm.DrillIn(); return; }
            vm.MoveDown();
        }

        Assert.Fail($"'{label}' was not reachable by arrow navigation in the active column.");
    }
}
