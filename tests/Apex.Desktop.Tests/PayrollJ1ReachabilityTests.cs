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
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>THE FIVE REPORTS THAT EXISTED AS ARITHMETIC AND NOT AS REPORTS.</b>
///
/// <para>Census rows <b>7.22 – 7.26</b> were added by user ruling 19 because they had <b>no census row at all</b>.
/// Row 7.26 is the sharpest statement of what was missing and why a test file like this one exists: the annual
/// income-tax computation was <i>already</i> in the product and already backing Form 16 Part B — what was absent
/// was a <b>ReportKind, a route and a drill</b>. The census's own bar is that <i>a capability no user can reach is
/// not complete</i>, so every test below drives the operator's route rather than calling a builder.</para>
///
/// <para><b>Why these assertions walk the realised visual tree.</b> Asserting <c>Reports.Kind</c> or a view-model
/// flag is exactly the test that passes on a build where the grid draws nothing — this project has shipped three
/// slices green that held 34, 42 and 17 defects on precisely that pattern. So the reachability tests arrow to a
/// menu label and press Enter, and the rendering tests then look for realised, effectively-visible
/// <see cref="TextBlock"/>s carrying the report's own column headings and figures.</para>
///
/// <para>Headless-safe: visual-tree and layout inspection only. No Skia, no rendered frame.</para>
/// </summary>
public sealed class PayrollJ1ReachabilityTests : IDisposable
{
    private static readonly DateOnly Dob1 = new(1985, 6, 1);
    private static readonly DateOnly Dob2 = new(1990, 2, 3);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public PayrollJ1ReachabilityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexJ1Ui_" + Guid.NewGuid().ToString("N"));
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

    // ------------------------------------------------------------------------------------------ the routes

    /// <summary>The three rows that live under Reports → Payroll Reports.</summary>
    public static IEnumerable<object[]> PayrollReportRows() => new[]
    {
        new object[] { "Attendance Sheet", ReportKind.AttendanceSheet },
        new object[] { "Pay Head Employee Breakup", ReportKind.PayHeadEmployeeBreakup },
        new object[] { "Employee Pay Head Breakup", ReportKind.EmployeePayHeadBreakup },
    };

    /// <summary>The two rows that live under Reports → Statutory Reports → Payroll.</summary>
    public static IEnumerable<object[]> StatutoryRows() => new[]
    {
        new object[] { "Payroll Statutory Summary", ReportKind.PayrollStatutorySummary },
        new object[] { "Income Tax Computation", ReportKind.IncomeTaxComputation },
    };

    /// <summary>
    /// 🔴 <b>THE REACHABILITY TEST FOR THE PAYROLL-REPORTS COLUMN.</b> Each row is driven the way an operator
    /// drives it — open the menu, arrow down to the label, press Enter — and must land on its <b>own</b> report.
    /// </summary>
    [Theory]
    [MemberData(nameof(PayrollReportRows))]
    public void Each_payroll_report_row_is_reachable_by_arrowing_and_opens_its_own_report(
        string label, ReportKind expected)
    {
        var vm = BuildCompany();
        vm.ShowPayrollReportsMenu();
        ArrowTo(vm, label);
        vm.ActivateSelected();

        Assert.NotNull(vm.Reports);
        Assert.Equal(expected, vm.Reports!.Kind);
        Assert.False(string.IsNullOrWhiteSpace(vm.Reports.Title), $"'{label}' opened with no title.");
    }

    /// <summary>The same, for the two rows nested under the payroll statutory column.</summary>
    [Theory]
    [MemberData(nameof(StatutoryRows))]
    public void Each_payroll_statutory_row_is_reachable_by_arrowing_and_opens_its_own_report(
        string label, ReportKind expected)
    {
        var vm = BuildCompany();
        vm.ShowPayrollStatutoryReportsMenu();
        ArrowTo(vm, label);
        vm.ActivateSelected();

        Assert.NotNull(vm.Reports);
        Assert.Equal(expected, vm.Reports!.Kind);
        Assert.False(string.IsNullOrWhiteSpace(vm.Reports.Title), $"'{label}' opened with no title.");
    }

    /// <summary>
    /// 🔴 <b>THE ATTENDANCE SHEET AND THE ATTENDANCE REGISTER ARE TWO MENU ROWS OPENING TWO REPORTS.</b> This is
    /// the single assertion census row 7.22 exists to force. The vendor publishes both on separate pages; routing
    /// the Sheet's label to the Register's <see cref="ReportKind"/> would satisfy "the label exists" while
    /// shipping one report under two names.
    /// </summary>
    [Fact]
    public void The_attendance_sheet_row_and_the_attendance_register_row_open_two_different_reports()
    {
        var vm = BuildCompany();

        vm.ShowPayrollReportsMenu();
        ArrowTo(vm, "Attendance Register");
        vm.ActivateSelected();
        Assert.Equal(ReportKind.AttendanceRegister, vm.Reports!.Kind);
        var registerTitle = vm.Reports.Title;
        var registerHeaders = vm.Reports.PayrollColumns.Select(c => c.Header).ToList();

        vm.ShowPayrollReportsMenu();
        ArrowTo(vm, "Attendance Sheet");
        vm.ActivateSelected();
        Assert.Equal(ReportKind.AttendanceSheet, vm.Reports!.Kind);
        var sheetHeaders = vm.Reports.PayrollColumns.Select(c => c.Header).ToList();

        Assert.NotEqual(registerTitle, vm.Reports.Title);
        Assert.NotEqual(registerHeaders, sheetHeaders);

        // The Sheet's four figures are its own; the Register has no column called any of them.
        foreach (var own in new[] { "Days Present", "Days Absent" })
        {
            Assert.Contains(sheetHeaders, h => h == own);
            Assert.DoesNotContain(registerHeaders, h => h == own);
        }
    }

    /// <summary>
    /// 🔴 <b>7.23 AND 7.24 CARRY DIFFERENT SCOPE PICKERS, AND EXACTLY ONE EACH.</b> They transpose the same data,
    /// so the scope control is the only thing on screen that says which of the two an operator is reading. Both
    /// pickers live in the same grid cell, so "exactly one" is a layout claim as well as a semantic one.
    /// </summary>
    [Theory]
    [InlineData(ReportKind.PayHeadEmployeeBreakup, true, false)]
    [InlineData(ReportKind.EmployeePayHeadBreakup, false, true)]
    [InlineData(ReportKind.IncomeTaxComputation, true, false)]
    [InlineData(ReportKind.AttendanceSheet, false, false)]
    [InlineData(ReportKind.PayrollStatutorySummary, false, false)]
    public void Each_report_carries_the_scope_picker_it_is_scoped_by_and_no_other(
        ReportKind kind, bool employeePicker, bool payHeadPicker)
    {
        var vm = BuildCompany();
        vm.OpenPayrollStatutoryForm(kind);
        var r = vm.Reports!;
        Assert.Equal(kind, r.Kind);

        Assert.Equal(employeePicker, r.ShowPayrollEmployeePicker);
        Assert.Equal(payHeadPicker, r.ShowPayrollPayHeadPicker);

        // The wage-month picker and the pay-head picker share a grid cell, so they must never both be visible.
        Assert.False(r.ShowPayrollMonthPicker && r.ShowPayrollPayHeadPicker,
            "the wage-month row and the pay-head row occupy the same grid cell and would overlap on screen.");
        Assert.True(r.ShowPayrollMonthPicker || r.ShowPayrollPayHeadPicker,
            "every one of these reports is scoped to a wage month, so some row carrying that picker must show.");

        if (payHeadPicker) Assert.NotEmpty(r.PayrollPayHeads);
        if (employeePicker) Assert.NotEmpty(r.PayrollEmployees);
    }

    /// <summary>Every one of the five kinds has a saved-view token — a kind with none throws
    /// <see cref="KeyNotFoundException"/> the moment an operator presses Alt+K on it.</summary>
    [Theory]
    [InlineData(ReportKind.AttendanceSheet)]
    [InlineData(ReportKind.PayHeadEmployeeBreakup)]
    [InlineData(ReportKind.EmployeePayHeadBreakup)]
    [InlineData(ReportKind.PayrollStatutorySummary)]
    [InlineData(ReportKind.IncomeTaxComputation)]
    public void Every_new_kind_round_trips_through_its_saved_view_token(ReportKind kind)
    {
        var token = ReportsViewModel.TokenFor(kind);
        Assert.False(string.IsNullOrEmpty(token));
        Assert.Equal(kind, ReportsViewModel.KindFor(token));
    }

    // --------------------------------------------------------------------- the realised visual tree

    /// <summary>
    /// 🔴 <b>THE ATTENDANCE SHEET'S FOUR FIGURES ARE ON SCREEN.</b> The oracle is 24 present days, 2 absent, 300
    /// units and 12 overtime hours for one employee — each must be readable, under its own heading, in the
    /// realised grid. Asserting the projection's row would pass on a build whose DataTemplate drew nothing.
    /// </summary>
    [AvaloniaFact]
    public void The_attendance_sheets_figures_and_headings_are_actually_rendered()
    {
        var (window, vm) = OpenWindow();
        try
        {
            vm.OpenPayrollStatutoryForm(ReportKind.AttendanceSheet);
            Pump(window);

            foreach (var heading in new[] { "Employee", "Days Present", "Days Absent" })
                Assert.True(IsTextVisible(window, heading),
                    $"the Attendance Sheet's \"{heading}\" heading is not on screen.");

            // The two production headings carry their payroll unit, so a reader can tell 300 pieces from 12 hours.
            Assert.True(IsTextVisible(window, "Units Produced (Nos)"),
                "the Units Produced column is not on screen, or does not name its payroll unit.");
            Assert.True(IsTextVisible(window, "Overtime (Hrs)"),
                "the Overtime column is not on screen, or does not name its payroll unit.");

            foreach (var figure in new[] { "24", "300", "12" })
                Assert.True(IsTextVisible(window, figure),
                    $"the Attendance Sheet projected {figure} but nothing on screen renders it.");

            Assert.True(IsTextVisible(window, "Rajkumar Sharma"),
                "the employee the attendance belongs to is not on screen.");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE VENDOR'S F12 "REMOVE ZERO-VALUED TRANSACTIONS" IS REACHABLE FROM THE KEYBOARD.</b> The engine
    /// carries the flag; this asserts an operator can actually reach it through the report's own F12
    /// configuration, and that the employee with no attendance leaves the screen while the one with attendance
    /// stays. An engine parameter no keystroke sets is a parameter, not a feature.
    /// </summary>
    [AvaloniaFact]
    public void The_attendance_sheets_f12_remove_zero_valued_option_actually_hides_the_empty_row()
    {
        var (window, vm) = OpenWindow();
        try
        {
            vm.OpenPayrollStatutoryForm(ReportKind.AttendanceSheet);
            Pump(window);
            Assert.True(IsTextVisible(window, "Anita Desai"),
                "the employee with no attendance should be listed before the option is applied.");

            vm.Reports!.ApplyConfiguration(hideZero: true, showPercentages: false, ClosingStockMode.AsPostedLedger);
            Pump(window);

            Assert.False(IsTextVisible(window, "Anita Desai"),
                "F12 \"remove zero-valued\" left the employee with no attendance on screen.");
            Assert.True(IsTextVisible(window, "Rajkumar Sharma"),
                "F12 \"remove zero-valued\" also removed the employee who DOES have attendance.");
            Assert.True(IsTextVisible(window, "300"), "the surviving row lost its figures.");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE BREAKUPS NEST UNDER A NAMED GROUP BAND RATHER THAN DUMPING A FLAT LIST.</b> The vendor's 7.23 is
    /// explicitly a <i>"group-wise summary … with the closing balance"</i>, and this codebase's standing UI rule
    /// forbids a flat dump. So the group heading, the pay head beneath it and the closing balance must all render.
    /// </summary>
    [AvaloniaFact]
    public void The_pay_head_employee_breakup_renders_its_group_band_lines_and_closing_balance()
    {
        var (window, vm) = OpenWindow();
        try
        {
            vm.OpenPayrollStatutoryForm(ReportKind.PayHeadEmployeeBreakup);
            Pump(window);
            var r = vm.Reports!;

            Assert.False(r.IsPayrollEmpty, r.PayrollEmptyNote);
            foreach (var heading in new[] { "Pay Head", "Opening Balance", "Debit", "Credit", "Closing Balance" })
                Assert.True(IsTextVisible(window, heading),
                    $"the breakup's \"{heading}\" heading is not on screen.");

            // The group band, and a line indented beneath it.
            var groupRow = r.PayrollRows.First(row => row.Cells[0].Text == "Indirect Expenses");
            Assert.NotNull(groupRow);
            Assert.True(IsTextVisible(window, "Indirect Expenses"),
                "the breakup's accounting-group band is not on screen — this would be the flat dump the UI rule "
                + "forbids and the vendor's group-wise summary is not.");
            Assert.True(IsTextVisible(window, "Basic"), "the pay head under the group band is not on screen.");

            // The ₹30,000 closing balance, rendered WITH its side. A bare negative would be unreadable as a credit.
            Assert.True(IsTextVisible(window, "30,000.00"),
                "the breakup's Basic figure is not on screen.");
            Assert.True(IsTextVisible(window, "Dr"),
                "no balance on the breakup names its side — a signed number alone cannot be read as Dr or Cr.");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE TRANSPOSE IS A DIFFERENT REPORT ON SCREEN, NOT THE SAME ONE RELABELLED.</b> 7.24 must list
    /// EMPLOYEES under its scope pay head, and 7.23 must list PAY HEADS under its scope employee.
    /// </summary>
    [AvaloniaFact]
    public void The_employee_pay_head_breakup_lists_employees_where_its_transpose_lists_pay_heads()
    {
        var (window, vm) = OpenWindow();
        try
        {
            vm.OpenPayrollStatutoryForm(ReportKind.EmployeePayHeadBreakup);
            // Scope it to Basic, which both employees are paid on.
            vm.Reports!.SelectedPayrollPayHead =
                vm.Reports.PayrollPayHeads.Single(p => p.Display == "Basic");
            Pump(window);

            Assert.True(IsTextVisible(window, "Employee"), "7.24's name column is not headed \"Employee\".");
            Assert.True(IsTextVisible(window, "Rajkumar Sharma"));
            Assert.True(IsTextVisible(window, "Anita Desai"),
                "the second employee paid on this head is missing — 7.24 is ONE head across ALL employees.");
            Assert.True(IsTextVisible(window, "Staff"),
                "7.24's employee-group band is not on screen.");

            // ...and the transpose, scoped to one employee, shows pay heads and only that employee's figures.
            vm.OpenPayrollStatutoryForm(ReportKind.PayHeadEmployeeBreakup);
            vm.Reports!.SelectedPayrollEmployee =
                vm.Reports.PayrollEmployees.First(e => e.Display.StartsWith("Anita", StringComparison.Ordinal));
            Pump(window);

            Assert.True(IsTextVisible(window, "Basic"));
            Assert.False(IsTextVisible(window, "Rajkumar Sharma"),
                "7.23 is scoped to ONE employee; another employee's name on it means the scope is not applied.");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE STATUTORY SUMMARY'S DETAIL BAND IS DRAWN, NOT JUST PROJECTED.</b> The vendor opens the
    /// <i>Statutory Pay Head Details</i> on Enter; this book renders it as a second band instead, which is a
    /// divergence in shape only if the band is actually on screen. A projected-and-undrawn second band is exactly
    /// the defect that hollowed out PF Form 6A page 2.
    /// </summary>
    [AvaloniaFact]
    public void The_payroll_statutory_summary_renders_its_types_its_details_and_its_notes()
    {
        var (window, vm) = OpenWindow();
        try
        {
            vm.OpenPayrollStatutoryForm(ReportKind.PayrollStatutorySummary);
            Pump(window);
            var r = vm.Reports!;

            Assert.False(r.IsPayrollEmpty, r.PayrollEmptyNote);
            foreach (var heading in new[] { "Pay Head Type", "Payable", "Paid", "Balance" })
                Assert.True(IsTextVisible(window, heading), $"the summary's \"{heading}\" heading is not on screen.");

            Assert.True(IsTextVisible(window, "Provident Fund"));
            Assert.True(IsTextVisible(window, "Professional Tax"));

            // The detail band and a pay head that appears ONLY in it.
            Assert.True(r.HasPayrollSection2);
            Assert.True(IsTextVisible(window, "Statutory Pay Head Details"),
                "the detail band's caption was projected but never drawn.");
            Assert.True(IsTextVisible(window, "Employer EPF"),
                "the per-pay-head detail level is not on screen — the summary would be a total with nothing "
                + "behind it.");

            // The NPS divergence is declared on the report's own face, not left as a silent gap.
            Assert.Contains(r.PayrollFootnotes, n => n.Contains("NPS", StringComparison.Ordinal));
            foreach (var note in r.PayrollFootnotes)
                Assert.True(IsTextVisible(window, note), $"the footnote \"{note}\" is not on screen.");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE INCOME TAX COMPUTATION IS A REPORT AN OPERATOR CAN READ, AND ITS FIGURES ARE FORM 16'S.</b> The
    /// row was ABSENT rather than PARTIAL precisely because the arithmetic already existed and no user could
    /// reach it, so the assertion is that every Form-16 line is <b>on screen</b> and equal to the Annexure II row.
    /// </summary>
    [AvaloniaFact]
    public void The_income_tax_computation_renders_the_form_16_lines_and_agrees_with_annexure_two()
    {
        var (window, vm) = OpenWindow();
        try
        {
            vm.OpenPayrollStatutoryForm(ReportKind.IncomeTaxComputation);
            Pump(window);
            var r = vm.Reports!;
            Assert.False(r.IsPayrollEmpty, r.PayrollEmptyNote);

            foreach (var caption in new[]
            {
                "Gross Salary", "Less: Standard Deduction u/s 16(ia)", "Total Taxable Income",
                "Income Tax on Total Income", "Health and Education Cess", "Total Tax Payable",
                "Less: Tax Deducted so far", "Balance Tax Payable", "Tax Regime", "PAN",
            })
                Assert.True(IsTextVisible(window, caption),
                    $"the computation's \"{caption}\" line is not on screen.");

            // The employee, the regime and the PAN — the vendor's header band.
            Assert.True(IsTextVisible(window, "ABCPS1234K"), "the employee's PAN is not on screen.");

            // 🔴 The figures ARE Form 16 Part B's. A second computation here would drift from the certificate.
            var fyStart = vm.Company!.FinancialYearStart.Month >= 4
                ? vm.Company.FinancialYearStart.Year
                : vm.Company.FinancialYearStart.Year - 1;
            var employeeId = r.SelectedPayrollEmployee!.EmployeeId;
            var annexure = Form24Q.BuildAnnexureII(vm.Company, fyStart).Single(x => x.EmployeeId == employeeId);
            var report = Report.BuildIncomeTaxComputation(vm.Company, employeeId, fyStart)!;

            Assert.Equal(annexure.GrossSalary, report.Lines.Single(l => l.Caption == "Gross Salary").Amount!.Value);
            Assert.Equal(annexure.TotalTax, report.TotalTaxPayable);
            Assert.Equal(annexure.TaxDeducted, report.TaxDeductedSoFar);

            // And the gross the operator reads is the salary actually posted, formatted for the grid.
            Assert.True(IsTextVisible(window, IndianFormat.AmountAlways(annexure.GrossSalary.Amount)),
                "the computation's Gross Salary figure is not on screen.");

            // The date-blindness of the §192 slab tables (open defect T1-26) is stated on the report itself
            // rather than papered over.
            Assert.Contains(r.PayrollFootnotes,
                n => n.Contains("2025-26", StringComparison.Ordinal)
                  && n.Contains("effective-from", StringComparison.Ordinal));
        }
        finally { window.Close(); }
    }

    /// <summary>Switching to another report clears the new reports' second band and footnotes — a stale detail
    /// band under a different title would print one report's figures under another's heading.</summary>
    [AvaloniaFact]
    public void The_statutory_summarys_detail_band_is_cleared_when_another_report_is_opened()
    {
        var (window, vm) = OpenWindow();
        try
        {
            vm.OpenPayrollStatutoryForm(ReportKind.PayrollStatutorySummary);
            Pump(window);
            var stale = vm.Reports!.PayrollSection2Title;
            Assert.True(vm.Reports.HasPayrollSection2);

            vm.Reports.Show(ReportKind.AttendanceSheet);
            Pump(window);

            Assert.False(vm.Reports.HasPayrollSection2);
            Assert.Empty(vm.Reports.PayrollRows2);
            Assert.Empty(vm.Reports.PayrollColumns2);
            Assert.False(IsTextVisible(window, stale),
                "the statutory summary's detail caption is still on screen under a different report.");
        }
        finally { window.Close(); }
    }

    // ----------------------------------------------------------------------- legibility invariants
    //
    // 🔴 The existing legibility sweep (PayrollStatutoryFormLegibilityTests) covers the eight PF/ESI forms and
    // NOTHING ELSE, so a new report family inherits none of it. Truncation is this project's largest measured
    // defect class — a column cut at its differentiator makes two different money columns read identically — so
    // the same two invariants are asserted here against the five new reports.
    //
    // The metrics below are the shipped face's own, copied from that file: the grid is monospace, so width is
    // character count × advance, and both numbers were measured by render rather than assumed.

    /// <summary>The body cell DataTemplate's <c>Padding="8,0,8,0"</c> (MainWindow.axaml).</summary>
    private const double CellPadding = 16;

    /// <summary>Shipped-face advance for a body cell at <c>FontSize="12.5"</c>.</summary>
    private const double BodyAdvance = 6.8726;

    /// <summary>Shipped-face advance for a <c>colHdr</c> column heading at <c>FontSize="12"</c>.</summary>
    private const double HeaderAdvance = 6.5977;

    /// <summary>
    /// 🔴 <b>EVERY ROW HAS ONE CELL PER COLUMN.</b> A row one cell short prints every figure after the gap under
    /// the WRONG heading — silently, and most damagingly on the breakups, where Opening / Debit / Credit /
    /// Closing are four money columns that look alike. The group-heading and total rows are included on purpose:
    /// they are the rows most likely to be built by hand and to drift from the column band.
    /// </summary>
    [Theory]
    [InlineData(ReportKind.AttendanceSheet)]
    [InlineData(ReportKind.PayHeadEmployeeBreakup)]
    [InlineData(ReportKind.EmployeePayHeadBreakup)]
    [InlineData(ReportKind.PayrollStatutorySummary)]
    [InlineData(ReportKind.IncomeTaxComputation)]
    public void Every_row_of_every_new_report_has_one_cell_per_column(ReportKind kind)
    {
        var vm = BuildCompany();
        vm.OpenPayrollStatutoryForm(kind);
        var r = vm.Reports!;

        Assert.NotEmpty(r.PayrollColumns);
        Assert.NotEmpty(r.PayrollRows);      // else the per-row assertion would prove nothing

        foreach (var (row, i) in r.PayrollRows.Select((x, i) => (x, i)))
            Assert.True(r.PayrollColumns.Count == row.Cells.Count,
                $"{kind} row {i} has {row.Cells.Count} cells against {r.PayrollColumns.Count} columns — every "
                + $"figure after the missing cell prints under the WRONG heading. Row: "
                + $"[{string.Join(" ~ ", row.Cells.Select(c => c.Text))}]");

        foreach (var (row, i) in r.PayrollRows2.Select((x, i) => (x, i)))
            Assert.True(r.PayrollColumns2.Count == row.Cells.Count,
                $"{kind} detail-band row {i} has {row.Cells.Count} cells against "
                + $"{r.PayrollColumns2.Count} columns.");
    }

    /// <summary>
    /// 🔴 <b>NOTHING TRUNCATES — NOT A HEADING, NOT A VALUE.</b> Width is character count × the shipped
    /// monospace advance, so this is exact rather than approximate. A cut heading is the worse half: it is the
    /// only thing telling "Opening Balance" from "Closing Balance", or one statutory pay head from another.
    /// </summary>
    [Theory]
    [InlineData(ReportKind.AttendanceSheet)]
    [InlineData(ReportKind.PayHeadEmployeeBreakup)]
    [InlineData(ReportKind.EmployeePayHeadBreakup)]
    [InlineData(ReportKind.PayrollStatutorySummary)]
    [InlineData(ReportKind.IncomeTaxComputation)]
    public void No_heading_or_value_on_a_new_report_is_cut_off_by_its_column(ReportKind kind)
    {
        var vm = BuildCompany();
        vm.OpenPayrollStatutoryForm(kind);
        var r = vm.Reports!;

        CheckBand(kind, "grid", r.PayrollColumns, r.PayrollRows);
        CheckBand(kind, "detail band", r.PayrollColumns2, r.PayrollRows2);

        static void CheckBand(
            ReportKind kind,
            string band,
            IEnumerable<PayrollMatrixColumnVm> columns,
            IEnumerable<PayrollMatrixRowVm> rows)
        {
            var cols = columns.ToList();
            for (var j = 0; j < cols.Count; j++)
            {
                var room = cols[j].Width - 16;   // colHdr Padding="8,4" — the same 8px each side as a body cell
                Assert.True(cols[j].Header.Length * HeaderAdvance <= room,
                    $"{kind} {band} heading \"{cols[j].Header}\" needs "
                    + $"{cols[j].Header.Length * HeaderAdvance:0.#}px but its column gives {room:0.#}px — it "
                    + "will be cut, and the cut part is what tells this column from its neighbours.");
            }

            foreach (var row in rows)
                for (var j = 0; j < row.Cells.Count && j < cols.Count; j++)
                {
                    var cell = row.Cells[j];
                    var room = cell.Width - CellPadding;
                    Assert.True(cell.Text.Length * BodyAdvance <= room,
                        $"{kind} {band} value \"{cell.Text}\" under \"{cols[j].Header}\" needs "
                        + $"{cell.Text.Length * BodyAdvance:0.#}px but its cell gives {room:0.#}px.");
                }
        }
    }

    // ---------------------------------------------------------------------------- print and export

    /// <summary>The new reports survive onto paper and into the spreadsheet, with their footnotes. A report that
    /// cannot be printed is not a document — the defect that hollowed out rows 8.1, 11.9, 11.10 and 11.11.</summary>
    [Theory]
    [InlineData(ReportKind.AttendanceSheet)]
    [InlineData(ReportKind.PayHeadEmployeeBreakup)]
    [InlineData(ReportKind.EmployeePayHeadBreakup)]
    [InlineData(ReportKind.PayrollStatutorySummary)]
    [InlineData(ReportKind.IncomeTaxComputation)]
    public void Every_new_report_prints_and_exports_with_its_rows(ReportKind kind)
    {
        var vm = BuildCompany();
        vm.OpenPayrollStatutoryForm(kind);
        var r = vm.Reports!;
        Assert.False(r.IsPayrollEmpty, $"{kind} rendered empty: {r.PayrollEmptyNote}");

        var print = ReportPrintProjector.Project(r);
        Assert.NotEmpty(print.Rows);

        var export = ReportTabularProjector.Project(r);
        var flat = export.Rows.SelectMany(row => row.Cells).ToList();
        Assert.NotEmpty(flat);
        foreach (var note in r.PayrollFootnotes)
            Assert.Contains(flat, c => c.TextValue == note);
    }

    /// <summary>A company with Payroll off never surfaces the payroll-reports column at all.</summary>
    [Fact]
    public void A_company_without_payroll_cannot_open_the_payroll_reports_menu()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "No Payroll Co";
        vm.CreateCompany();

        vm.ShowPayrollReportsMenu();

        Assert.NotEqual(GatewayMenu.PayrollReports, vm.CurrentGatewayMenu);
    }

    // ------------------------------------------------------------------------------------------- harness

    private static void ArrowTo(MainWindowViewModel vm, string label)
    {
        var guard = 0;
        while (vm.Menu[vm.SelectedIndex].Label != label)
        {
            vm.MoveDown();
            Assert.True(++guard < 100, $"'{label}' is not reachable by arrowing through the open menu.");
        }
    }

    private (MainWindow Window, MainWindowViewModel Vm) OpenWindow()
    {
        var vm = BuildCompany();
        var window = new MainWindow { DataContext = vm, Width = 1600, Height = 900 };
        window.Show();
        Pump(window);
        return (window, vm);
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

    private static bool IsTextVisible(MainWindow window, string text)
        => !string.IsNullOrEmpty(text)
        && Descendants(window).Any(v =>
            v is TextBlock { IsEffectivelyVisible: true } t
            && t.Bounds.Width > 0 && t.Bounds.Height > 0
            && t.Text is not null
            && t.Text.Contains(text, StringComparison.Ordinal));

    /// <summary>
    /// One April wage month, two employees on flat structures, PF + PT + salary-TDS enrolled, with attendance
    /// recorded for one of them across all four attendance kinds (present · absent · production · an overtime
    /// production type) and the payroll posted. Deliberately the same oracle the engine tests use.
    /// </summary>
    private MainWindowViewModel BuildCompany()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "J1 Payroll Co " + Guid.NewGuid().ToString("N")[..8];
        vm.CreateCompany();
        var c = vm.Company!;
        var from = new DateOnly(c.FinancialYearStart.Year, c.FinancialYearStart.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);

        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableProvidentFund(capWagesAtCeiling: true);
        pay.EnableProfessionalTax("27");
        pay.EnableSalaryTds();

        var days = pay.CreateSimplePayrollUnit("Days", "Days");
        var hrs = pay.CreateSimplePayrollUnit("Hrs", "Hours");
        var nos = pay.CreateSimplePayrollUnit("Nos", "Numbers");
        var present = pay.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid, payrollUnitId: days.Id);
        var absent = pay.CreateAttendanceType("Absent", AttendanceTypeKind.LeaveWithoutPay, payrollUnitId: days.Id);
        var pieces = pay.CreateAttendanceType("Pieces", AttendanceTypeKind.Production, payrollUnitId: nos.Id);
        var otType = pay.CreateAttendanceType("Extra Duty", AttendanceTypeKind.Production, payrollUnitId: hrs.Id);

        var ph = new PayHeadService(c);
        var indirect = c.FindGroupByName("Indirect Expenses")!.Id;
        var liab = c.FindGroupByName("Current Liabilities")!.Id;

        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: indirect, incomeTaxComponent: IncomeTaxComponent.BasicSalary, partOfPfWages: true);
        // The only thing that makes "Extra Duty" an overtime type is this head.
        ph.CreatePayHead("Overtime", PayHeadType.Earnings, PayHeadCalculationType.OnProduction,
            underGroupId: indirect, attendanceTypeId: otType.Id, isOvertime: true);
        var pt = ph.CreatePayHead("Professional Tax", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            ptComponent: PtStatutoryComponent.ProfessionalTax);
        var eePf = ph.CreatePayHead("Employee EPF", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            pfComponent: PfStatutoryComponent.EmployeeProvidentFund);
        var erPf = ph.CreatePayHead("Employer EPF", PayHeadType.EmployersStatutoryContributions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            pfComponent: PfStatutoryComponent.EmployerProvidentFund);

        var grp = pay.CreateEmployeeGroup("Staff").Id;
        // "Anita" sorts before "Rajkumar", so she is the DEFAULT employee-picker selection. The tests that read
        // the default therefore need her to be the one with the salary; the attendance oracle is Rajkumar's, and
        // the attendance sheet lists everybody, so both are readable without changing the picker.
        var anita = pay.CreateEmployee("Anita Desai", grp, employeeNumber: "E-002", pan: "ABCPS1234K",
            uan: "100123456790");
        anita.DateOfJoining = from;
        anita.PfJoinDate = from;
        anita.DateOfBirth = Dob2;
        pay.SetEmployeePfDetails(anita.Id, applicable: true, contributeOnHigherWages: false);

        var raj = pay.CreateEmployee("Rajkumar Sharma", grp, employeeNumber: "E-001", pan: "ABCPD5678L",
            uan: "100123456789");
        raj.DateOfJoining = from;
        raj.PfJoinDate = from;
        raj.DateOfBirth = Dob1;
        pay.SetEmployeePfDetails(raj.Id, applicable: true, contributeOnHigherWages: false);

        var ss = new SalaryStructureService(c);
        foreach (var (id, amount) in new[] { (anita.Id, 30000m), (raj.Id, 20000m) })
            ss.DefineForEmployee(id, from, new[]
            {
                new SalaryStructureLine(basic.Id, 0, new Money(amount)),
                new SalaryStructureLine(pt.Id, 1),
                new SalaryStructureLine(eePf.Id, 2),
                new SalaryStructureLine(erPf.Id, 3),
            });

        var att = new PayrollAttendanceService(c);
        att.Record(raj.Id, present.Id, from, to, 24m);
        att.Record(raj.Id, absent.Id, from, to, 2m);
        att.Record(raj.Id, pieces.Id, from, to, 300m);
        att.Record(raj.Id, otType.Id, from, to, 12m);

        new PayrollVoucherService(c).Post(from, to, new[] { anita.Id, raj.Id });
        _storage.Save(c);
        return vm;
    }
}
