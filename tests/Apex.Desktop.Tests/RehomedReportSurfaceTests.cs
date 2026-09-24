using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>W-V2 — THE RE-HOMED REPORTS</b> (census 11.9 Outstandings · 11.10 Cost Centre · 11.11 Budget Variance ·
/// 7.13 Gratuity Provision · 7.14 Statutory Bonus).
///
/// <para><b>What this file exists to stop, stated precisely.</b> Five reports shipped on bespoke page Screens.
/// Their arithmetic was never in doubt — every projection behind them has had passing unit tests in
/// <c>Apex.Ledger</c> for several phases. What they lacked was a SURFACE: a page Screen leaves the shell's
/// report context null, and that one fact switches off Ctrl+P print, Ctrl+E export, F2/Alt+F2 period,
/// F12 configuration, Alt+F12 sort/filter and Alt+K saved views simultaneously. Six gestures off per report.</para>
///
/// <para>🔴 <b>WHAT COMES BACK IS FOUR AND A HALF OF THOSE SIX, AND THIS FILE SAYS SO RATHER THAN ROUNDING
/// UP.</b> Ctrl+P, Ctrl+E, F2/Alt+F2 and Alt+K arrive in full; F12 arrives with its PERIOD half only, because
/// its three display knobs genuinely do not act on these kinds and are now hidden; Alt+F12 arrives saying it
/// cannot act. Hiding a knob that does nothing and refusing out loud are both the right behaviour — and both
/// make the honest count smaller. This branch has twice been withheld for a claim stronger than the code
/// behind it, so the count is written down where it can be checked.</para>
///
/// <para>🔴 <b>AND THE OBVIOUS FIX HAS A TRAP IN IT, WHICH IS WHY HALF THE TESTS BELOW ARE ABOUT EGRESS.</b>
/// Adding a <see cref="ReportKind"/> member is not re-homing. The per-kind export caption table covers a
/// minority of kinds and the print path had NO caption table for a non-accounting kind at all, so a report
/// re-homed onto a bespoke grid would have gained a BLANK export header row and a BLANK printed header band —
/// strictly worse than the dedicated Screen it replaced, and invisible to any test that only asserts the enum
/// member exists. These eight kinds render through the shared dynamic matrix instead, whose live column band
/// both egress projectors read directly. The tests that matter most here are therefore the ones that project
/// the report to CSV and to PDF and assert the captions came out populated.</para>
///
/// <para>Every route below is walked with the ARROW KEYS from the Gateway, because a capability no operator can
/// reach from the keyboard is not delivered — this repository has shipped that shape more than once and
/// <c>CostReports.BuildLedgerBreakup</c>, routed for the first time in this wave, is its own filed example.</para>
/// </summary>
public sealed class RehomedReportSurfaceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public RehomedReportSurfaceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexRehomed_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>The eight kinds this wave re-homed, in census order.</summary>
    public static IEnumerable<object[]> RehomedKinds() => new[]
    {
        new object[] { ReportKind.ReceivablesOutstanding },
        new object[] { ReportKind.PayablesOutstanding },
        new object[] { ReportKind.CostCategorySummary },
        new object[] { ReportKind.CostCentreBreakup },
        new object[] { ReportKind.CostCentreLedgerBreakup },
        new object[] { ReportKind.BudgetVariance },
        new object[] { ReportKind.GratuityProvisionRegister },
        new object[] { ReportKind.BonusRegister },
    };

    // ================================================================= the egress contract (the real work)

    /// <summary>
    /// 🔴 <b>THE CENTRAL TEST OF THIS WAVE.</b> Every re-homed kind must export WITH COLUMN CAPTIONS and print
    /// WITH COLUMN CAPTIONS. Both are asserted against the SAME report object, because the failure this guards
    /// is asymmetric: the export caption table and the print caption band are different code paths, and a kind
    /// can easily land in one and not the other.
    ///
    /// <para>The blank-caption failure is what makes this necessary rather than pedantic. A CSV whose header row
    /// is <c>,,,,</c> and a printed report whose header band is empty boxes are both things an operator sends to
    /// a bank, an auditor or a client. Asserting "the kind is in the enum" would pass on every one of them.</para>
    ///
    /// <para>Fails on today's main: none of these kinds exists there.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(RehomedKinds))]
    public void Every_rehomed_kind_exports_and_prints_with_populated_column_captions(ReportKind kind)
    {
        var vm = FullFixture($"Egress {kind}");
        var reports = new ReportsViewModel(vm.Company!, kind);

        // On screen.
        Assert.True(reports.IsWideMatrixReport,
            $"{kind} is not on the wide-matrix surface, so neither egress projector can see its columns.");
        Assert.True(reports.PayrollColumns.Count >= 2,
            $"{kind} built {reports.PayrollColumns.Count} column(s); a re-homed report needs a label column and "
            + "at least one figure column or there is nothing to caption.");
        Assert.All(reports.PayrollColumns, c => Assert.False(string.IsNullOrWhiteSpace(c.Header),
            $"{kind} has an unlabelled on-screen column."));

        // Export (CSV / XLSX / e-mail attachment all go through this one projection).
        var exported = ReportTabularProjector.Project(reports);
        Assert.Equal(reports.PayrollColumns.Count, exported.Columns.Count);
        Assert.All(exported.Columns, c => Assert.False(string.IsNullOrWhiteSpace(c.Header),
            $"{kind} exported a BLANK column caption. A re-homed report that lands with no export captions has "
            + "gained a defect the dedicated page Screen it replaced did not have."));

        // Print (print preview, physical print and Save-as-PDF all go through this one projection).
        var printed = ReportPrintProjector.Project(reports);
        Assert.Equal(reports.PayrollColumns.Count, printed.Columns.Count);
        Assert.All(printed.Columns, c => Assert.False(string.IsNullOrWhiteSpace(c.Header),
            $"{kind} printed a BLANK column caption band."));

        // 🔴 And the two egress paths must agree with the SCREEN, caption for caption. This is the property the
        // shared-matrix design buys and a per-kind caption table cannot: there is one list, so drift is not
        // merely unlikely, it is unrepresentable. If this ever fails, someone has re-introduced a second table.
        Assert.Equal(
            reports.PayrollColumns.Select(c => c.Header).ToList(),
            exported.Columns.Select(c => c.Header).ToList());
        Assert.Equal(
            reports.PayrollColumns.Select(c => c.Header).ToList(),
            printed.Columns.Select(c => c.Header).ToList());
    }

    /// <summary>
    /// Alt+K (save this view) indexes the persisted-token map DIRECTLY, so a kind missing from it throws
    /// <see cref="KeyNotFoundException"/> the instant an operator presses the chord. Saved views are one of the
    /// gestures this re-home exists to hand these reports, so a missing token would have turned the fix
    /// into a crash on the very feature being delivered.
    /// </summary>
    [Theory]
    [MemberData(nameof(RehomedKinds))]
    public void Every_rehomed_kind_has_a_saved_view_token_that_round_trips(ReportKind kind)
    {
        var token = ReportsViewModel.TokenFor(kind);      // throws if the kind was not registered
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal(kind, ReportsViewModel.KindFor(token));
    }

    /// <summary>
    /// 🔴 <b>THE TWO GRID-VISIBILITY TRAPS, PINNED.</b> A matrix report leaves <c>Rows</c> empty, which is how
    /// the accounting grid and the shared empty-state overlay both decide what to show. Get either wrong and the
    /// report is CORRECT and UNREADABLE: the accounting Particulars/Dr/Cr table renders stacked on top of the
    /// real grid, or a fully populated report renders under a "No entries for the selected period." banner.
    /// </summary>
    [Theory]
    [MemberData(nameof(RehomedKinds))]
    public void A_rehomed_report_shows_neither_the_accounting_grid_nor_the_empty_state_overlay(ReportKind kind)
    {
        var vm = FullFixture($"Grid {kind}");
        var reports = new ReportsViewModel(vm.Company!, kind);

        Assert.False(reports.IsAccountingReport,
            $"{kind} still claims the accounting grid, which would draw an empty Particulars/Dr/Cr table over "
            + "its own rows — two tables at once.");
        Assert.False(reports.IsEmpty,
            $"{kind} reports IsEmpty, so the shared C7 overlay would cover a populated matrix with "
            + "\"No entries for the selected period.\" — the figures are on screen underneath it.");
        Assert.True(reports.IsPayrollMatrix, $"{kind} does not render through the matrix DataTemplate.");
    }

    // ================================================================= the routes (arrow-walked)

    /// <summary>
    /// Census 11.9 / 11.10 / 11.11 — Gateway → Statements of Accounts → {Outstandings | Cost Centres | Budgets}
    /// and every leaf row lands on a <see cref="ReportKind"/>, not on a page Screen.
    /// Fails on today's main, where each of these rows opens a bespoke page.
    /// </summary>
    [Fact]
    public void The_statements_of_accounts_rows_now_open_reports_rather_than_pages()
    {
        var vm = FullFixture("Route Statements");

        AssertRouteOpensReport(vm, ReportKind.ReceivablesOutstanding, "Statements of Accounts", "Outstandings", "Receivables");
        AssertRouteOpensReport(vm, ReportKind.PayablesOutstanding, "Statements of Accounts", "Outstandings", "Payables");
        AssertRouteOpensReport(vm, ReportKind.CostCategorySummary, "Statements of Accounts", "Cost Centres", "Category Summary");
        AssertRouteOpensReport(vm, ReportKind.CostCentreBreakup, "Statements of Accounts", "Cost Centres", "Cost Centre Break-up");
        AssertRouteOpensReport(vm, ReportKind.BudgetVariance, "Statements of Accounts", "Budgets", "Budget Variance");
    }

    /// <summary>
    /// 🔴 <b>THE FLAGSHIP OF THIS WAVE.</b> <c>CostReports.BuildLedgerBreakup</c> has been implemented,
    /// documented and unit-tested since Phase 2 with <b>zero production callers</b> — seven separate source
    /// comments across this repository cite it BY NAME as the example of careful, correct-looking, unreachable
    /// code counted as delivered. Nothing about the engine was ever broken. It had no door.
    ///
    /// <para>This test is the door, walked with the arrow keys. It fails on today's main for the plainest of
    /// reasons: there is no "Ledger Break-up" row in the Cost Centres column to arrow to.</para>
    /// </summary>
    [Fact]
    public void The_ledger_breakup_report_is_reachable_for_the_first_time()
    {
        var vm = FullFixture("Ledger Breakup Route");

        AssertRouteOpensReport(vm, ReportKind.CostCentreLedgerBreakup,
            "Statements of Accounts", "Cost Centres", "Ledger Break-up");

        // …and it actually projects the engine, rather than opening an empty shell that merely looks routed.
        var reports = vm.Reports!;
        WidenToAllTime(reports, vm.Company!);
        Assert.Contains(reports.PayrollRows,
            r => r.Cells.Any(c => c.Text == "Head Office") && r.Cells.Any(c => c.Text == "Rent"));
    }

    /// <summary>
    /// Census 7.13 / 7.14 — Gateway → Statutory Reports → Payroll → {Gratuity Provision | Bonus Register}, both
    /// now opening reports. Their two-part enrolment gate is unchanged and is asserted alongside, because a gate
    /// that silently opened for a non-enrolled establishment would be a worse regression than the missing print.
    /// </summary>
    [Fact]
    public void The_two_payroll_registers_now_open_reports_and_keep_their_enrolment_gate()
    {
        var vm = PayrollFixture("Payroll Register Route", gratuity: true, bonus: true);

        AssertRouteOpensReport(vm, ReportKind.GratuityProvisionRegister, "Statutory Reports", "Payroll", "Gratuity Provision");
        AssertRouteOpensReport(vm, ReportKind.BonusRegister, "Statutory Reports", "Payroll", "Bonus Register");

        // The gate: an establishment enrolled for neither statute reaches neither report, by the opener AND by
        // the absence of the menu row.
        var ungated = PayrollFixture("Payroll Register Gated", gratuity: false, bonus: false);
        ungated.OpenGratuityProvisionReport();
        Assert.Null(ungated.Reports);
        ungated.OpenBonusRegisterReport();
        Assert.Null(ungated.Reports);
    }

    // ================================================================= the figures survived the move

    /// <summary>
    /// The golden gratuity accrual — Basic + DA ₹26,000 over exactly 10 completed years ⇒ ₹1,50,000, vested —
    /// rendered by the RE-HOMED report. The same figure the dedicated page produced, which is the point: a
    /// re-home that changed a statutory number would be a defect wearing a refactor's clothes.
    /// </summary>
    [Fact]
    public void The_rehomed_gratuity_register_renders_the_golden_150000()
    {
        var vm = PayrollFixture("Gratuity Golden", gratuity: true, bonus: false);
        var c = vm.Company!;
        var basic = CreateBasicHead(c);
        var groupId = new PayrollService(c).CreateEmployeeGroup("Staff").Id;
        var fyStart = c.FinancialYearStart;
        AddEmployee(c, basic, groupId, "Anil Rao", "E001", fyStart.AddYears(-10).AddDays(29), 26_000m);
        _storage.Save(c);

        vm.OpenGratuityProvisionReport();
        var reports = vm.Reports!;
        reports.SetAsOf(fyStart.AddMonths(1).AddDays(-1));   // as-on 30-Apr-<fyStart>

        var row = Assert.Single(reports.PayrollRows, r => r.Cells[0].Text == "Anil Rao");
        Assert.Equal("10", row.Cells[3].Text);
        Assert.Equal("Yes", row.Cells[4].Text);
        Assert.Equal("26,000", row.Cells[5].Text);
        Assert.Equal("1,50,000", row.Cells[6].Text);

        var total = Assert.Single(reports.PayrollRows, r => r.IsTotal);
        Assert.Equal("1,50,000", total.Cells[6].Text);

        // The movement band is a fact about the COMPANY, so it rides in the second section rather than as extra
        // employee columns. Nothing is posted yet, so the whole liability is still to post.
        Assert.True(reports.HasPayrollSection2);
        Assert.Contains(reports.PayrollRows2,
            r => r.Cells[0].Text == "Delta still to post" && r.Cells[1].Text == "1,50,000");
    }

    /// <summary>
    /// 🔴 <b>Ctrl+A ON THE RE-HOMED GRATUITY REPORT POSTS THE PROVISION.</b> The register's primary ACTION had
    /// to travel with it; a re-home that delivered print and export while quietly dropping the only way to post
    /// the period-end voucher would be a net loss. The duplicate-post refusal travels too — the second Ctrl+A on
    /// an already-provisioned date must be a friendly no-op, not a second ₹1,50,000 on the books.
    /// </summary>
    [Fact]
    public void Ctrl_A_on_the_rehomed_gratuity_report_posts_the_provision_once_and_refuses_the_second()
    {
        var vm = PayrollFixture("Gratuity Post", gratuity: true, bonus: false);
        var c = vm.Company!;
        var basic = CreateBasicHead(c);
        var groupId = new PayrollService(c).CreateEmployeeGroup("Staff").Id;
        var fyStart = c.FinancialYearStart;
        AddEmployee(c, basic, groupId, "Anil Rao", "E001", fyStart.AddYears(-10).AddDays(29), 26_000m);
        _storage.Save(c);

        vm.OpenGratuityProvisionReport();
        var reports = vm.Reports!;
        reports.SetAsOf(fyStart.AddMonths(1).AddDays(-1));

        vm.PostGratuityProvisionFromReport();

        Assert.Equal(150_000m,
            new PayrollVoucherService(c).PriorGratuityProvisionBalance(reports.AsOf.AddDays(1)).Amount);
        Assert.Contains("Posted gratuity provision", reports.ReportActionStatus);

        // The report refreshed underneath the message: the movement band now shows a zero delta.
        Assert.Contains(reports.PayrollRows2,
            r => r.Cells[0].Text == "Delta still to post" && r.Cells[1].Text == "0");

        // Second Ctrl+A on the same date: refused, and NOT a second posting.
        vm.PostGratuityProvisionFromReport();
        Assert.Contains("unchanged from the posted balance", reports.ReportActionStatus);
        Assert.Equal(150_000m,
            new PayrollVoucherService(c).PriorGratuityProvisionBalance(reports.AsOf.AddDays(1)).Amount);
    }

    /// <summary>
    /// The golden statutory bonus — Basic + DA ₹18,000 capped at ₹7,000, 8.33%, full year ⇒ ₹6,997 — through the
    /// re-homed report, with the above-ceiling member still excluded by the Act's own rule.
    /// </summary>
    [Fact]
    public void The_rehomed_bonus_register_renders_the_golden_6997_and_excludes_the_high_earner()
    {
        var vm = PayrollFixture("Bonus Golden", gratuity: false, bonus: true);
        var c = vm.Company!;
        var basic = CreateBasicHead(c);
        var groupId = new PayrollService(c).CreateEmployeeGroup("Staff").Id;
        AddEmployee(c, basic, groupId, "Bina Roy", "B001", c.FinancialYearStart.AddYears(-2), 18_000m);
        AddEmployee(c, basic, groupId, "Chetan Roy", "B002", c.FinancialYearStart.AddYears(-2), 25_000m);
        _storage.Save(c);

        vm.OpenBonusRegisterReport();
        var reports = vm.Reports!;
        reports.SetAsOf(c.FinancialYearStart.AddYears(1).AddDays(-1));

        var row = Assert.Single(reports.PayrollRows, r => r.Cells[0].Text == "Bina Roy");
        Assert.Equal("Yes", row.Cells[2].Text);
        Assert.Equal("18,000", row.Cells[3].Text);
        Assert.Equal("7,000", row.Cells[4].Text);
        Assert.Equal("8.33%", row.Cells[5].Text);
        Assert.Equal("6,997", row.Cells[6].Text);

        Assert.DoesNotContain(reports.PayrollRows, r => r.Cells[0].Text == "Chetan Roy");
    }

    /// <summary>
    /// 🔴 <b>THE ACCOUNTING-YEAR SNAP, WHICH IS EASY TO GET WRONG AND EXPENSIVE WHEN IT IS.</b> Statutory bonus
    /// is an ANNUAL computation and the re-homed report derives its year from the report date. Taking
    /// <c>asOf.Year</c> naively would put February — a month inside the PREVIOUS accounting year for an
    /// April-start book — into the wrong year entirely, and report a member's bonus against twelve months they
    /// had not yet worked. A February as-of and a May as-of must therefore name different years.
    /// </summary>
    [Fact]
    public void The_bonus_year_is_the_accounting_year_containing_the_report_date_not_the_calendar_year()
    {
        var vm = PayrollFixture("Bonus Year Snap", gratuity: false, bonus: true);
        var c = vm.Company!;
        var fyStart = c.FinancialYearStart;                 // 1 April of some year Y
        _storage.Save(c);

        vm.OpenBonusRegisterReport();
        var reports = vm.Reports!;

        // A February date falls in accounting year (Y-1)…(Y) only when February is AFTER the FY start month in
        // the same calendar year, which for an April-start book it is not — so Feb of year Y+1 belongs to Y.
        reports.SetAsOf(new DateOnly(fyStart.Year + 1, 2, 15));
        var februarySubtitle = reports.Subtitle;

        reports.SetAsOf(new DateOnly(fyStart.Year + 1, 5, 15));
        var maySubtitle = reports.Subtitle;

        Assert.NotEqual(februarySubtitle, maySubtitle);
        Assert.Contains(fyStart.Year.ToString(), februarySubtitle);
        Assert.Contains((fyStart.Year + 1).ToString(), maySubtitle);
    }

    /// <summary>
    /// Census 11.9 — the re-homed Bills Receivable carries the open bill, its pending amount and its ageing
    /// bucket, and the ageing SUMMARY arrives in the matrix's own second band rather than being folded under the
    /// bill columns (a bucket total printed under a "Due Date" heading is the wrong-figure-under-a-right-heading
    /// defect this codebase records having shipped once already).
    /// </summary>
    [Fact]
    public void The_rehomed_receivables_report_carries_the_bill_and_a_separate_ageing_band()
    {
        var vm = FullFixture("Receivables Shape");
        var c = vm.Company!;
        var reports = new ReportsViewModel(c, ReportKind.ReceivablesOutstanding);
        WidenToAllTime(reports, c);

        var row = Assert.Single(reports.PayrollRows, r => r.Cells[0].Text == "Acme Traders");
        Assert.Equal("INV-1", row.Cells[1].Text);
        Assert.Equal("10,000.00", row.Cells[5].Text);        // pending

        Assert.True(reports.HasPayrollSection2, "The ageing summary band did not render.");
        Assert.Equal(2, reports.PayrollColumns2.Count);       // bucket + pending, its own shape
        Assert.Contains(reports.PayrollRows2, r => r.Cells[0].Text == "Not due" || r.Cells[0].Text == "90+ days");
    }

    /// <summary>
    /// 🔴 <b>THE SETTLE WORKFLOW SURVIVED THE RE-HOME — DRIVEN BY THE ACTUAL KEYSTROKE.</b> Routing the
    /// Receivables/Payables menu rows to ReportKinds means nothing else points at <c>Screen.Outstandings</c> any
    /// more — and that page is the only place the spacebar bill multi-select and the settlement preload live.
    /// Alt+A on the report is the door that keeps it reachable. Without it the wave would have created a second
    /// instance of the very defect census row 11.10 records ("one shipped report nobody can reach").
    ///
    /// <para>🔴 <b>THE PREVIOUS VERSION OF THIS TEST WAS A DEAD GUARD AND IS THE REASON IT WAS REWRITTEN.</b> It
    /// called <c>vm.OpenSettlementPageFromOutstandingsReport()</c> directly and never pressed a key, so it
    /// exercised the view-model method and NOT the chord. The chord lives in
    /// <c>MainWindow.axaml.cs</c>'s tunnel chain, which no view-model call reaches: deleting that entire arm —
    /// i.e. removing the operator's only route to the page — left the old test GREEN. A test named after a
    /// keystroke that never presses the keystroke is exactly the "overstated closure" class this repository
    /// already has filed. The rewrite opens the real window and drives
    /// <c>KeyPressQwerty(PhysicalKey.A, Alt)</c>, so the arm is load-bearing.</para>
    ///
    /// <para><b>Mutation-verified, measured.</b> Disabling the <c>Key.A</c> arm in <c>MainWindow.axaml.cs</c>
    /// fails exactly this test and nothing else — <c>Failed: 1, Passed: 43</c>, <c>Expected: Outstandings /
    /// Actual: Report</c>. Restored, the class is <c>Failed: 0, Passed: 44</c>. The negative case below is what
    /// stops the fix being "Alt+A navigates from everywhere", which would satisfy this assertion while stealing
    /// the key from the Day Book's own Alt+A.</para>
    /// </summary>
    [AvaloniaFact]
    public void Alt_A_on_the_rehomed_outstandings_report_reaches_the_settlement_page()
    {
        var vm = FullFixture("Settle Door");
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        // ---- Receivables: the chord, not the method.
        vm.OpenReport(ReportKind.ReceivablesOutstanding);
        Pump(window);
        Assert.Equal(Screen.Report, vm.CurrentScreen);           // else the press below proves nothing

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
        Pump(window);

        Assert.Equal(Screen.Outstandings, vm.CurrentScreen);
        Assert.NotNull(vm.Outstandings);
        Assert.Equal(OutstandingsKind.Receivables, vm.Outstandings!.Kind);

        // ---- Payables: the SAME chord must carry the side across, not default back to the receivable half.
        vm.OpenReport(ReportKind.PayablesOutstanding);
        Pump(window);
        Assert.Equal(Screen.Report, vm.CurrentScreen);

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
        Pump(window);

        Assert.Equal(Screen.Outstandings, vm.CurrentScreen);
        Assert.Equal(OutstandingsKind.Payables, vm.Outstandings!.Kind);

        window.Close();
    }

    /// <summary>
    /// 🔴 <b>AND THE GUARD IS NARROW.</b> The positive test above is satisfied just as well by an arm that fires
    /// Alt+A on every report in the app — which would silently steal the key from the Day Book's "Add voucher in
    /// a report", a shipped chord on the same modifier. So the same keystroke is driven on a re-homed report
    /// that is NOT an outstandings side, and must leave the operator exactly where they were.
    ///
    /// <para>🔴 <b>MUTATION-VERIFIED, AND THE MEASUREMENT CORRECTED WHAT THIS COMMENT FIRST CLAIMED.</b> The
    /// obvious mutation — widening the window arm's guard to <c>vm.Reports is not null</c> — left this test
    /// GREEN (<c>Failed: 0, Passed: 44</c>). The narrowness is enforced TWICE, and the second guard is the one
    /// carrying the load: <see cref="MainWindowViewModel.OpenSettlementPageFromOutstandingsReport"/> switches on
    /// the kind with no <c>default</c> arm, so a widened key guard still lands on nothing. Only removing BOTH —
    /// the widened key guard plus a <c>default: OpenOutstandings(Receivables)</c> in that switch — reddens it:
    /// <c>Failed: 1, Passed: 43</c>, <c>Expected: Report / Actual: Outstandings</c>. That is the mutation this
    /// test is proven against, and it is recorded precisely because the first guess was wrong and a mutation
    /// claim nobody re-ran is worth no more than an unopened citation.</para>
    /// </summary>
    [AvaloniaFact]
    public void Alt_A_does_not_fire_on_a_rehomed_report_that_is_not_an_outstandings_side()
    {
        var vm = FullFixture("Settle Guard");
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        vm.OpenReport(ReportKind.CostCentreLedgerBreakup);
        Pump(window);

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
        Pump(window);

        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.Equal(ReportKind.CostCentreLedgerBreakup, vm.Reports!.Kind);

        window.Close();
    }

    /// <summary>
    /// 🔴 <b>AND THE CHORD IS REFUSED WHILE A COLUMN IS STACKED OVER THE REPORT — THE DEFECT THE TWO TESTS
    /// ABOVE DID NOT CATCH, FOUND BY PROBE AND MEASURED BEFORE IT WAS FIXED.</b>
    ///
    /// <para>The arm shipped guarded on <c>vm.Reports is { Kind: … }</c> alone. That is report-CONTEXT width,
    /// and it is the wrong width for a verb that NAVIGATES AWAY: <c>Reports</c> stays bound BENEATH an F12
    /// config panel, an Alt+F12 sort/filter panel, an Alt+K saved-views panel and a Print Preview column,
    /// precisely so the report-PARAMETER shortcuts keep acting on the report underneath. So Alt+A fired while
    /// the operator was standing INSIDE one of those and threw them out of the panel they were working in,
    /// discarding the column they had open. Measured before the fix, not theorised: F12 over Bills Receivable
    /// gave <c>Expected: ReportConfig / Actual: Outstandings</c>, and Print Preview over Bills Payable gave
    /// <c>Expected: PrintPreview / Actual: Outstandings</c>. The fix is <c>IsLiveReportPage</c>, the predicate
    /// this codebase had already written for exactly this distinction and documented as the one a destructive
    /// verb needs — the same hole Phase 10.11 S3's Alt+X arm fell into.
    ///
    /// <para>🔴 <b>Mutation-verified, measured both ways.</b> Removing <c>vm.IsLiveReportPage</c> from the arm
    /// in <c>MainWindow.axaml.cs</c> — i.e. restoring exactly what shipped — fails THIS test and nothing else.
    /// Both stacked surfaces are asserted because they are different code paths into the same predicate: F12
    /// opens a config column, Print Preview opens a preview column, and an exclusion list written against one
    /// screen name would have missed the other. The positive test above still passes with the guard in place,
    /// which is what proves the fix did not simply kill the chord.</para>
    ///
    /// <para>🔴 <b>A THIRD PROBE FAILED AND IS NOT FIXED HERE — IT IS REPORTED INSTEAD.</b> The same press on
    /// the DAY BOOK's own Alt+A arm, which this branch does not touch, also fires through an F12 panel
    /// (<c>Expected: ReportConfig / Actual: AddVoucherPicker</c>). That arm is guarded on
    /// <c>IsDayBookReport &amp;&amp; !IsDayBookPickerOpen</c>, and <c>IsDayBookReport</c> excludes only
    /// LedgerVouchers and VoucherDetail, so a stacked config column passes it. It is a PRE-EXISTING defect on
    /// main of the identical shape, it is out of this track's scope, and it is written down here rather than
    /// silently widened into this fix.</para>
    /// </summary>
    [AvaloniaFact]
    public void AltA_on_a_rehomed_outstandings_report_is_refused_while_a_column_is_stacked_over_it()
    {
        var vm = FullFixture("Stacked Guard");
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        // ---- F12 configuration column over Bills Receivable.
        vm.OpenReport(ReportKind.ReceivablesOutstanding);
        Pump(window);
        vm.OpenReportConfig();
        Pump(window);
        Assert.Equal(Screen.ReportConfig, vm.CurrentScreen);   // else the press below proves nothing

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
        Pump(window);

        Assert.Equal(Screen.ReportConfig, vm.CurrentScreen);

        // ---- Print Preview column over Bills Payable: a different column, the same predicate.
        vm.OpenReport(ReportKind.PayablesOutstanding);
        Pump(window);
        vm.OpenPrintPreview();
        Pump(window);
        Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
        Pump(window);

        Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);

        window.Close();
    }

    /// <summary>
    /// 🔴 <b>NO COLUMN IS NARROWER THAN ITS OWN CAPTION, AND EVERY BODY CELL MATCHES ITS COLUMN'S WIDTH.</b>
    ///
    /// <para>Two separate defects, both invisible to a view-model test that only checks the text. The matrix
    /// header band renders with <c>TextTrimming="CharacterEllipsis"</c>, so a caption wider than its column
    /// ships CLIPPED — and <c>"Completed Year…"</c> reads as deliberate, which is why a clipped heading is worse
    /// than a missing one. Four of these captions overflowed the hand-picked constants first written for them
    /// ("Overdue Days" 95px against 92, "Completed Years" 115 against 92, "Date of Joining" 115 against 104,
    /// "Actual Basic + DA" 128 against 124). Separately, the band and every body row are horizontal
    /// <c>StackPanel</c>s of fixed-width cells, so one mismatched cell width shifts every column to its right.</para>
    ///
    /// <para>The arithmetic is the shipped header metric — <c>ColumnHeaderAdvance</c> 6.5977px at the colHdr's
    /// 12pt, plus its 16px padding — the same expression <c>PayColWidthFor</c> uses.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(RehomedKinds))]
    public void No_rehomed_column_is_narrower_than_its_caption_and_cells_match_their_columns(ReportKind kind)
    {
        const double headerAdvance = 6.5977;
        const double headerPadding = 16;

        var vm = FullFixture($"Widths {kind}");
        var reports = new ReportsViewModel(vm.Company!, kind);

        foreach (var band in new[] { reports.PayrollColumns, reports.PayrollColumns2 })
            foreach (var c in band)
                Assert.True(c.Width >= c.Header.Length * headerAdvance + headerPadding,
                    $"{kind}: the column '{c.Header}' is {c.Width:0.#}px wide but its own caption needs "
                    + $"{c.Header.Length * headerAdvance + headerPadding:0.#}px — it ships clipped.");

        foreach (var row in reports.PayrollRows)
        {
            Assert.Equal(reports.PayrollColumns.Count, row.Cells.Count);
            for (int i = 0; i < row.Cells.Count; i++)
                Assert.Equal(reports.PayrollColumns[i].Width, row.Cells[i].Width);
        }

        foreach (var row in reports.PayrollRows2)
        {
            Assert.Equal(reports.PayrollColumns2.Count, row.Cells.Count);
            for (int i = 0; i < row.Cells.Count; i++)
                Assert.Equal(reports.PayrollColumns2[i].Width, row.Cells[i].Width);
        }
    }

    /// <summary>
    /// 🔴 <b>THE COST GRAND TOTAL PRINTS UNDER "OWN", NOT UNDER "ROLLED UP".</b> The Cost Centre Break-up has
    /// four columns and the footing figure could plausibly sit under either money column — but
    /// <c>CostCentreBreakupReport.GrandTotal</c> is expressly the cost counted ONCE PER ENTRY LINE, which is
    /// what the Own column sums to; it is not a roll-up. Footing it under "Rolled Up" would put a correct
    /// number under a heading that means something else, and a reader has no way to tell. The obvious
    /// implementation — foot the last column — gets this wrong, which is why it is pinned.
    /// </summary>
    [Fact]
    public void The_cost_breakup_grand_total_foots_the_own_column_not_the_rolled_up_column()
    {
        var vm = FullFixture("Cost Total Column");
        var reports = new ReportsViewModel(vm.Company!, ReportKind.CostCentreBreakup);
        WidenToAllTime(reports, vm.Company!);

        var ownColumn = reports.PayrollColumns.Select(c => c.Header).ToList().IndexOf("Own");
        var rolledUpColumn = reports.PayrollColumns.Select(c => c.Header).ToList().IndexOf("Rolled Up");
        Assert.True(ownColumn >= 0 && rolledUpColumn >= 0);

        var total = Assert.Single(reports.PayrollRows, r => r.IsTotal);
        Assert.Equal("5,000.00", total.Cells[ownColumn].Text);
        Assert.Equal(string.Empty, total.Cells[rolledUpColumn].Text);
    }

    // ================================================================= the realised visual tree

    /// <summary>
    /// 🔴 <b>THE CAPTIONS REACH THE SCREEN, MEASURED ON THE REALISED VISUAL TREE, FOR ALL EIGHT KINDS.</b>
    /// Everything above this point asserts view-model state, and view-model state is exactly what a report can
    /// have in full while rendering nothing — the matrix pane is gated on <c>IsPayrollMatrix</c>, the accounting
    /// pane on <c>IsAccountingReport</c>, and getting either wrong yields a correct view model behind a blank or
    /// double-drawn pane. This test opens the real window, drives the real route, and reads the header band that
    /// actually laid out.
    ///
    /// <para>🔴 <b>IT WAS A ONE-KIND [Fact] AND A REVIEW FINDING SAID SO.</b> It rendered
    /// <c>CostCentreLedgerBreakup</c> only and left the other seven proven at view-model and projector level.
    /// The gating flag is uniform, so the risk was low — but "the actual rendered columns for every re-homed
    /// kind" was the requirement, the harness was already parameterised, and a [Theory] costs nothing.</para>
    ///
    /// <para>🔴 <b>AND THE WIDTH IS CROSS-CHECKED AGAINST THE LAYOUT, WHICH THE VIEW-MODEL WIDTH TEST CANNOT
    /// DO.</b> <c>No_rehomed_column_is_narrower_than_its_caption…</c> asserts
    /// <c>Width &gt;= Header.Length * 6.5977 + 16</c>, which is the expression <c>PayColWidthFor</c> itself
    /// computes — for every column <c>WideCol</c> built it cannot fail, and a review finding correctly called
    /// that semi-vacuous (it is a real lock against a future call site that bypasses <c>WideCol</c>, and no
    /// more). Here the assertion is against a DIFFERENT authority: the laid-out header TextBlock's own
    /// <c>Bounds.Width</c>, which the template takes from <c>Width="{Binding Width}"</c>. A template that
    /// dropped that binding — the change that would silently auto-size the band out of step with the body rows
    /// and shift every column — reddens this and nothing else. The first body row is measured against the same
    /// band, because header-and-body alignment is the defect an operator actually sees.</para>
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(RehomedKinds))]
    public void The_rehomed_report_header_band_lays_out_on_screen_with_its_captions(ReportKind kind)
    {
        var vm = FullFixture($"Visual Tree {kind}");
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        vm.OpenReport(kind);
        Pump(window);

        var reports = vm.Reports!;
        Assert.True(reports.IsPayrollMatrix, $"{kind} does not render through the matrix pane.");

        foreach (var column in reports.PayrollColumns)
        {
            var block = Descendants(window)
                .OfType<TextBlock>()
                .FirstOrDefault(t => t.IsEffectivelyVisible && t.Text == column.Header);

            Assert.True(block is not null,
                $"{kind}: the column caption '{column.Header}' never reached the visual tree — the report's "
                + "figures are on screen under headings nobody can read.");
            Assert.True(block!.Bounds.Width > 0 && block.Bounds.Height > 0,
                $"{kind}: the column caption '{column.Header}' laid out at a degenerate size.");

            // The band laid out at the width the view model computed — not at whatever the text happened to
            // need. This is the half the arithmetic test cannot prove, because it re-derives the arithmetic.
            //
            // The upper bound is Width + 1 because Avalonia rounds a laid-out size UP to a whole device pixel:
            // MEASURED here, not assumed — "Overdue Days" computes 95.1724 and lays out at 96, "Actual Basic +
            // DA" computes 128.1609 and lays out at 129. A one-pixel window is still far tighter than the
            // failure it guards: a template that dropped Width="{Binding Width}" auto-sizes each caption to its
            // own text (~70px for a 240px column), which lands nowhere near this range.
            Assert.InRange(block.Bounds.Width, column.Width, column.Width + 1);
        }

        // The accounting Particulars/Dr/Cr band must NOT be drawn at the same time. "Particulars" is that grid's
        // own first caption and no re-homed report uses it, so a visible one here means two tables are stacked —
        // which is how this defect presented the last time it shipped.
        Assert.DoesNotContain(
            Descendants(window).OfType<TextBlock>().Where(t => t.IsEffectivelyVisible),
            t => t.Text == "Particulars");

        window.Close();
    }

    // ================================================== the write paths (review findings 2 and 3, both HIGH)

    /// <summary>
    /// 🔴🔴 <b>A BARE ENTER ON THE RE-HOMED GRATUITY REGISTER MUST NOT POST A VOUCHER.</b>
    ///
    /// <para>An adversarial review MEASURED this on the realised window against a fully green gate: one Enter
    /// took the voucher count 0 → 1 with no confirmation, status <i>"Posted gratuity provision as-on
    /// 30-Apr-2026: Dr 1,50,000.00 = Cr 1,50,000.00"</i>. The re-home created it. Enter means DRILL on ~90
    /// report kinds; the window tries <c>DrillSelectedRow</c> first and falls through to
    /// <c>ActivateSelected</c> when it declines, which on a matrix report it ALWAYS does because <c>Rows</c> is
    /// empty — so the <c>case Screen.Report when Kind == GratuityProvisionRegister</c> arm, which posts, was
    /// sitting directly under the drill key.</para>
    ///
    /// <para>🔴 <b>THIS TEST PRESSES THE REAL KEY, AND THAT IS THE POINT.</b> The branch's existing
    /// <c>Ctrl_A_…_posts_the_provision_once</c> calls <c>PostGratuityProvisionFromReport()</c> directly, so it
    /// is green whatever the key arms do — it could not have caught this and did not. Both gestures are driven
    /// here through <c>KeyPressQwerty</c> on a shown <see cref="MainWindow"/>: Enter must leave the books
    /// untouched and say which key posts, and Ctrl+A on the very same window must still post, or the "fix"
    /// would simply be the feature removed.</para>
    ///
    /// <para><b>Mutation-verified.</b> Restoring <c>vm.ActivateSelected()</c> (no argument) at the window's
    /// <c>case Key.Enter when !IsPickerOpen(e)</c> arm reddens exactly this test with
    /// <c>Assert.Empty() Failure: Collection was not empty</c> on the voucher list. Restored byte-identically.</para>
    /// </summary>
    [AvaloniaFact]
    public void Bare_Enter_on_the_rehomed_gratuity_register_does_not_post_but_Ctrl_A_still_does()
    {
        var vm = GratuityFixtureWithOneEmployee("Enter Guard", out var c, out var asOn);
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        vm.OpenGratuityProvisionReport();
        Pump(window);
        var reports = vm.Reports!;
        reports.SetAsOf(asOn);
        Pump(window);
        Assert.Equal(Screen.Report, vm.CurrentScreen);          // else the presses below prove nothing
        Assert.Empty(c.Vouchers);

        // ---- Enter: the drill key. It must not write.
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump(window);

        Assert.Empty(c.Vouchers);
        Assert.Equal(0m, new PayrollVoucherService(c).PriorGratuityProvisionBalance(asOn.AddDays(1)).Amount);
        Assert.Contains("Ctrl+A", reports.ReportActionStatus);
        Assert.Equal(Screen.Report, vm.CurrentScreen);          // and it did not navigate away either

        // ---- Ctrl+A on the SAME window: the accept chord still posts, through the real key.
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Pump(window);

        Assert.Single(c.Vouchers);
        Assert.Equal(150_000m, new PayrollVoucherService(c).PriorGratuityProvisionBalance(asOn.AddDays(1)).Amount);
        Assert.Contains("Posted gratuity provision", vm.Reports!.ReportActionStatus);

        window.Close();
    }

    /// <summary>
    /// 🔴🔴 <b>A SAVED VIEW MUST NOT WALK AROUND THE ER-13 STATUTORY GATE.</b>
    ///
    /// <para>MEASURED by the review: with Payroll Statutory switched off, the gated opener refused correctly and
    /// Alt+K → saved view opened the same register anyway — <i>"opened=True, rows=2, vouchers 0 → 1"</i>. This
    /// wave opened that door by registering saved-view tokens for all eight re-homed kinds;
    /// <c>ApplySavedView</c> resolves a token to a kind and calls <c>OpenReport</c> with no gate at all.
    /// A gate one of two doors asks about is cosmetic — the branch's own quick-button comment says exactly
    /// that.</para>
    ///
    /// <para>The refusal is asserted BOTH ways. A guard that simply never opens a saved view would satisfy the
    /// negative half and destroy the feature, so the same saved view is applied again with the switch back on
    /// and must open.</para>
    ///
    /// <para><b>Mutation-verified.</b> Removing the <c>ReportKindIsPermitted</c> block from
    /// <c>MainWindowViewModel.ApplySavedView</c> reddens exactly this test —
    /// <c>Assert.NotEqual() Failure: Values are equal (Report)</c>. Restored byte-identically.</para>
    /// </summary>
    [Fact]
    public void A_saved_view_cannot_reopen_a_report_the_company_has_switched_off()
    {
        var vm = GratuityFixtureWithOneEmployee("Saved View Gate", out var c, out var asOn);

        vm.OpenGratuityProvisionReport();
        vm.Reports!.SetAsOf(asOn);
        var savedView = vm.Reports!.ToSavedView();
        Assert.NotEmpty(vm.Reports!.PayrollRows);               // the view was captured off a POPULATED report
        vm.Back();

        // F11 → Payroll Statutory off. GratuityConfig is deliberately left in place — that is what the product
        // does (OnPayrollStatutoryEnabledChanged clears the flag only), and it is the state the bypass lived in.
        SetPayrollStatutory(vm, false);
        Assert.NotNull(c.GratuityConfig);

        vm.OpenGratuityProvisionReport();                       // the gated door: refuses, as it always did
        Assert.NotEqual(Screen.Report, vm.CurrentScreen);

        vm.ApplySavedView(savedView);                           // the ungated door: must now refuse too

        Assert.NotEqual(Screen.Report, vm.CurrentScreen);
        Assert.Contains("switched off", vm.Message);
        Assert.Empty(c.Vouchers);

        // ---- And the gate is not a wall: switch the statute back on and the same saved view opens.
        SetPayrollStatutory(vm, true);
        vm.ApplySavedView(savedView);

        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.Equal(ReportKind.GratuityProvisionRegister, vm.Reports!.Kind);
    }

    /// <summary>
    /// 🔴 <b>AND THE WRITE ITSELF REFUSES, INDEPENDENTLY OF WHICH DOOR OPENED THE REPORT.</b> The saved-view
    /// gate above closes the door this wave opened; this closes the write. They are separate locks on purpose —
    /// <c>PostGratuityProvisionFromReport</c> guarded on <c>GratuityConfig</c> only while its own opener guarded
    /// on <c>PayrollStatutoryEnabled</c> AND <c>GratuityConfig</c>, and a write path that relies on the
    /// reachability of its opener is a guard with a sell-by date. The report is opened here through the ungated
    /// <c>OpenReport</c> precisely so the door gate cannot be what makes this test pass.
    ///
    /// <para><b>Mutation-verified.</b> Deleting the <c>PayrollStatutoryEnabled</c> block from
    /// <c>PostGratuityProvisionFromReport</c> reddens exactly this test (<c>Collection was not empty</c>).
    /// Restored byte-identically.</para>
    /// </summary>
    [Fact]
    public void The_gratuity_post_refuses_when_payroll_statutory_is_off_however_the_report_was_opened()
    {
        var vm = GratuityFixtureWithOneEmployee("Post Guard", out var c, out var asOn);

        vm.OpenReport(ReportKind.GratuityProvisionRegister);    // the UNGATED opener, deliberately
        vm.Reports!.SetAsOf(asOn);
        c.PayrollStatutoryEnabled = false;                      // the F11 tick, applied to the aggregate

        vm.PostGratuityProvisionFromReport();

        Assert.Empty(c.Vouchers);
        Assert.Equal(0m, new PayrollVoucherService(c).PriorGratuityProvisionBalance(asOn.AddDays(1)).Amount);
        Assert.Contains("Payroll Statutory is not enabled", vm.Reports!.ReportActionStatus);
    }

    /// <summary>
    /// 🔴 <b>THE PROJECTIONS DEGRADE ON THEIR OWN ACCOUNT, AND KEEP THEIR COLUMNS WHILE THEY DO IT.</b> The two
    /// locks above are on the shell. This is the third, on the builder, so the fourth door — whatever it turns
    /// out to be — cannot render a switched-off company's wage base or cost allocations.
    ///
    /// <para>The column band is asserted INTACT in the degraded state, and that is not decoration: both egress
    /// projectors read their captions from that live band, so a builder that returned before declaring its
    /// columns would export a header row of bare commas and print an empty header band — the exact defect this
    /// whole re-home exists to avoid.</para>
    /// </summary>
    [Fact]
    public void A_rehomed_report_degrades_to_a_named_message_when_its_company_feature_is_off()
    {
        // ---- 7.13 / 7.14: the payroll statutory master switch, with the enrolments left in place.
        var payroll = PayrollFixture("Degrade Payroll", gratuity: true, bonus: true);
        var pc = payroll.Company!;
        pc.PayrollStatutoryEnabled = false;

        var gratuity = new ReportsViewModel(pc, ReportKind.GratuityProvisionRegister);
        Assert.Empty(gratuity.PayrollRows);
        Assert.True(gratuity.IsPayrollEmpty);
        Assert.Contains("Payroll Statutory", gratuity.PayrollEmptyNote);
        Assert.Equal(7, gratuity.PayrollColumns.Count);         // captions intact ⇒ egress still has a header

        var bonus = new ReportsViewModel(pc, ReportKind.BonusRegister);
        Assert.Empty(bonus.PayrollRows);
        Assert.Contains("Payroll Statutory", bonus.PayrollEmptyNote);
        Assert.Equal(7, bonus.PayrollColumns.Count);

        // ---- 11.10: F11 → Accounting → Enable Cost Centres, over a book that HAS allocations.
        var cost = FullFixture("Degrade Cost");
        var cc = cost.Company!;
        var populated = new ReportsViewModel(cc, ReportKind.CostCentreBreakup);
        WidenToAllTime(populated, cc);
        Assert.NotEmpty(populated.PayrollRows);                 // the rows exist while the feature is on

        cc.EnableCostCentres = false;
        foreach (var kind in new[] { ReportKind.CostCategorySummary, ReportKind.CostCentreBreakup,
                                     ReportKind.CostCentreLedgerBreakup })
        {
            var off = new ReportsViewModel(cc, kind);
            WidenToAllTime(off, cc);
            Assert.Empty(off.PayrollRows);
            Assert.Contains("Cost Centres are not enabled", off.PayrollEmptyNote);
            Assert.True(off.PayrollColumns.Count >= 2, $"{kind} lost its column band while degraded.");
        }
    }

    // ============================================== the panels that claimed to act (review finding 5, MEDIUM)

    /// <summary>
    /// 🔴 <b>THE THREE F12 DISPLAY KNOBS CANNOT CHANGE A RE-HOMED REPORT, PROVEN BY APPLYING THEM.</b>
    /// Hide-zero-balances, show-percentages and the closing-stock basis are read only by the row-bearing
    /// builders; the matrix surface renders <c>PayrollRows</c>, which nothing filters or percentages. Until this
    /// wave's fix the F12 panel rendered all three unconditionally on every kind, so an operator could tick
    /// "Hide zero balances" on Bills Receivable, press Apply, be told the view updated, and see nothing change.
    ///
    /// <para>The proof is behavioural, not a flag assertion: all three options are applied to a populated report
    /// and the projection must come back identical cell for cell. That is what justifies hiding the controls —
    /// and if a future builder ever DOES honour one of them on a matrix kind, this test reddens and the
    /// <see cref="ReportsViewModel.SupportsDisplayOptions"/> predicate has to be revisited, which is exactly the
    /// notice that should fire.</para>
    ///
    /// <para>🔴 <b>AND THE ANTI-VACUITY GUARD NOW BITES FOR ALL EIGHT CASES (review finding N4).</b> The first
    /// version of this theory snapshotted the COLUMN BAND ahead of the rows, so the snapshot was non-empty at
    /// ZERO rows and <c>Assert.NotEqual(string.Empty, before)</c> passed for the wrong reason. MEASURED by the
    /// review: under <see cref="FullFixture"/>, which deliberately never enables payroll, the gratuity and bonus
    /// cases ran at <c>rows=0</c> — a QUARTER of the theory was comparing a band that
    /// <c>ApplyConfiguration</c> cannot alter under any circumstance, so a future builder that DID start
    /// honouring hide-zero on the gratuity register would not have reddened the test this comment promises will
    /// redden. The snapshot is now ROWS ONLY, the row count is asserted before and after, and the two payroll
    /// registers get a fixture that enrols the statute and hires an employee. All eight cases now compare real
    /// cells.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(RehomedKinds))]
    public void The_three_F12_display_knobs_cannot_change_a_rehomed_report(ReportKind kind)
    {
        var reports = PopulatedRehomedReport($"Dead Knobs {kind}", kind);

        Assert.False(reports.SupportsDisplayOptions,
            $"{kind} claims the F12 display knobs act on it; if that became true the panel must show them again.");
        Assert.False(reports.SupportsHideZeroBalances, $"{kind} claims F12 hide-zero acts on it.");
        Assert.False(reports.SupportsPercentages, $"{kind} claims F12 percentages act on it.");
        Assert.False(reports.SupportsClosingStockBasis, $"{kind} claims the F12 closing-stock basis acts on it.");

        // 🔴 ROWS ONLY — no column band. See the N4 paragraph above: the band is exactly the half
        // ApplyConfiguration could never touch, so including it is what made this comparison vacuous.
        string Snapshot(ReportsViewModel r) => string.Join("|",
            r.PayrollRows.Select(row => string.Join(",", row.Cells.Select(x => x.Text)))
             .Concat(r.PayrollRows2.Select(row => string.Join(",", row.Cells.Select(x => x.Text)))));

        Assert.NotEmpty(reports.PayrollRows);   // ← THE GUARD THAT ACTUALLY BITES: no rows, nothing compared.
        var before = Snapshot(reports);
        Assert.NotEqual(string.Empty, before);

        reports.ApplyConfiguration(hideZero: true, showPercentages: true,
            Apex.Ledger.Reports.ClosingStockMode.InventoryDerived);

        Assert.Equal(before, Snapshot(reports));
        Assert.NotEmpty(reports.PayrollRows);   // and hide-zero did not simply empty the report instead.
    }

    /// <summary>
    /// 🔴 <b>THE ALT+F12 PANEL SAYS IT CANNOT ACT INSTEAD OF ANSWERING "APPLIED — VIEW UPDATED".</b>
    /// <c>SupportsSortFilter</c> has existed on <c>ReportSortFilterViewModel</c> since RQ-3 and was bound
    /// NOWHERE in <c>MainWindow.axaml</c>, while <c>Apply()</c> reported success unconditionally. An operator on
    /// Bills Receivable could type a name filter, apply it, be told it worked, and read a report that was never
    /// filtered — worse than the feature being absent, because they now believe the list is filtered.
    ///
    /// <para>Both directions are asserted: the refusal on a re-homed kind, and a real Trial Balance still
    /// applying and still saying so, because "always refuse" would satisfy half of this and kill the feature.</para>
    /// </summary>
    [Fact]
    public void The_sort_filter_panel_refuses_on_a_rehomed_report_instead_of_claiming_it_applied()
    {
        var vm = FullFixture("Sort Filter Honesty");
        var c = vm.Company!;

        var rehomed = new ReportSortFilterViewModel(new ReportsViewModel(c, ReportKind.ReceivablesOutstanding));
        Assert.True(rehomed.CannotSortOrFilter);
        rehomed.NameContains = "Acme";
        rehomed.Apply();

        Assert.DoesNotContain("Applied", rehomed.Status);
        Assert.Contains("does not act on this report", rehomed.Status);

        // ---- and the panel still works where it is meant to.
        var trialBalance = new ReportSortFilterViewModel(new ReportsViewModel(c, ReportKind.TrialBalance));
        Assert.False(trialBalance.CannotSortOrFilter);
        trialBalance.NameContains = "Rent";
        trialBalance.Apply();

        Assert.Contains("Applied", trialBalance.Status);
    }

    // ============================== closing the CLASS, not the list (review pass 3: N1, N2, N3, N5, N7, N8)

    /// <summary>
    /// 🔴🔴 <b>THE BLOCKER. A SAVED VIEW MUST NOT RENDER THE PAY SHEET ON A COMPANY WHOSE F11 PAYROLL SWITCH IS
    /// OFF — AND THE PAY SHEET IS EVERY EMPLOYEE'S GROSS, DEDUCTIONS AND NET PAY.</b>
    ///
    /// <para>MEASURED by an adversarial review against a fully green gate, on the real view model:
    /// <c>vm.ApplySavedView(view)</c> left <c>screen=Report kind=PaySheet Company.PayrollEnabled=False</c> with
    /// <c>Message</c> empty. The previous pass's saved-view guard enumerated the FIFTEEN kinds whose OPENERS
    /// happened to carry a guard — counted from its arms, not the eighteen the review's prose said — and
    /// permitted the other seventy-eight, while saying in a red-flagged comment that it had closed the door on
    /// every company-feature gate in the product. The Payroll report family has no opener
    /// guard to copy: its ER-13 gate is the conditional <c>col.Add("Payroll Reports")</c> on the MENU ROW. So the
    /// menu group vanished, and Alt+K walked in behind it.</para>
    ///
    /// <para>🔴 <b>THIS TEST IS ON THE REALISED WINDOW AND IT ASSERTS ON PAY, NOT ON A FLAG.</b> The employee's
    /// name and their basic figure must be absent from the rendered visual tree, because a predicate returning
    /// false is not the thing that protects the data — what protects the data is the report not being drawn. The
    /// positive direction is asserted on the same window with the same saved view, or "always refuse" would pass
    /// the confidentiality half and destroy the feature.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_saved_view_cannot_render_the_pay_sheet_when_F11_payroll_is_switched_off()
    {
        var vm = PaidEmployeeFixture("Pay Sheet Saved View", out var c, out var wageMonth);

        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        // ---- Capture an Alt+K saved view off a POPULATED Pay Sheet, exactly as an operator would.
        OpenPaySheetForMonth(vm, wageMonth);
        Pump(window);
        Assert.Contains(vm.Reports!.PayrollRows, r => r.Cells[0].Text == "Zara Mehta");
        var savedView = vm.Reports!.ToSavedView();
        Assert.Contains(Descendants(window).OfType<TextBlock>().Where(t => t.IsEffectivelyVisible),
            t => t.Text == "Zara Mehta");
        vm.Back();
        Pump(window);

        // ---- F11 → Maintain Payroll OFF. The menu group goes with it; the saved view must go with it too.
        vm.ShowGstConfig();
        vm.GstConfig!.PayrollEnabled = false;
        vm.Back();
        Pump(window);
        Assert.False(c.PayrollEnabled);
        vm.ShowGateway();
        Assert.DoesNotContain(vm.Columns[0].Items, i => i.Label == "Payroll Reports");

        vm.ApplySavedView(savedView);
        Pump(window);

        // 🔴 THE ASSERTION THAT MATTERS COMES FIRST, DELIBERATELY: the employee's pay must not be on screen.
        // Asserting the screen enum first would make the mutation report "Expected: Not Report / Actual: Report",
        // which is a navigation complaint. What this test is for is the payroll data, so the payroll data is what
        // the failure message names.
        Assert.DoesNotContain(Descendants(window).OfType<TextBlock>().Where(t => t.IsEffectivelyVisible),
            t => t.Text == "Zara Mehta");
        Assert.DoesNotContain(Descendants(window).OfType<TextBlock>().Where(t => t.IsEffectivelyVisible),
            t => t.Text is { } s && s.Contains("41,000", StringComparison.Ordinal));
        Assert.NotEqual(Screen.Report, vm.CurrentScreen);
        Assert.Contains("switched off", vm.Message);

        // ---- And the gate is not a wall: switch Payroll back on and the same saved view renders again.
        vm.ShowGstConfig();
        vm.GstConfig!.PayrollEnabled = true;
        vm.Back();
        vm.ApplySavedView(savedView);
        Pump(window);

        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.Equal(ReportKind.PaySheet, vm.Reports!.Kind);
        SelectWageMonth(vm.Reports!, wageMonth);
        Pump(window);
        Assert.Contains(Descendants(window).OfType<TextBlock>().Where(t => t.IsEffectivelyVisible),
            t => t.Text == "Zara Mehta");

        window.Close();
    }

    /// <summary>
    /// 🔴 <b>EVERY <see cref="ReportKind"/> CARRIES AN EXPLICIT ER-13 DECISION. THIS IS THE TEST THAT MAKES THE
    /// CLASS STAY CLOSED.</b>
    ///
    /// <para>The predicate's predecessor was an enumeration of exceptions over a <c>_ => true</c> default, so a
    /// report kind added later was ungated by DEFAULT and nothing anywhere noticed. The decision table is now
    /// total and the default is DENY, which means a new kind arrives unreachable-by-saved-view rather than
    /// unguarded — and this test fails naming it, so the developer who added it is the one who decides its gate
    /// rather than an operator discovering it.</para>
    ///
    /// <para>Fails on today's main and on the previous two passes of this branch: there was no table to be total.</para>
    /// </summary>
    [Fact]
    public void Every_report_kind_carries_a_saved_view_gate_decision()
    {
        var undecided = Enum.GetValues<ReportKind>()
            .Where(k => MainWindowViewModel.SavedViewGateFor(k) is null)
            .ToList();

        Assert.True(undecided.Count == 0,
            "These ReportKind members have no ER-13 gate decision in MainWindowViewModel.ReportKindGates, so a "
            + "saved view of them is REFUSED (deny by default) and nobody has said whether that is right: "
            + string.Join(", ", undecided));

        // And the table has no entry for a kind that no longer exists — a stale row would quietly gate nothing.
        Assert.Equal(Enum.GetValues<ReportKind>().Length,
            Enum.GetValues<ReportKind>().Count(k => MainWindowViewModel.SavedViewGateFor(k) is not null));
    }

    /// <summary>
    /// 🔴 <b>THE SAVED-VIEW DOOR AND THE MENU ROW ANSWER THE SAME QUESTION, CHECKED FAMILY BY FAMILY ON A FRESH
    /// COMPANY.</b> A fresh company has Payroll, Payroll Statutory, TDS, TCS, VAT, batch-wise details, price
    /// levels, tracking numbers, cost tracking, job costing and job order processing all OFF, so every menu row
    /// below is absent from the real cascade. The saved view for each of those families must be absent too.
    ///
    /// <para>One representative kind per gated family, so the test names the family that broke rather than
    /// failing ninety times. The kinds NOT listed here are the <c>None</c> decisions, and the totality test above
    /// is what stops a new kind slipping between the two lists.</para>
    /// </summary>
    [Theory]
    [InlineData(ReportKind.PaySheet, ReportFeatureGate.Payroll)]
    [InlineData(ReportKind.AttendanceSheet, ReportFeatureGate.Payroll)]
    [InlineData(ReportKind.PfForm3A, ReportFeatureGate.PayrollStatutory)]
    [InlineData(ReportKind.IncomeTaxComputation, ReportFeatureGate.PayrollStatutory)]
    [InlineData(ReportKind.TdsOutstanding, ReportFeatureGate.Tds)]
    [InlineData(ReportKind.TcsOutstanding, ReportFeatureGate.Tcs)]
    [InlineData(ReportKind.LedgersWithoutPan, ReportFeatureGate.TdsOrTcs)]
    [InlineData(ReportKind.VatComputation, ReportFeatureGate.Vat)]
    [InlineData(ReportKind.Batchwise, ReportFeatureGate.Batchwise)]
    [InlineData(ReportKind.PriceList, ReportFeatureGate.PriceLevels)]
    [InlineData(ReportKind.PosRegister, ReportFeatureGate.PosSales)]
    [InlineData(ReportKind.PurchaseBillsPending, ReportFeatureGate.TrackingNumbers)]
    [InlineData(ReportKind.StockItemCostAnalysis, ReportFeatureGate.CostTracking)]
    [InlineData(ReportKind.JobWorkAnalysis, ReportFeatureGate.JobCosting)]
    [InlineData(ReportKind.MaterialInRegister, ReportFeatureGate.JobOrderProcessing)]
    [InlineData(ReportKind.GratuityProvisionRegister, ReportFeatureGate.GratuityEnrolment)]
    [InlineData(ReportKind.BonusRegister, ReportFeatureGate.BonusEnrolment)]
    public void A_saved_view_refuses_a_report_family_this_company_has_not_switched_on(
        ReportKind kind, ReportFeatureGate expectedGate)
    {
        Assert.Equal(expectedGate, MainWindowViewModel.SavedViewGateFor(kind));

        var vm = NewCompany($"Gate {kind}");
        var view = new Apex.Ledger.Reports.SavedReportView
        {
            ReportKind = ReportsViewModel.TokenFor(kind),
            AsOfDate = vm.Company!.FinancialYearStart.AddMonths(1),
        };

        vm.ApplySavedView(view);

        Assert.NotEqual(Screen.Report, vm.CurrentScreen);
        Assert.Null(vm.Reports);
        Assert.Contains("switched off", vm.Message);
    }

    /// <summary>
    /// 🔴 <b>NO <see cref="ReportFeatureGate"/> MEMBER IS AN ORPHAN.</b> A gate member that no kind maps to is a
    /// gate nobody can reach, and a gate member is exactly the kind of thing that gets added for a family and
    /// then wired to nothing. Paired with the theory above — which drives every non-<c>None</c> arm of
    /// <c>ReportFeatureGateIsOpen</c> through a company that has the feature off — this is what makes the
    /// enum's own coverage a measurement rather than a claim.
    /// </summary>
    [Fact]
    public void Every_report_feature_gate_member_is_reached_by_a_report_kind()
    {
        var used = Enum.GetValues<ReportKind>()
            .Select(MainWindowViewModel.SavedViewGateFor)
            .Where(g => g is not null)
            .Select(g => g!.Value)
            .ToHashSet();

        var orphans = Enum.GetValues<ReportFeatureGate>().Where(g => !used.Contains(g)).ToList();

        Assert.True(orphans.Count == 0,
            "These ReportFeatureGate members are mapped to by no ReportKind at all, so nothing evaluates them "
            + "and nobody would notice if their arm were wrong: " + string.Join(", ", orphans));
    }

    /// <summary>
    /// 🔴 <b>THE COST-CENTRE ARM, WHICH THE FAMILY THEORY ABOVE CANNOT COVER.</b> Every other gated family is
    /// OFF on a fresh company, so the theory gets them for free. <c>EnableCostCentres</c> is the one flag whose
    /// shipped convention is that an unset company is <b>ON</b> (<c>!= false</c>, not <c>== true</c>), so its
    /// arm has to be driven by switching the feature off deliberately — and without this test that arm of
    /// <c>ReportFeatureGateIsOpen</c> would be the only one nothing exercises.
    ///
    /// <para>Both directions, and the MENU is asserted alongside the saved view, because the point of deriving
    /// the gate from <c>CostCentresFeatureOn</c> is that those two answers cannot differ.</para>
    /// </summary>
    [Fact]
    public void A_saved_view_refuses_a_cost_centre_report_when_the_feature_is_switched_off()
    {
        var vm = FullFixture("Cost Centre Saved View");
        var c = vm.Company!;

        vm.OpenReport(ReportKind.CostCentreBreakup);
        var savedView = vm.Reports!.ToSavedView();
        vm.Back();

        c.EnableCostCentres = false;

        // The menu row goes …
        vm.ShowGateway();
        ArrowToAndDrill(vm, "Statements of Accounts");
        Assert.DoesNotContain(vm.Columns[^1].Items, i => i.Label == "Cost Centres");

        // … and so does the saved view.
        vm.ApplySavedView(savedView);
        Assert.NotEqual(Screen.Report, vm.CurrentScreen);
        Assert.Contains("switched off", vm.Message);

        // Not a wall: switch the feature back on and the same saved view opens.
        c.EnableCostCentres = true;
        vm.ApplySavedView(savedView);
        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.Equal(ReportKind.CostCentreBreakup, vm.Reports!.Kind);
    }

    /// <summary>
    /// 🔴 <b>THE PF / ESI HALF OF THE GATE, WHICH UNTIL NOW WAS ASSERTED ONLY BY A COMMENT (review finding N5).</b>
    /// The previous pass deliberately widened the guard beyond this wave to cover the ten payroll-statutory kinds
    /// and said so in a red-flagged comment, while every test exercised only the Gratuity register. A later
    /// refactor could have trimmed that arm and the suite would have stayed green.
    ///
    /// <para>Both directions, on a company that HAS the statutory enrolments, so the refusal cannot be an
    /// accident of an empty company: the form refuses while F11 → Payroll Statutory is off, and the SAME saved
    /// view opens once it is back on.</para>
    /// </summary>
    [Fact]
    public void A_saved_view_cannot_reopen_a_payroll_statutory_form_while_the_statute_is_off()
    {
        var vm = PayrollFixture("Pf Form Saved View", gratuity: false, bonus: false);

        vm.OpenPayrollStatutoryForm(ReportKind.PfForm3A);
        Assert.Equal(Screen.Report, vm.CurrentScreen);
        var savedView = vm.Reports!.ToSavedView();
        vm.Back();

        SetPayrollStatutory(vm, false);

        vm.OpenPayrollStatutoryForm(ReportKind.PfForm3A);        // the gated opener: refuses, as it always did
        Assert.NotEqual(Screen.Report, vm.CurrentScreen);

        vm.ApplySavedView(savedView);                            // the second door: must refuse too
        Assert.NotEqual(Screen.Report, vm.CurrentScreen);
        Assert.Contains("switched off", vm.Message);

        SetPayrollStatutory(vm, true);
        vm.ApplySavedView(savedView);
        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.Equal(ReportKind.PfForm3A, vm.Reports!.Kind);
    }

    /// <summary>
    /// 🔴 <b>THE THIRD DOOR. GO TO (ALT+G) MUST NOT REACH A REPORT THE COMPANY HAS SWITCHED OFF EITHER — AND
    /// THIS IS THE PATTERN SWEEP OF THE CLASS, TURNED INTO A MEASUREMENT.</b>
    ///
    /// <para>Two doors into a <see cref="ReportKind"/> were counted and closed above: the MENU row (an inline
    /// feature condition in a builder) and the SAVED VIEW (<see cref="MainWindowViewModel.ApplySavedView"/>, the
    /// hole this pass fixed). A grep for the pattern found exactly one more surface that navigates to a report
    /// by NAME rather than by walking the cascade — the Go To overlay — plus exactly two
    /// <c>OpenReport(kind)</c> call sites taking a variable rather than a literal
    /// (<c>OpenPayrollStatutoryForm</c>, gated, and <c>ApplySavedView</c>, now gated). The previous review
    /// asserted Go To was safe because it REPLAYS menu paths and so inherits the menu gates. That is true by
    /// construction — <c>ActivateGoTo</c> re-resolves each Group row in the rebuilt column and bails when one is
    /// gone — but on this branch "true by reading" has been wrong three times, so it is measured here.</para>
    ///
    /// <para>Both halves: the destination must be ABSENT from the index while the feature is off (so it cannot be
    /// typed at all), and PRESENT once the feature is back on (so the sweep has not simply proven Go To is
    /// broken). Payroll for the family whose exposure was the blocker, and Cost Centres because it is the one
    /// flag whose unset default is ON.</para>
    /// </summary>
    [Fact]
    public void The_go_to_overlay_cannot_reach_a_report_family_the_company_has_switched_off()
    {
        var vm = PayrollFixture("Go To Gate", gratuity: false, bonus: false);
        var c = vm.Company!;

        // ---- Payroll ON: the Pay Sheet is findable by name, which is what makes the negative half meaningful.
        vm.OpenGoTo();
        vm.GoTo!.SearchText = "Pay Sheet";
        Assert.Contains(vm.GoTo!.Results, r => r.Label == "Pay Sheet");
        vm.CloseGoTo();

        // ---- F11 → Maintain Payroll OFF through the real page. The whole family must leave the index with the
        // menu group, because Go To builds its index by WALKING the real menu.
        vm.ShowGstConfig();
        vm.GstConfig!.PayrollEnabled = false;
        vm.Back();
        Assert.False(c.PayrollEnabled);

        vm.OpenGoTo();
        vm.GoTo!.SearchText = "Pay Sheet";
        Assert.DoesNotContain(vm.GoTo!.Results, r => r.Label == "Pay Sheet");
        Assert.Empty(vm.GoTo!.AllDestinations.Where(d => d.Label == "Pay Sheet"));
        Assert.False(vm.ActivateGoTo() && vm.CurrentScreen == Screen.Report,
            "Go To jumped to a report on a company that has the feature switched off.");
        vm.CloseGoTo();
        Assert.NotEqual(Screen.Report, vm.CurrentScreen);

        // ---- and the cost-centre flag, whose unset default is ON, so it has to be switched off deliberately.
        vm.OpenGoTo();
        vm.GoTo!.SearchText = "Cost Centre Break-up";
        var hadCostCentre = vm.GoTo!.AllDestinations.Any(d => d.Label.Contains("Cost Centre", StringComparison.Ordinal));
        vm.CloseGoTo();
        Assert.True(hadCostCentre, "No Cost Centre destination in the Go To index at all — the sweep proves nothing.");

        c.EnableCostCentres = false;
        vm.OpenGoTo();
        Assert.DoesNotContain(vm.GoTo!.AllDestinations,
            d => d.Label.Contains("Cost Centre", StringComparison.Ordinal));
        vm.CloseGoTo();
    }

    /// <summary>
    /// 🔴🔴 <b>BARE ENTER ON THE E-PAYMENTS REPORT MUST NOT WRITE A BANK PAYMENT-INSTRUCTION FILE (review
    /// finding N2).</b> This is the OTHER half of the class the <c>viaAcceptChord</c> parameter was introduced to
    /// close. The previous pass guarded the gratuity arm three lines below this one, wrote a comment naming
    /// "any <c>case Screen.Report when …</c> arm below that WRITES", and left this arm untouched.
    ///
    /// <para>MEASURED by the review on a shown window with a real Enter press: <c>EPaymentsExportStatus</c> went
    /// from empty to the arm's own "Nothing is ready to send…" message, i.e. the arm RAN and only an early
    /// return stopped the file. This fixture makes the report READY — a complete bank master, a complete
    /// beneficiary and a posted NEFT payment — so nothing but the guard stands between Enter and the file.</para>
    ///
    /// <para>Both gestures on the same window: Enter must leave the export folder untouched and say which key
    /// exports; Ctrl+A must still write the file, or the fix is the feature removed.</para>
    /// </summary>
    [AvaloniaFact]
    public void Bare_Enter_on_the_e_payments_report_writes_no_file_but_Ctrl_A_still_does()
    {
        var vm = ReadyEPaymentsFixture("EPay Enter Guard", out var company);
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        vm.OpenReport(ReportKind.EPayments);
        Pump(window);
        vm.Reports!.SetPeriod(company.BooksBeginFrom, company.BooksBeginFrom.AddYears(1));
        Pump(window);

        var report = vm.Reports!;
        Assert.NotEmpty(report.Rows);
        Assert.Contains(report.Rows,
            r => r.Particulars.Contains("Ready for Sending to Bank", StringComparison.Ordinal));

        var folder = ExportFolderDefault.Resolve();
        var expected = Path.Combine(folder,
            $"PaymentInstructions-{company.BooksBeginFrom:yyyyMMdd}-{company.BooksBeginFrom.AddYears(1):yyyyMMdd}.csv");
        if (File.Exists(expected)) File.Delete(expected);        // a stale file from another run proves nothing

        try
        {
            // ---- the drill key. It must not export.
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.False(File.Exists(expected),
                "A bare Enter on the e-Payments report wrote the bank payment-instruction file. Enter is the "
                + "DRILL key on every report in this product; an operator aiming at a drill must not produce a "
                + "payment file.");
            Assert.DoesNotContain("Saved", report.EPaymentsExportStatus);
            Assert.Contains("Press Ctrl+A", report.EPaymentsExportStatus);

            // ---- and the accept chord on the same window still exports.
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);

            Assert.True(File.Exists(expected),
                "Ctrl+A no longer exports the payment instruction file. Status was: " + report.EPaymentsExportStatus);
            Assert.Contains("Saved", report.EPaymentsExportStatus);
            Assert.Contains("Acme Supplies", File.ReadAllText(expected));
        }
        finally
        {
            try { if (File.Exists(expected)) File.Delete(expected); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            window.Close();
        }
    }

    /// <summary>
    /// 🔴 <b>EACH F12 DISPLAY KNOB IS OFFERED ONLY WHERE ITS OWN BUILDER READS IT (review finding N3).</b>
    ///
    /// <para>The previous pass gated all three on <c>!IsPayrollMatrix</c> and defended it in a comment saying the
    /// three options "are consumed in exactly one place — the row-bearing builders". MEASURED, that is wrong in
    /// BOTH directions. Day Book is row-bearing and honours none of the three — a posted voucher's row comes back
    /// byte-identical after hide-zero + percentages + a closing-stock basis — and so does every register, GSTR-1,
    /// GSTR-3B and Statistics, about eighty kinds in all, on which the panel still showed all three. Meanwhile
    /// the Attendance Sheet DOES honour hide-zero (<c>BuildAttendanceSheet</c> passes
    /// <c>_options.HideZeroBalances</c> straight into <c>Report.BuildAttendanceSheet</c> as the vendor's own
    /// "Remove zero-valued transactions") and the matrix predicate HID it there.</para>
    ///
    /// <para>The inertness half is behavioural, over a report with ROWS in it — not a flag assertion and not a
    /// snapshot that leads with a column band that <c>ApplyConfiguration</c> could never alter anyway.</para>
    /// </summary>
    [Fact]
    public void The_F12_display_knobs_are_offered_only_where_the_builder_reads_them()
    {
        var vm = FullFixture("Knob Scope");
        var c = vm.Company!;

        // ---- Day Book: row-bearing, honours NONE of the three. The defect the old predicate left open.
        var dayBook = new ReportsViewModel(c, ReportKind.DayBook);
        WidenToAllTime(dayBook, c);
        Assert.NotEmpty(dayBook.Rows);                          // rows, so the comparison below is not vacuous
        Assert.False(dayBook.SupportsHideZeroBalances);
        Assert.False(dayBook.SupportsPercentages);
        Assert.False(dayBook.SupportsClosingStockBasis);
        Assert.False(dayBook.SupportsDisplayOptions);

        var before = RowText(dayBook);
        Assert.NotEqual(string.Empty, before);
        dayBook.ApplyConfiguration(hideZero: true, showPercentages: true,
            Apex.Ledger.Reports.ClosingStockMode.InventoryDerived);
        Assert.Equal(before, RowText(dayBook));

        // ---- Trial Balance: hide-zero and percentages act; the closing-stock basis does NOT (only BS and P&L
        // read it, through ReportOptions).
        var trialBalance = new ReportsViewModel(c, ReportKind.TrialBalance);
        Assert.True(trialBalance.SupportsHideZeroBalances);
        Assert.True(trialBalance.SupportsPercentages);
        Assert.False(trialBalance.SupportsClosingStockBasis);

        // ---- Balance Sheet / P&L: all three.
        foreach (var kind in new[] { ReportKind.BalanceSheet, ReportKind.ProfitAndLoss })
        {
            var r = new ReportsViewModel(c, kind);
            Assert.True(r.SupportsHideZeroBalances, $"{kind} lost hide-zero.");
            Assert.True(r.SupportsPercentages, $"{kind} lost percentages.");
            Assert.True(r.SupportsClosingStockBasis, $"{kind} lost the closing-stock basis.");
        }

        // ---- Stock Summary: hide-zero and percentages, but it values stock directly and ignores the basis.
        var stock = new ReportsViewModel(c, ReportKind.StockSummary);
        Assert.True(stock.SupportsHideZeroBalances);
        Assert.True(stock.SupportsPercentages);
        Assert.False(stock.SupportsClosingStockBasis);

        // ---- Attendance Sheet: hide-zero is LIVE here and the matrix predicate hid it. Percentages and the
        // closing-stock basis are not.
        var payroll = PayrollFixture("Knob Scope Payroll", gratuity: false, bonus: false);
        var attendance = new ReportsViewModel(payroll.Company!, ReportKind.AttendanceSheet);
        Assert.True(attendance.SupportsHideZeroBalances,
            "The Attendance Sheet honours F12 hide-zero (the vendor's 'Remove zero-valued transactions'); "
            + "hiding the knob there is the same defect as showing a dead one, one direction over.");
        Assert.False(attendance.SupportsPercentages);
        Assert.False(attendance.SupportsClosingStockBasis);
        Assert.True(attendance.SupportsDisplayOptions);          // the section heading must still appear
    }

    /// <summary>
    /// 🔴 <b>THE F12 PANEL RENDERS ONLY THE KNOBS THAT ACT, PROVEN ON THE VISUAL TREE (review finding N8).</b>
    /// The two visibility bindings added by the previous pass were asserted at view-model level only, on a branch
    /// whose filed failure mode is precisely a knob that renders and does nothing. A binding that resolves is not
    /// a control that left the panel.
    /// </summary>
    [AvaloniaFact]
    public void The_F12_panel_renders_only_the_display_knobs_that_act_on_the_open_report()
    {
        var vm = FullFixture("F12 Visual Tree");
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 1000 };
        window.Show();
        Pump(window);

        // ---- A Trial Balance: hide-zero and percentages are on screen, the closing-stock row is not.
        vm.OpenReport(ReportKind.TrialBalance);
        vm.OpenReportConfig();
        Pump(window);
        Assert.True(VisibleCheckBox(window, "Hide zero balances"));
        Assert.True(VisibleCheckBox(window, "Show percentages (of section/column total)"));
        Assert.False(VisibleLabel(window, "Closing stock"));
        vm.Back();
        vm.Back();
        Pump(window);

        // ---- A Balance Sheet: all three, including the closing-stock basis.
        vm.OpenReport(ReportKind.BalanceSheet);
        vm.OpenReportConfig();
        Pump(window);
        Assert.True(VisibleCheckBox(window, "Hide zero balances"));
        Assert.True(VisibleLabel(window, "Closing stock"));
        vm.Back();
        vm.Back();
        Pump(window);

        // ---- Bills Receivable, a re-homed matrix report: none of the three, and no "Display" heading standing
        // over nothing.
        vm.OpenReport(ReportKind.ReceivablesOutstanding);
        vm.OpenReportConfig();
        Pump(window);
        Assert.False(VisibleCheckBox(window, "Hide zero balances"));
        Assert.False(VisibleCheckBox(window, "Show percentages (of section/column total)"));
        Assert.False(VisibleLabel(window, "Closing stock"));
        Assert.False(VisibleLabel(window, "Display"));
        // The panel is genuinely open — otherwise every assertion above passes for the wrong reason.
        Assert.NotNull(vm.ReportConfig);
        Assert.True(VisibleCheckBox(window, "Detailed (ledger/item-level; unchecked = group summary)")
                    || VisibleLabel(window, "Configure — F12"));

        // ---- Day Book: row-bearing, and the knobs must be gone there too. This is the case the matrix
        // predicate could never have covered.
        vm.Back();
        vm.Back();
        vm.OpenReport(ReportKind.DayBook);
        vm.OpenReportConfig();
        Pump(window);
        Assert.False(VisibleCheckBox(window, "Hide zero balances"));
        Assert.False(VisibleLabel(window, "Display"));

        window.Close();
    }

    /// <summary>
    /// 🔴 <b>THE ALT+F12 PANEL DISABLES THE CONTROLS IT CANNOT ACT WITH, PROVEN ON THE VISUAL TREE (review
    /// findings N7 and N8).</b> The banner plus the refusal in <c>Apply()</c> was only half the fix: the sort
    /// combo, the Ascending box, Min, Max, "Name has" and Apply were all still live on a report the panel cannot
    /// touch, so an operator could type a filter into an enabled box, press an enabled Apply, and learn only
    /// afterwards that nothing happened. A control that cannot do anything must not accept input.
    ///
    /// <para>Both directions, and the banner's own visibility is checked on the rendered tree rather than through
    /// its view-model flag.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_sort_filter_panel_disables_the_controls_it_cannot_act_with()
    {
        var vm = FullFixture("Alt F12 Visual Tree");
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 1000 };
        window.Show();
        Pump(window);

        // ---- a re-homed report: the banner is drawn and Apply is dead.
        vm.OpenReport(ReportKind.ReceivablesOutstanding);
        vm.OpenReportSortFilter();
        Pump(window);

        Assert.NotNull(vm.ReportSortFilter);
        Assert.Contains(Descendants(window).OfType<TextBlock>().Where(t => t.IsEffectivelyVisible),
            t => t.Text is { } s && s.Contains("does not act on this report", StringComparison.Ordinal));

        var apply = ApplyButton(window);
        Assert.False(apply.IsEffectivelyEnabled,
            "The Alt+F12 Apply button is live on a report the panel cannot sort or filter.");
        Assert.All(FilterInputs(window), box => Assert.False(box.IsEffectivelyEnabled,
            "An Alt+F12 filter box accepts typing on a report the panel cannot act on."));

        vm.Back();
        vm.Back();
        Pump(window);

        // ---- a Trial Balance: no banner, and every control live.
        vm.OpenReport(ReportKind.TrialBalance);
        vm.OpenReportSortFilter();
        Pump(window);

        Assert.DoesNotContain(Descendants(window).OfType<TextBlock>().Where(t => t.IsEffectivelyVisible),
            t => t.Text is { } s && s.Contains("does not act on this report", StringComparison.Ordinal));
        Assert.True(ApplyButton(window).IsEffectivelyEnabled,
            "The Alt+F12 Apply button is dead on a Trial Balance, which the panel does act on.");
        Assert.All(FilterInputs(window), box => Assert.True(box.IsEffectivelyEnabled));

        window.Close();
    }

    // ================================================================= harness

    /// <summary>A gratuity-enrolled company with ONE employee whose accrued provision is the golden ₹1,50,000
    /// at the returned as-on date — the same figure the branch's own Ctrl+A test uses, so a voucher appearing
    /// where none should is unmistakable.</summary>
    private MainWindowViewModel GratuityFixtureWithOneEmployee(string name, out Company company, out DateOnly asOn)
    {
        var vm = PayrollFixture(name, gratuity: true, bonus: false);
        var c = vm.Company!;
        var basic = CreateBasicHead(c);
        var groupId = new PayrollService(c).CreateEmployeeGroup("Staff").Id;
        AddEmployee(c, basic, groupId, "Anil Rao", "E001", c.FinancialYearStart.AddYears(-10).AddDays(29), 26_000m);
        _storage.Save(c);

        company = c;
        asOn = c.FinancialYearStart.AddMonths(1).AddDays(-1);
        return vm;
    }

    /// <summary>Flips F11 → Payroll Statutory through the real configuration page, which is what leaves the
    /// enrolment configs standing — the state the saved-view bypass lived in.</summary>
    private static void SetPayrollStatutory(MainWindowViewModel vm, bool on)
    {
        vm.ShowGstConfig();
        vm.GstConfig!.PayrollStatutoryEnabled = on;
        vm.Back();
        Assert.Equal(on, vm.Company!.PayrollStatutoryEnabled);
    }

    /// <summary>Walks the cascade with the arrow keys down <paramref name="path"/> and asserts the leaf opened
    /// the expected <see cref="ReportKind"/> on <see cref="Screen.Report"/> — not a page Screen.</summary>
    private static void AssertRouteOpensReport(MainWindowViewModel vm, ReportKind expected, params string[] path)
    {
        // No "Reports" step: on the root Gateway column "Reports" is a section HEADER, not a drillable row —
        // the report groups sit directly under it and are what the arrows land on.
        vm.ShowGateway();
        foreach (var label in path) ArrowToAndDrill(vm, label);

        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.NotNull(vm.Reports);
        Assert.Equal(expected, vm.Reports!.Kind);
    }

    /// <summary>Arrows down the ACTIVE column until the highlighted row carries this label, then drills in —
    /// the exact sequence an operator's fingers perform. Fails loudly when the row is not arrow-reachable,
    /// which is the failure a "does the view model have the method" test cannot see.</summary>
    private static void ArrowToAndDrill(MainWindowViewModel vm, string label)
    {
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            if (vm.Menu[vm.SelectedIndex].Label == label) { vm.DrillIn(); return; }
            vm.MoveDown();
        }
        Assert.Fail($"'{label}' was not reachable by arrow navigation in the active column.");
    }

    /// <summary>Opens the report's window wide enough that a fixture voucher is inside it whatever the
    /// company's financial year happens to be.</summary>
    private static void WidenToAllTime(ReportsViewModel reports, Company c) =>
        reports.SetPeriod(c.BooksBeginFrom.AddYears(-2), c.FinancialYearStart.AddYears(3));

    private MainWindowViewModel NewCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name + " " + Guid.NewGuid().ToString("N")[..6];
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    /// <summary>
    /// A company carrying data for ALL of 11.9, 11.10 and 11.11 at once: an open sales bill (so Outstandings has
    /// a row), a cost category + centre with an allocated expense (so all three cost reports have rows), and a
    /// budget with one line (so Budget Variance has a section). Built once so the parameterised tests above
    /// exercise every kind against a populated book rather than an empty one — an empty report passes a
    /// caption test for the wrong reason.
    /// </summary>
    private MainWindowViewModel FullFixture(string name)
    {
        var vm = NewCompany(name);
        var c = vm.Company!;
        var svc = new LedgerService(c);
        var journal = c.FindVoucherTypeByName("Journal")!;
        var cash = Ledger(c, "Cash", "Cash-in-Hand");
        var date = c.FinancialYearStart.AddMonths(1);

        // ---- 11.9: a bill-by-bill debtor with one open invoice.
        var debtor = new DomainLedger(Guid.NewGuid(), "Acme Traders", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true) { MaintainBillByBill = true };
        c.AddLedger(debtor);
        var sales = Ledger(c, "Sales", "Sales Accounts");

        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, date, new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(10_000m), DrCr.Debit,
                billAllocations: new[] { new BillAllocation(BillRefType.NewRef, "INV-1", Money.FromRupees(10_000m)) }),
            new EntryLine(sales.Id, Money.FromRupees(10_000m), DrCr.Credit),
        }));

        // ---- 11.10: a cost centre under the seeded Primary Cost Category, with an expense allocated to it.
        var category = c.FindCostCategoryByName("Primary Cost Category")!;
        var centre = new CostCentre(Guid.NewGuid(), "Head Office", category.Id);
        c.AddCostCentre(centre);
        var rent = Ledger(c, "Rent", "Indirect Expenses");

        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, date, new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(5_000m), DrCr.Debit, billAllocations: null,
                costAllocations: new[] { new CostAllocation(category.Id, centre.Id, Money.FromRupees(5_000m)) }),
            new EntryLine(cash.Id, Money.FromRupees(5_000m), DrCr.Credit),
        }));

        // ---- 11.11: one budget with one ledger line.
        var budget = new Budget(Guid.NewGuid(), "Annual Opex",
            c.FinancialYearStart, c.FinancialYearStart.AddYears(1).AddDays(-1));
        budget.AddLine(BudgetLine.ForLedger(rent.Id, BudgetType.OnNettTransactions, Money.FromRupees(60_000m)));
        c.AddBudget(budget);

        _storage.Save(c);
        return vm;
    }

    /// <summary>A payroll company with the statutory switches on and the two enrolments chosen per test.</summary>
    private MainWindowViewModel PayrollFixture(string name, bool gratuity, bool bonus)
    {
        var vm = NewCompany(name);
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.PayrollEnabled = true;
        page.PayrollStatutoryEnabled = true;
        vm.Back();

        var c = vm.Company!;
        if (gratuity) new PayrollService(c).EnableGratuity();
        if (bonus) new PayrollService(c).EnableStatutoryBonus();
        _storage.Save(c);
        return vm;
    }

    /// <summary>The named ledger, created under <paramref name="groupName"/> if the fresh company did not seed
    /// it. Tolerating both is deliberate: which ledgers a new company seeds is not this wave's contract, and a
    /// fixture that asserts it would fail for a reason that has nothing to do with the re-home.</summary>
    private static DomainLedger Ledger(Company c, string name, string groupName)
    {
        if (c.FindLedgerByName(name) is { } existing) return existing;
        var created = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(created);
        return created;
    }

    private static Guid IndirectExpenses(Company c) => c.FindGroupByName("Indirect Expenses")!.Id;

    private static Guid CreateBasicHead(Company c) =>
        new PayHeadService(c).CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c), useForGratuity: true).Id;

    private static Guid AddEmployee(Company c, Guid basicHeadId, Guid groupId, string name, string? number,
        DateOnly doj, decimal basicDa)
    {
        var pay = new PayrollService(c);
        var e = pay.CreateEmployee(name, groupId, employeeNumber: number);
        c.FindEmployee(e.Id)!.DateOfJoining = doj;
        new SalaryStructureService(c).DefineForEmployee(e.Id, c.FinancialYearStart,
            new[] { new SalaryStructureLine(basicHeadId, 0, new Money(basicDa)) });
        return e.Id;
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    /// <summary>The report's ROW text only — no column band. The band is what
    /// <c>ApplyConfiguration</c> could never alter under any circumstance, so a snapshot that includes it is
    /// non-empty at zero rows and its own anti-vacuity guard stops biting.</summary>
    private static string RowText(ReportsViewModel r) => string.Join("|",
        r.Rows.Select(row => string.Join(",", row.Particulars, row.Secondary, row.Amount, row.Debit, row.Credit)));

    /// <summary>
    /// A re-homed report of <paramref name="kind"/> that actually HAS ROWS IN IT (review finding N4).
    ///
    /// <para>The six accounting kinds are populated by <see cref="FullFixture"/>, which seeds the bills, cost
    /// allocations and budget this wave's reports read. The two payroll REGISTERS are not: <c>FullFixture</c>
    /// deliberately never enables payroll, so both of them come back through this wave's own
    /// <c>PayrollStatutoryEnabled</c> degrade at zero rows — which is correct behaviour and a useless fixture
    /// for a test that has to compare cells. They therefore get the statute switched on, the enrolment created
    /// and one employee hired, exactly as the golden-figure tests above do.</para>
    /// </summary>
    private ReportsViewModel PopulatedRehomedReport(string name, ReportKind kind)
    {
        if (kind is ReportKind.GratuityProvisionRegister or ReportKind.BonusRegister)
        {
            var wantsGratuity = kind == ReportKind.GratuityProvisionRegister;
            var payrollVm = PayrollFixture(name, gratuity: wantsGratuity, bonus: !wantsGratuity);
            var pc = payrollVm.Company!;
            var basic = CreateBasicHead(pc);
            var groupId = new PayrollService(pc).CreateEmployeeGroup("Staff").Id;
            AddEmployee(pc, basic, groupId, "Bina Roy", "B001", pc.FinancialYearStart.AddYears(-3), 18_000m);
            _storage.Save(pc);

            var register = new ReportsViewModel(pc, kind);
            register.SetAsOf(pc.FinancialYearStart.AddYears(1).AddDays(-1));
            return register;
        }

        var vm = FullFixture(name);
        var reports = new ReportsViewModel(vm.Company!, kind);
        WidenToAllTime(reports, vm.Company!);
        return reports;
    }

    /// <summary>True when a <see cref="CheckBox"/> with this exact content is laid out and visible.</summary>
    private static bool VisibleCheckBox(Visual root, string content) =>
        Descendants(root).OfType<CheckBox>()
            .Any(cb => cb.IsEffectivelyVisible && cb.Content as string == content);

    /// <summary>True when a <see cref="TextBlock"/> carrying this exact text is laid out and visible.</summary>
    private static bool VisibleLabel(Visual root, string text) =>
        Descendants(root).OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text == text);

    /// <summary>The Alt+F12 panel's Apply button, found by its own caption so the test breaks loudly if the
    /// template is restructured rather than passing over a control that is no longer there.</summary>
    private static Button ApplyButton(Visual root) =>
        Assert.Single(Descendants(root).OfType<Button>()
            .Where(b => b.IsEffectivelyVisible && b.Content as string == "Apply (Ctrl+A)"));

    /// <summary>The Alt+F12 panel's three filter text boxes, found by their placeholders.</summary>
    private static IReadOnlyList<TextBox> FilterInputs(Visual root)
    {
        var boxes = Descendants(root).OfType<TextBox>()
            .Where(b => b.IsEffectivelyVisible && b.PlaceholderText is
                "rupees (blank = no lower bound)" or "rupees (blank = no upper bound)" or "substring (blank = all)")
            .ToList();
        Assert.Equal(3, boxes.Count);
        return boxes;
    }

    /// <summary>
    /// A payroll company with ONE employee whose salary run for the FY-start wage month is POSTED, so the Pay
    /// Sheet has a real row in it. The posted voucher is what matters: the payroll presentation reports project
    /// the POSTED run, not the salary structure, so a company with a structure and no run renders "No employees
    /// with salary for this wage month" and a confidentiality test over it would pass for the wrong reason
    /// (measured — that is exactly what the first draft of this fixture did).
    /// </summary>
    private MainWindowViewModel PaidEmployeeFixture(string name, out Company company, out DateOnly wageMonth)
    {
        var vm = PayrollFixture(name, gratuity: false, bonus: false);
        var c = vm.Company!;
        var basic = CreateBasicHead(c);
        var groupId = new PayrollService(c).CreateEmployeeGroup("Staff").Id;
        var month = new DateOnly(c.FinancialYearStart.Year, c.FinancialYearStart.Month, 1);
        var monthTo = month.AddMonths(1).AddDays(-1);

        var empId = AddEmployee(c, basic, groupId, "Zara Mehta", "Z001",
            c.FinancialYearStart.AddYears(-2), 41_000m);
        new PayrollVoucherService(c).Post(month, monthTo, new[] { empId });

        _storage.Save(c);
        company = c;
        wageMonth = month;
        return vm;
    }

    /// <summary>Opens the Pay Sheet and scopes it to the posted wage month, as the F4 month picker does.</summary>
    private static void OpenPaySheetForMonth(MainWindowViewModel vm, DateOnly wageMonth)
    {
        vm.OpenReport(ReportKind.PaySheet);
        SelectWageMonth(vm.Reports!, wageMonth);
    }

    private static void SelectWageMonth(ReportsViewModel reports, DateOnly wageMonth) =>
        reports.SelectedPayrollMonth = reports.PayrollMonths.First(m => m.FirstDay == wageMonth);

    /// <summary>
    /// A company whose e-Payments report has a row in "Ready for Sending to Bank": a bank ledger with both its
    /// account number and IFS code, a beneficiary with both, and a posted NEFT payment between them. Without
    /// ALL of that the export takes its nothing-ready early return and an Enter-does-not-write assertion would
    /// pass for the wrong reason.
    /// </summary>
    private MainWindowViewModel ReadyEPaymentsFixture(string name, out Company company)
    {
        var vm = NewCompany(name);
        var c = vm.Company!;

        var bank = new DomainLedger(Guid.NewGuid(), "HDFC Bank", c.FindGroupByName("Bank Accounts")!.Id,
            Money.FromRupees(500_000m), openingIsDebit: true)
        {
            EnableChequePrinting = true,
            BankAccountNumber = "50200012345678",
            BankIfsc = "HDFC0000123",
        };
        c.AddLedger(bank);

        var acme = new DomainLedger(Guid.NewGuid(), "Acme Supplies", c.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, openingIsDebit: false)
        {
            BankAccountNumber = "9876543210",
            BankIfsc = "ICIC0000456",
        };
        c.AddLedger(acme);

        new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), c.FindVoucherTypeByName("Payment")!.Id, c.BooksBeginFrom.AddDays(20),
            new[]
            {
                new EntryLine(acme.Id, Money.FromRupees(125_000m), DrCr.Debit),
                new EntryLine(bank.Id, Money.FromRupees(125_000m), DrCr.Credit,
                    bankAllocation: new BankAllocation(BankTransactionType.NEFT, "NEFT-77")),
            },
            partyId: acme.Id));

        _storage.Save(c);
        company = c;
        return vm;
    }
}
