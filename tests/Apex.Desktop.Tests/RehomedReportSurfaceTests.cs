using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
    /// six gestures this re-home exists to hand these reports, so a missing token would have turned the fix
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
    /// 🔴 <b>THE SETTLE WORKFLOW SURVIVED THE RE-HOME.</b> Routing the Receivables/Payables menu rows to
    /// ReportKinds means nothing else points at <c>Screen.Outstandings</c> any more — and that page is the only
    /// place the spacebar bill multi-select and the Alt+A settlement preload live. Alt+A on the report is the
    /// door that keeps it reachable. Without this the wave would have created a second instance of the very
    /// defect census row 11.10 records ("one shipped report nobody can reach").
    /// </summary>
    [Fact]
    public void Alt_A_on_the_rehomed_outstandings_report_still_reaches_the_settlement_page()
    {
        var vm = FullFixture("Settle Door");

        vm.OpenReport(ReportKind.ReceivablesOutstanding);
        vm.OpenSettlementPageFromOutstandingsReport();
        Assert.Equal(Screen.Outstandings, vm.CurrentScreen);
        Assert.Equal(OutstandingsKind.Receivables, vm.Outstandings!.Kind);

        vm.OpenReport(ReportKind.PayablesOutstanding);
        vm.OpenSettlementPageFromOutstandingsReport();
        Assert.Equal(Screen.Outstandings, vm.CurrentScreen);
        Assert.Equal(OutstandingsKind.Payables, vm.Outstandings!.Kind);
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
    /// 🔴 <b>THE CAPTIONS REACH THE SCREEN, MEASURED ON THE REALISED VISUAL TREE.</b> Everything above this
    /// point asserts view-model state, and view-model state is exactly what a report can have in full while
    /// rendering nothing — the matrix pane is gated on <c>IsPayrollMatrix</c>, the accounting pane on
    /// <c>IsAccountingReport</c>, and getting either wrong yields a correct view model behind a blank or
    /// double-drawn pane. This test opens the real window, drives the real route, and reads the header band that
    /// actually laid out, with non-degenerate bounds.
    /// </summary>
    [AvaloniaFact]
    public void The_rehomed_report_header_band_lays_out_on_screen_with_its_captions()
    {
        var vm = FullFixture("Visual Tree");
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        vm.OpenReport(ReportKind.CostCentreLedgerBreakup);
        Pump(window);

        var reports = vm.Reports!;
        Assert.True(reports.IsPayrollMatrix);

        foreach (var caption in reports.PayrollColumns.Select(c => c.Header))
        {
            var block = Descendants(window)
                .OfType<TextBlock>()
                .FirstOrDefault(t => t.IsEffectivelyVisible && t.Text == caption);

            Assert.True(block is not null,
                $"The column caption '{caption}' never reached the visual tree — the report's figures are on "
                + "screen under headings nobody can read.");
            Assert.True(block!.Bounds.Width > 0 && block.Bounds.Height > 0,
                $"The column caption '{caption}' laid out at a degenerate size.");
        }

        // The accounting Particulars/Dr/Cr band must NOT be drawn at the same time. "Particulars" is that grid's
        // own first caption and none of the three cost reports uses it, so a visible one here means two tables
        // are stacked — which is how this defect presented the last time it shipped.
        Assert.DoesNotContain(
            Descendants(window).OfType<TextBlock>().Where(t => t.IsEffectivelyVisible),
            t => t.Text == "Particulars");
    }

    // ================================================================= harness

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
}
