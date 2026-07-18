using System;
using System.IO;
using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// End-to-end coverage for the Phase-8 slice-8 <b>payroll presentation reports</b> UI surfaced in the cascade
/// (RQ-16; catalog §14): the Payslip, Pay Sheet, Payroll Register/Statement, Attendance Register and Payment/Bank
/// Advice, all opened as <see cref="ReportKind"/> report pages under Reports → Payroll Reports (gated on Payroll).
/// The headline oracle is the S3 golden payslip (Basic ₹30,000 + HRA 40% = ₹12,000 − Advance ₹2,000 ⇒ gross ₹42,000
/// / net ₹40,000) extended to a second employee (Basic ₹20,000 + HRA ₹8,000 ⇒ gross ₹28,000 / net ₹28,000), so the
/// run nets ₹68,000. Drives the real shell view models over a throwaway .db — no UI toolkit — and prints the Payslip
/// through the dedicated de-branded PayslipPdf. Every figure reconciles to the payroll computation to the paisa.
/// </summary>
public sealed class PayrollReportsViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public PayrollReportsViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexPayrollReportsUiTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    // ---------------------------------------------------------------- harness

    private static Guid IndirectExpenses(Company c) => c.FindGroupByName("Indirect Expenses")!.Id;
    private static Guid CurrentLiabilities(Company c) => c.FindGroupByName("Current Liabilities")!.Id;

    private readonly record struct Fixture(MainWindowViewModel Vm, Company Company, Guid Emp1, Guid Emp2, DateOnly Month);

    /// <summary>A new company with Payroll enabled via the F11 Statutory-Configuration page.</summary>
    private MainWindowViewModel NewPayrollCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        vm.ShowGstConfig();
        vm.GstConfig!.PayrollEnabled = true;
        vm.Back();
        return vm;
    }

    /// <summary>Builds the two-employee golden payroll company (structures effective from the company FY start) plus
    /// April attendance for emp1, and returns the FY-start wage month the reports project.</summary>
    private Fixture BuildGolden(string name)
    {
        var vm = NewPayrollCompany(name);
        var c = vm.Company!;
        var month = new DateOnly(c.FinancialYearStart.Year, c.FinancialYearStart.Month, 1);
        var monthTo = month.AddMonths(1).AddDays(-1);

        var pay = new PayrollService(c);
        var ph = new PayHeadService(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c), incomeTaxComponent: IncomeTaxComponent.BasicSalary);
        var hra = ph.CreatePayHead("HRA", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue,
            underGroupId: IndirectExpenses(c),
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(4000) }));
        var advance = ph.CreatePayHead("Advance Recovery", PayHeadType.LoansAndAdvances, PayHeadCalculationType.FlatRate,
            underGroupId: CurrentLiabilities(c));

        var grp = pay.CreateEmployeeGroup("Staff").Id;

        var emp1 = pay.CreateEmployee("Rajkumar Sharma", grp, employeeNumber: "E-001", pan: "ABCPS1234K");
        emp1.Designation = "Manager";
        emp1.DateOfJoining = new DateOnly(2020, 1, 1);
        emp1.BankName = "State Bank";
        emp1.BankAccountNumber = "1234567890";
        emp1.BankIfsc = "SBIN0001234";

        var emp2 = pay.CreateEmployee("Anita Desai", grp, employeeNumber: "E-002");
        emp2.BankName = "HDFC Bank";
        emp2.BankAccountNumber = "9876543210";
        emp2.BankIfsc = "HDFC0004321";

        var ss = new SalaryStructureService(c);
        ss.DefineForEmployee(emp1.Id, month, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(30000m)),
            new SalaryStructureLine(hra.Id, 1),
            new SalaryStructureLine(advance.Id, 2, new Money(2000m)),
        });
        ss.DefineForEmployee(emp2.Id, month, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(20000m)),
            new SalaryStructureLine(hra.Id, 1),
        });

        var present = pay.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid);
        var absent = pay.CreateAttendanceType("Absent", AttendanceTypeKind.LeaveWithoutPay);
        var att = new PayrollAttendanceService(c);
        att.Record(emp1.Id, present.Id, month, monthTo, 24m);
        att.Record(emp1.Id, absent.Id, month, monthTo, 2m);

        // The payroll presentation reports project the POSTED Payroll voucher, so post the golden run for the month.
        new PayrollVoucherService(c).Post(month, monthTo, new[] { emp1.Id, emp2.Id });

        _storage.Save(c);
        return new Fixture(vm, c, emp1.Id, emp2.Id, month);
    }

    /// <summary>Opens the report of <paramref name="kind"/> and scopes it to the golden wage month, returning the VM.</summary>
    private static ReportsViewModel Open(Fixture f, ReportKind kind)
    {
        f.Vm.OpenReport(kind);
        var r = f.Vm.Reports!;
        r.SelectedPayrollMonth = r.PayrollMonths.First(m => m.FirstDay == f.Month);
        return r;
    }

    // ---------------------------------------------------------------- (1) Payslip

    [Fact]
    public void Payslip_shows_the_golden_gross_42000_and_net_40000()
    {
        var f = BuildGolden("Payslip UI Co");
        var r = Open(f, ReportKind.Payslip);
        r.SelectedPayrollEmployee = r.PayrollEmployees.First(e => e.EmployeeId == f.Emp1);

        Assert.Equal(Screen.Report, f.Vm.CurrentScreen);
        Assert.False(r.IsPayslipEmpty);
        Assert.Equal("Rajkumar Sharma", r.PayslipEmployee);
        Assert.Equal("42,000.00", r.PayslipGross);
        Assert.Equal("2,000.00", r.PayslipTotalDeductions);
        Assert.Equal("40,000.00", r.PayslipNet);

        Assert.Equal("30,000.00", r.PayslipEarnings.Single(l => l.Name == "Basic").Amount);
        Assert.Equal("12,000.00", r.PayslipEarnings.Single(l => l.Name == "HRA").Amount);
        Assert.Equal("2,000.00", r.PayslipDeductions.Single(l => l.Name == "Advance Recovery").Amount);
        Assert.Contains("Forty Thousand", r.PayslipNetWords, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Payslip_prints_a_debranded_pdf_via_the_payslip_writer()
    {
        var f = BuildGolden("Payslip PDF Co");
        var r = Open(f, ReportKind.Payslip);
        r.SelectedPayrollEmployee = r.PayrollEmployees.First(e => e.EmployeeId == f.Emp1);

        f.Vm.OpenPrintPreview();
        var preview = f.Vm.PrintPreview!;
        Assert.NotEmpty(preview.PdfBytes);

        string s = Encoding.Latin1.GetString(preview.PdfBytes);
        Assert.StartsWith("%PDF-", s);
        Assert.Contains("%%EOF", s);
        Assert.Contains("Rajkumar Sharma", s);
        Assert.Contains("42,000.00", s);
        Assert.Contains("40,000.00", s);
        Assert.DoesNotContain("tally", s.ToLowerInvariant());
    }

    // ---------------------------------------------------------------- (2) Pay Sheet

    [Fact]
    public void Pay_sheet_renders_the_pay_head_matrix_with_footing_totals()
    {
        var f = BuildGolden("Pay Sheet UI Co");
        var r = Open(f, ReportKind.PaySheet);

        Assert.True(r.IsPayrollMatrix);
        var headers = r.PayrollColumns.Select(c => c.Header).ToArray();
        Assert.Equal(new[] { "Employee", "Basic", "HRA", "Advance Recovery", "Gross", "Deductions", "Net Pay" }, headers);

        // Two employee rows + a Grand-Total row.
        Assert.Equal(3, r.PayrollRows.Count);
        var raj = r.PayrollRows.First(row => row.Cells[0].Text == "Rajkumar Sharma");
        Assert.Contains(raj.Cells, cell => cell.Text == "42,000.00"); // Gross
        Assert.Contains(raj.Cells, cell => cell.Text == "40,000.00"); // Net

        var total = r.PayrollRows.Single(row => row.IsTotal);
        Assert.Equal("Grand Total", total.Cells[0].Text);
        Assert.Contains(total.Cells, cell => cell.Text == "70,000.00"); // total gross
        Assert.Contains(total.Cells, cell => cell.Text == "68,000.00"); // total net
    }

    // ---------------------------------------------------------------- (3) Payroll Register

    [Fact]
    public void Payroll_register_breaks_out_statutory_columns_and_totals()
    {
        var f = BuildGolden("Payroll Register UI Co");
        var r = Open(f, ReportKind.PayrollRegister);

        Assert.True(r.IsPayrollMatrix);
        Assert.Equal(new[] { "Employee", "Gross", "Prof. Tax", "Employee PF", "Employee ESI",
            "Income Tax", "Other Ded.", "Total Ded.", "Net Pay", "Employer Contrib." },
            r.PayrollColumns.Select(c => c.Header).ToArray());

        var raj = r.PayrollRows.First(row => row.Cells[0].Text == "Rajkumar Sharma");
        Assert.Equal("42,000.00", raj.Cells[1].Text); // Gross
        Assert.Equal("2,000.00", raj.Cells[6].Text);  // Other deductions (the advance)
        Assert.Equal("40,000.00", raj.Cells[8].Text); // Net

        var total = r.PayrollRows.Single(row => row.IsTotal);
        Assert.Equal("70,000.00", total.Cells[1].Text); // total gross
        Assert.Equal("68,000.00", total.Cells[8].Text); // total net
    }

    // ---------------------------------------------------------------- (4) Attendance Register

    [Fact]
    public void Attendance_register_reports_per_type_days_and_totals()
    {
        var f = BuildGolden("Attendance UI Co");
        var r = Open(f, ReportKind.AttendanceRegister);

        Assert.True(r.IsPayrollMatrix);
        var headers = r.PayrollColumns.Select(c => c.Header).ToArray();
        Assert.Equal(new[] { "Employee", "Absent", "Present", "Days Paid", "LOP" }, headers);

        var raj = r.PayrollRows.First(row => row.Cells[0].Text == "Rajkumar Sharma");
        Assert.Equal("2", raj.Cells[1].Text);   // Absent
        Assert.Equal("24", raj.Cells[2].Text);  // Present
        Assert.Equal("24", raj.Cells[3].Text);  // Days Paid
        Assert.Equal("2", raj.Cells[4].Text);   // LOP
    }

    // ---------------------------------------------------------------- (5) Payment Advice

    [Fact]
    public void Payment_advice_shows_the_bank_details_and_net_matching_the_payslip()
    {
        var f = BuildGolden("Payment Advice UI Co");
        var r = Open(f, ReportKind.PaymentAdvice);

        Assert.True(r.IsPayrollMatrix);
        Assert.Equal(new[] { "Employee", "Bank", "A/c Number", "IFSC", "Net Pay" },
            r.PayrollColumns.Select(c => c.Header).ToArray());

        var raj = r.PayrollRows.First(row => row.Cells[0].Text == "Rajkumar Sharma");
        Assert.Equal("State Bank", raj.Cells[1].Text);
        Assert.Equal("1234567890", raj.Cells[2].Text);
        Assert.Equal("SBIN0001234", raj.Cells[3].Text);
        Assert.Equal("40,000.00", raj.Cells[4].Text);

        var total = r.PayrollRows.Single(row => row.IsTotal);
        Assert.Equal("68,000.00", total.Cells[4].Text);
    }

    // ---------------------------------------------------------------- (6) nav gating

    [Fact]
    public void Payroll_reports_group_is_surfaced_only_when_payroll_is_enabled()
    {
        var f = BuildGolden("Payroll Nav Co");
        f.Vm.ShowPayrollReportsMenu();
        Assert.Equal(GatewayMenu.PayrollReports, f.Vm.CurrentGatewayMenu);
        var submenu = f.Vm.Columns[^1];
        Assert.True(submenu.IsMenu);
        var labels = submenu.Items.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Equal(new[] { "Payslip", "Pay Sheet", "Payroll Register", "Attendance Register", "Payment Advice" }, labels);

        // A non-payroll company never surfaces the group (ER-13) and the open path is a no-op.
        var plain = new MainWindowViewModel(_storage);
        plain.NewCompanyName = "No Payroll Co";
        plain.CreateCompany();
        plain.ShowPayrollReportsMenu();
        Assert.NotEqual(GatewayMenu.PayrollReports, plain.CurrentGatewayMenu);
    }

    // ---------------------------------------------------------------- (7) G1 empty-state regression lock
    //
    // THE BUG (D9 / G1). The shared C7 empty-state overlay ("No entries for the selected period.") is drawn
    // LAST over the report body and gated on ReportsViewModel.IsEmpty. IsEmpty was `Rows.Count == 0` — but a
    // PAYROLL report keeps its data in PayrollRows / the Payslip* collections, NOT in Rows, so a fully
    // computed payroll matrix had Rows.Count == 0 → IsEmpty WRONGLY true → the overlay painted "No entries…"
    // OVER the populated Pay Sheet / Payroll Register / Attendance / Payment Advice / Payslip. The fix makes
    // IsEmpty `!IsPayrollReport && Rows.Count == 0`: payroll reports are excluded from the shared overlay (they
    // carry their OWN in-pane note, IsPayrollEmpty / IsPayslipEmpty), and every non-payroll report keeps the
    // exact old behaviour. These plain-unit facts lock all three legs so the regression cannot silently return.

    [Fact]
    public void G1_populated_payroll_report_is_not_flagged_empty()
    {
        // A payroll matrix WITH data: PayrollRows are populated but Rows is empty. IsEmpty MUST be false, or the
        // C7 overlay masks the matrix. (Reverting the fix to `Rows.Count == 0` flips this to true — the bite.)
        var f = BuildGolden("G1 Populated Co");
        var r = Open(f, ReportKind.PaySheet);

        Assert.True(r.IsPayrollReport);
        Assert.NotEmpty(r.PayrollRows);        // the matrix HAS rows...
        Assert.Empty(r.Rows);                  // ...none of which live in Rows (the C7 signal)
        Assert.False(r.IsPayrollEmpty);        // genuinely populated
        Assert.False(r.IsEmpty);               // so the shared overlay stays OFF the payroll pane (G1)

        // The Payslip keeps its data in the Payslip* collections; it too must never read as empty.
        var slip = Open(f, ReportKind.Payslip);
        slip.SelectedPayrollEmployee = slip.PayrollEmployees.First(e => e.EmployeeId == f.Emp1);
        Assert.False(slip.IsPayslipEmpty);
        Assert.False(slip.IsEmpty);
    }

    [Fact]
    public void G1_genuinely_empty_payroll_shows_its_own_note_not_the_shared_overlay()
    {
        // Payroll enabled but NO run posted for the wage month → the matrix is genuinely empty. The fix must
        // not merely disable the empty-state: the payroll pane shows its OWN note (IsPayrollEmpty) while the
        // shared C7 overlay stays off it (IsEmpty false, because payroll is excluded from the shared overlay).
        var vm = NewPayrollCompany("G1 Empty Payroll Co");
        vm.OpenReport(ReportKind.PaySheet);
        var r = vm.Reports!;

        Assert.True(r.IsPayrollReport);
        Assert.True(r.IsPayrollEmpty);         // the pane's OWN "no employees with salary" note is on
        Assert.False(r.IsEmpty);               // the shared overlay is excluded from payroll entirely
    }

    [Fact]
    public void G1_non_payroll_empty_report_still_shows_the_shared_overlay()
    {
        // The fix's `!IsPayrollReport` guard must leave the C7 overlay working for the reports it was built for:
        // a non-payroll register with no data leaves Rows empty and IsEmpty must be TRUE so the shared overlay
        // renders "No entries for the selected period." across the whole body.
        var f = BuildGolden("G1 C7 Co");
        var r = new ReportsViewModel(f.Company, ReportKind.RejectionRegister); // no rejections seeded → empty

        Assert.False(r.IsPayrollReport);
        Assert.Empty(r.Rows);
        Assert.True(r.IsEmpty);                // C7 overlay ON for a genuinely-empty non-payroll report
        Assert.Equal("No entries for the selected period.", r.EmptyMessage);
    }
}
