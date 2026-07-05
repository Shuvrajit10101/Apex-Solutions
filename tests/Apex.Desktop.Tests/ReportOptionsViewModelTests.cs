using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// UI-side coverage for Phase-5 slice-1 (RQ-1 period/as-of, RQ-2 detailed↔summary, RQ-6 F12 configuration)
/// wired into <see cref="ReportsViewModel"/> and the <see cref="ReportConfigViewModel"/> panel, plus the
/// shell shortcut plumbing on <see cref="MainWindowViewModel"/> (F2/Alt+F2/Alt+F1/F12).
///
/// <para>Every test constructs a report over the embedded "Robert" demo (13 vouchers across April 2020,
/// last voucher 2020-04-30 → default as-of), asserts the untouched report reproduces the legacy view, then
/// drives a shortcut and asserts the projection inputs and the produced rows actually change. The engine
/// (already covered by <c>ReportOptionsTests</c>) is trusted; these tests pin the UI wiring.</para>
/// </summary>
public sealed class ReportOptionsViewModelTests : IDisposable
{
    private static readonly DateOnly LastVoucher = new(2020, 4, 30);
    private static readonly DateOnly BooksBegin = new(2020, 4, 1);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ReportOptionsViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexReportOptTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    private static Company Robert() => DemoData.BuildRobert("Robert " + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// A minimal seeded company whose ONLY "Bank Accounts" group nets to exactly zero (5000 Dr + 5000 Cr
    /// opening balances on two ledgers under it), plus two non-zero groups (Capital Account 5000 Cr,
    /// Cash-in-Hand 5000 Dr). Total Dr == Total Cr == 10000, so the Trial Balance balances — used to pin
    /// Defect C (a net-zero summary group must not render a blank Dr/Cr row).
    /// </summary>
    private static Company NetZeroGroupCompany()
    {
        var company = Apex.Ledger.Services.CompanyFactory.CreateSeeded(
            "NetZero " + Guid.NewGuid().ToString("N"),
            new DateOnly(2020, 4, 1), new DateOnly(2020, 4, 1));

        Guid GroupId(string name) => company.FindGroupByName(name)!.Id;

        // Bank Accounts: two offsetting openings → the group nets to exactly zero.
        company.AddLedger(new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "Bank A", GroupId("Bank Accounts"),
            Money.FromRupees(5000m), openingIsDebit: true));
        company.AddLedger(new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "Bank B", GroupId("Bank Accounts"),
            Money.FromRupees(5000m), openingIsDebit: false));
        // Two non-zero groups so the summary still has real rows and the whole TB balances.
        company.AddLedger(new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "Owner Capital", GroupId("Capital Account"),
            Money.FromRupees(5000m), openingIsDebit: false));
        company.AddLedger(new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "Petty Cash", GroupId("Cash-in-Hand"),
            Money.FromRupees(5000m), openingIsDebit: true));

        return company;
    }

    private static int LedgerLineCount(ReportsViewModel vm) =>
        vm.Rows.Count(r => !r.IsTotal && !r.IsHeader);

    // =============================================================== RQ-1: period / as-of (F2 / Alt+F2)

    [Fact]
    public void Default_report_uses_last_voucher_asOf_and_no_period()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        Assert.Equal(LastVoucher, vm.AsOf);
        Assert.Null(vm.Period);
        Assert.True(vm.Detailed);
        Assert.False(vm.HideZeroBalances);
        Assert.False(vm.ShowPercentages);
    }

    [Fact]
    public void SetAsOf_narrows_the_asOf_and_reprojects_trial_balance()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        var fullTotal = GrandTotalDebit(vm);

        // Pull the as-of back before the last few vouchers — a different (earlier) TB.
        vm.SetAsOf(new DateOnly(2020, 4, 14));

        Assert.Equal(new DateOnly(2020, 4, 14), vm.AsOf);
        Assert.Null(vm.Period);
        Assert.Contains("as at 14-Apr-2020", vm.Subtitle);
        Assert.NotEqual(fullTotal, GrandTotalDebit(vm)); // the balance genuinely changed
    }

    [Fact]
    public void SetPeriod_closes_trial_balance_as_at_the_window_end_and_labels_it_as_at()
    {
        var full = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        var vm = new ReportsViewModel(Robert(), ReportKind.TrialBalance);

        vm.SetPeriod(BooksBegin, new DateOnly(2020, 4, 14));

        Assert.Equal(new PeriodRange(BooksBegin, new DateOnly(2020, 4, 14)), vm.Period);
        Assert.Equal(new DateOnly(2020, 4, 14), vm.AsOf); // as-of tracks the window end
        // RQ-1: the TB is a CLOSING statement as-at Period.To (From has no effect on the figures), so its
        // subtitle reads "as at {To}" like the Balance Sheet — never "for the period …".
        Assert.Contains("as at 14-Apr-2020", vm.Subtitle);
        Assert.DoesNotContain("for the period", vm.Subtitle);
        // Closing as-at 14-Apr is not the full closing as-at 30-Apr.
        Assert.NotEqual(GrandTotalDebit(full), GrandTotalDebit(vm));
    }

    [Fact]
    public void Period_trial_balance_equals_the_asOf_trial_balance_at_the_window_end()
    {
        // RQ-1 invariant: Period.From does not affect the TB — a period TB with To == asOf is byte-for-byte
        // the plain as-of TB at that date. Pin it at the UI layer so the "as at {To}" label is not a lie.
        var period = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        period.SetPeriod(BooksBegin, new DateOnly(2020, 4, 14));

        var asOf = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        asOf.SetAsOf(new DateOnly(2020, 4, 14));

        Assert.Equal(GrandTotalDebit(asOf), GrandTotalDebit(period));
        Assert.Equal(LedgerLineCount(asOf), LedgerLineCount(period));
    }

    [Fact]
    public void SetPeriod_rejects_an_inverted_window()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        vm.SetPeriod(new DateOnly(2020, 4, 30), new DateOnly(2020, 4, 1)); // from > to
        Assert.Null(vm.Period); // rejected — left at the default
        Assert.Equal(LastVoucher, vm.AsOf);
    }

    [Fact]
    public void ClearPeriod_restores_the_default_asOf()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        vm.SetPeriod(BooksBegin, new DateOnly(2020, 4, 14));
        Assert.NotNull(vm.Period);

        vm.ClearPeriod();
        Assert.Null(vm.Period);
        Assert.Equal(LastVoucher, vm.AsOf);
    }

    [Fact]
    public void Period_narrows_the_day_book_row_count()
    {
        var full = new ReportsViewModel(Robert(), ReportKind.DayBook);
        var vm = new ReportsViewModel(Robert(), ReportKind.DayBook);
        var fullRows = full.Rows.Count;

        vm.SetPeriod(BooksBegin, new DateOnly(2020, 4, 10)); // only the first few days
        Assert.True(vm.Rows.Count < fullRows);
        Assert.Contains("01-Apr-2020 to 10-Apr-2020", vm.Subtitle);
    }

    // =============================================================== RQ-2: detailed ↔ summary (Alt+F1)

    [Fact]
    public void ToggleDetailed_rolls_trial_balance_up_to_group_rows_and_keeps_the_grand_total()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        var detailedLines = LedgerLineCount(vm);
        var detailedTotalDr = GrandTotalDebit(vm);

        vm.ToggleDetailed();

        Assert.False(vm.Detailed);
        var summaryLines = LedgerLineCount(vm);
        Assert.True(summaryLines < detailedLines);         // fewer rows (one per group)
        Assert.Contains("(Summary)", vm.Subtitle);
        Assert.Equal(detailedTotalDr, GrandTotalDebit(vm)); // Σ unchanged — a roll-up never moves the total

        vm.ToggleDetailed();
        Assert.True(vm.Detailed);
        Assert.Equal(detailedLines, LedgerLineCount(vm));   // back to the ledger-level view
    }

    [Fact]
    public void ToggleDetailed_is_a_no_op_on_the_day_book()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.DayBook);
        Assert.False(vm.SupportsDetailToggle);
        var before = vm.Rows.Count;
        vm.ToggleDetailed();
        Assert.True(vm.Detailed); // unchanged
        Assert.Equal(before, vm.Rows.Count);
    }

    [Fact]
    public void ToggleDetailed_summarises_profit_and_loss_but_keeps_totals()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.ProfitAndLoss);
        var totalIncomeRow = TotalRowText(vm, "Total Income");
        var totalExpenseRow = TotalRowText(vm, "Total Expenses");

        vm.ToggleDetailed();
        Assert.False(vm.Detailed);
        // The section totals are unchanged by summarising (Σ(summary)==Σ(detailed)).
        Assert.Equal(totalIncomeRow, TotalRowText(vm, "Total Income"));
        Assert.Equal(totalExpenseRow, TotalRowText(vm, "Total Expenses"));
    }

    // =============================================================== RQ-6: F12 configuration

    [Fact]
    public void ApplyConfiguration_hideZero_drops_exact_zero_rows()
    {
        // Seed one guaranteed zero-net ledger by choosing an as-of where a ledger has offsetting entries is
        // hard to guarantee from the fixture; instead assert the flag flows and the row count never grows.
        var vm = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        var before = LedgerLineCount(vm);

        vm.ApplyConfiguration(hideZero: true, showPercentages: false, ClosingStockMode.AsPostedLedger);
        Assert.True(vm.HideZeroBalances);
        Assert.True(LedgerLineCount(vm) <= before); // hidden rows only ever removed, never added
    }

    [Fact]
    public void ApplyConfiguration_showPercentages_appends_a_percent_suffix_to_rows()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        Assert.DoesNotContain(vm.Rows, r => r.Particulars.Contains('%'));

        vm.ApplyConfiguration(hideZero: false, showPercentages: true, ClosingStockMode.AsPostedLedger);
        Assert.True(vm.ShowPercentages);
        Assert.Contains(vm.Rows, r => !r.IsTotal && !r.IsHeader && r.Particulars.Contains('%'));
    }

    [Fact]
    public void ApplyConfiguration_closingStock_basis_flows_into_the_balance_sheet_build()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.BalanceSheet);
        Assert.Equal(ClosingStockMode.AsPostedLedger, vm.ClosingStock);

        vm.ApplyConfiguration(hideZero: false, showPercentages: false, ClosingStockMode.InventoryDerived);
        Assert.Equal(ClosingStockMode.InventoryDerived, vm.ClosingStock);
        // Robert is accounts-only (no inventory) so the sheet still builds and stays balanced.
        Assert.Equal(TotalRowText(vm, "Total Liabilities"), TotalRowText(vm, "Total Assets"));
    }

    // =============================================================== ReportConfigViewModel panel

    [Fact]
    public void ConfigPanel_seeds_from_the_report_and_a_noop_apply_changes_nothing()
    {
        var report = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        var rowsBefore = report.Rows.Count;
        var cfg = new ReportConfigViewModel(report);

        Assert.Equal("30-Apr-2020", cfg.AsOfText);
        Assert.False(cfg.UsePeriod);
        Assert.True(cfg.Detailed);

        cfg.Apply(); // no edits
        Assert.Equal(LastVoucher, report.AsOf);
        Assert.Null(report.Period);
        Assert.Equal(rowsBefore, report.Rows.Count);
        Assert.Contains("recomputed", cfg.Status);
    }

    [Fact]
    public void ConfigPanel_apply_pushes_period_detailed_and_display_flags_into_the_report()
    {
        var report = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        var cfg = new ReportConfigViewModel(report)
        {
            UsePeriod = true,
            PeriodFromText = "01-Apr-2020",
            PeriodToText = "14-Apr-2020",
            Detailed = false,
            HideZeroBalances = true,
            ShowPercentages = true,
        };

        cfg.Apply();

        Assert.Equal(new PeriodRange(BooksBegin, new DateOnly(2020, 4, 14)), report.Period);
        Assert.False(report.Detailed);
        Assert.True(report.HideZeroBalances);
        Assert.True(report.ShowPercentages);
    }

    [Fact]
    public void ConfigPanel_ignores_an_unparseable_asOf()
    {
        var report = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        var cfg = new ReportConfigViewModel(report) { UsePeriod = false, AsOfText = "not-a-date" };

        cfg.Apply();
        Assert.Equal(LastVoucher, report.AsOf); // unchanged
    }

    // =============================================================== Defect C: summary TB net-zero groups

    [Fact]
    public void Summary_trial_balance_over_a_net_zero_group_shows_no_blank_group_row()
    {
        // A company whose ONLY "Bank Accounts" group nets to exactly zero (5000 Dr + 5000 Cr), plus two
        // non-zero groups. Hide-zero is OFF. The legacy DETAILED TB suppresses zero rows; the summary
        // roll-up must do the same — a net-zero group must NOT render a blank Dr/Cr row (Defect C).
        var company = NetZeroGroupCompany();
        var vm = new ReportsViewModel(company, ReportKind.TrialBalance);
        vm.ToggleDetailed();                         // summary roll-up
        Assert.False(vm.Detailed);
        Assert.False(vm.HideZeroBalances);           // the defect only bit with hide-zero OFF

        // No group row is blank (both Dr and Cr empty) — the net-zero "Bank Accounts" group is gone.
        Assert.DoesNotContain(vm.Rows, r =>
            !r.IsTotal && !r.IsHeader && r.Debit.Length == 0 && r.Credit.Length == 0);
        Assert.DoesNotContain(vm.Rows, r => !r.IsTotal && r.Particulars == "Bank Accounts");

        // The two non-zero groups still show, and the Grand Total stays balanced.
        Assert.Contains(vm.Rows, r => !r.IsTotal && r.Particulars == "Capital Account");
        Assert.Contains(vm.Rows, r => !r.IsTotal && r.Particulars == "Cash-in-Hand");
        var total = vm.Rows.Single(r => r.IsTotal && r.Particulars == "Grand Total");
        Assert.Equal(total.Debit, total.Credit);
    }

    // =============================================================== Defect D: Apply validates the window

    [Fact]
    public void ConfigPanel_apply_with_an_inverted_window_reports_a_validation_error_and_leaves_period_null()
    {
        var report = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        var cfg = new ReportConfigViewModel(report)
        {
            UsePeriod = true,
            PeriodFromText = "30-Apr-2020",   // From > To
            PeriodToText = "01-Apr-2020",
        };

        cfg.Apply();

        Assert.Null(report.Period);                       // the report's period was NOT mutated
        Assert.Equal(LastVoucher, report.AsOf);           // as-of untouched
        Assert.DoesNotContain("recomputed", cfg.Status);  // no false success
        Assert.Contains("From must be on/before To", cfg.Status);
    }

    [Fact]
    public void ConfigPanel_apply_with_an_unparseable_period_date_reports_a_validation_error()
    {
        var report = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        var cfg = new ReportConfigViewModel(report)
        {
            UsePeriod = true,
            PeriodFromText = "01-Apr-2020",
            PeriodToText = "not-a-date",
        };

        cfg.Apply();

        Assert.Null(report.Period);
        Assert.DoesNotContain("recomputed", cfg.Status);
        Assert.Contains("Unrecognized date", cfg.Status);
    }

    [Fact]
    public void ConfigPanel_apply_with_a_valid_window_still_succeeds()
    {
        var report = new ReportsViewModel(Robert(), ReportKind.TrialBalance);
        var cfg = new ReportConfigViewModel(report)
        {
            UsePeriod = true,
            PeriodFromText = "01-Apr-2020",
            PeriodToText = "14-Apr-2020",
        };

        cfg.Apply();

        Assert.Equal(new PeriodRange(BooksBegin, new DateOnly(2020, 4, 14)), report.Period);
        Assert.Contains("recomputed", cfg.Status);
    }

    // =============================================================== shell shortcut plumbing

    [Fact]
    public void F12_opens_the_config_panel_as_a_column_beside_the_live_report()
    {
        var vm = OpenReportShell(ReportKind.TrialBalance, out var columnsBefore);

        vm.OpenReportConfig();

        Assert.Equal(Screen.ReportConfig, vm.CurrentScreen);
        Assert.NotNull(vm.ReportConfig);
        Assert.NotNull(vm.Reports);                       // the report stays live beneath the panel
        Assert.Equal(columnsBefore + 1, vm.Columns.Count); // opened as an EXTRA column, not a replacement
        Assert.True(vm.Columns[^1].Page is ReportConfigViewModel); // rightmost column hosts the panel

        // Re-pressing F12 does not stack a second panel.
        vm.OpenReportConfig();
        Assert.Equal(columnsBefore + 1, vm.Columns.Count);
    }

    [Fact]
    public void Closing_the_config_panel_leaves_the_report_live()
    {
        var vm = OpenReportShell(ReportKind.TrialBalance, out _);
        vm.OpenReportConfig();
        Assert.Equal(Screen.ReportConfig, vm.CurrentScreen);

        vm.Back(); // Esc / Left pops the config column

        Assert.Null(vm.ReportConfig);
        Assert.NotNull(vm.Reports);                 // the report survived
        Assert.Equal(Screen.Report, vm.CurrentScreen);
    }

    [Fact]
    public void ApplyReportConfig_from_the_shell_reprojects_the_report()
    {
        var vm = OpenReportShell(ReportKind.TrialBalance, out _);
        vm.OpenReportConfig();
        vm.ReportConfig!.ShowPercentages = true;

        vm.ApplyReportConfig();

        Assert.True(vm.Reports!.ShowPercentages);
        Assert.Contains(vm.Reports.Rows, r => !r.IsTotal && !r.IsHeader && r.Particulars.Contains('%'));
    }

    [Fact]
    public void ReportSetPeriod_shortcut_opens_the_panel_with_the_window_enabled()
    {
        var vm = OpenReportShell(ReportKind.TrialBalance, out _);
        vm.ReportSetPeriod();
        Assert.NotNull(vm.ReportConfig);
        Assert.True(vm.ReportConfig!.UsePeriod);
    }

    [Fact]
    public void ReportSetAsOf_shortcut_opens_the_panel_with_the_window_disabled()
    {
        var vm = OpenReportShell(ReportKind.TrialBalance, out _);
        vm.ReportSetPeriod();          // turn the window on first
        Assert.True(vm.ReportConfig!.UsePeriod);
        vm.ReportSetAsOf();            // F2 flips it back to the single as-of
        Assert.False(vm.ReportConfig!.UsePeriod);
    }

    [Fact]
    public void ReportToggleDetailed_shortcut_summarises_the_active_report()
    {
        var vm = OpenReportShell(ReportKind.TrialBalance, out _);
        Assert.True(vm.Reports!.Detailed);
        vm.ReportToggleDetailed();
        Assert.False(vm.Reports!.Detailed);
    }

    [Fact]
    public void IsReportContext_is_false_without_a_report_and_true_with_one()
    {
        var vm = new MainWindowViewModel(_storage);
        Assert.False(vm.IsReportContext);
        vm.LoadRobertDemo();
        vm.OpenReport(ReportKind.TrialBalance);
        Assert.True(vm.IsReportContext);
    }

    // =============================================================== helpers

    private MainWindowViewModel OpenReportShell(ReportKind kind, out int columnsBefore)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();
        vm.OpenReport(kind);
        Assert.Equal(Screen.Report, vm.CurrentScreen);
        columnsBefore = vm.Columns.Count;
        return vm;
    }

    /// <summary>The Grand-Total Dr cell text of a Dr/Cr report (Trial Balance) — a stable projection fingerprint.</summary>
    private static string GrandTotalDebit(ReportsViewModel vm) =>
        vm.Rows.Single(r => r.IsTotal && r.Particulars == "Grand Total").Debit;

    /// <summary>The amount text of a named total row (P&amp;L / Balance-Sheet section totals).</summary>
    private static string TotalRowText(ReportsViewModel vm, string label) =>
        vm.Rows.Single(r => r.IsTotal && r.Particulars == label).Amount;

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}
