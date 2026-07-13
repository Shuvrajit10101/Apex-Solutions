using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// End-to-end coverage for the Phase-8 slice-3 <b>Attendance / Production voucher</b> and <b>Payroll voucher</b> UI
/// surfaced in the cascade (Study Guide pp.213–216; RQ-6/RQ-7). Both screens are gated behind F11 "Maintain
/// Payroll" (ER-13) and nest under Transactions → Vouchers → Payroll. The headline oracle is the hand-derived
/// golden payslip driven through the real shell VMs: Basic 30,000 (flat) + HRA 40%-of-Basic = 12,000 (computed) −
/// Advance Recovery 2,000 (deduction) ⇒ gross 42,000 / net 40,000, the computed Dr/Cr summary balancing to the
/// paisa, and Accept posting one balanced integrated Payroll voucher. Also covers attendance recording (+ its flow
/// into an On-Production head), the nav gate, and the engine guards surfaced as friendly messages. Drives the real
/// VMs over a throwaway .db — no UI toolkit. Dates are derived from the created company's financial year so the
/// posted voucher always sits on/after Books-Begin.
/// </summary>
public sealed class PayrollVoucherViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public PayrollVoucherViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexPayrollVoucherTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- harness

    private MainWindowViewModel NewPayrollCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.PayrollEnabled = true;
        page.PayrollStatutoryEnabled = true;
        vm.Back(); // close the config page back to the Gateway
        return vm;
    }

    private static Guid IndirectExpenses(Company c) => c.FindGroupByName("Indirect Expenses")!.Id;
    private static Guid CurrentLiabilities(Company c) => c.FindGroupByName("Current Liabilities")!.Id;

    /// <summary>The FY's first month as a [from, to] pair (the payroll period every test pays over).</summary>
    private static (DateOnly From, DateOnly To) Period(Company c)
    {
        var from = c.FinancialYearStart;
        return (from, from.AddMonths(1).AddDays(-1));
    }

    private static string D(DateOnly d) => d.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

    /// <summary>Builds the golden employee (Basic + computed HRA + Advance Recovery) on the company's masters.</summary>
    private static Guid BuildGolden(Company c)
    {
        var ph = new PayHeadService(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: IndirectExpenses(c));
        var hra = ph.CreatePayHead("HRA", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue, underGroupId: IndirectExpenses(c),
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(4000) })); // 40% of Basic
        var advance = ph.CreatePayHead("Advance Recovery", PayHeadType.LoansAndAdvances, PayHeadCalculationType.FlatRate, underGroupId: CurrentLiabilities(c));

        var pay = new PayrollService(c);
        var emp = pay.CreateEmployee("Rajkumar Sharma", pay.CreateEmployeeGroup("Staff").Id);
        new SalaryStructureService(c).DefineForEmployee(emp.Id, Period(c).From, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(30000m)),
            new SalaryStructureLine(hra.Id, 1),
            new SalaryStructureLine(advance.Id, 2, new Money(2000m)),
        });
        return emp.Id;
    }

    private static string[] SelectableLabels(MainWindowViewModel vm) =>
        vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }

    // ---------------------------------------------------------------- nav (gated on F11 Maintain Payroll)

    [Fact]
    public void Vouchers_menu_lists_the_payroll_vouchers_only_when_payroll_is_enabled()
    {
        var vm = NewPayrollCompany("Nav On Co");
        vm.ShowVouchersMenu();
        var labels = SelectableLabels(vm);
        Assert.Contains("Attendance / Production", labels);
        Assert.Contains("Payroll", labels);
        Assert.Contains("Payroll", vm.Menu.Where(m => m.IsHeader).Select(m => m.Label));
        // The Payroll voucher advertises its Ctrl+F4 accelerator.
        Assert.Equal("Ctrl+F4", vm.Menu.Single(m => m.IsSelectable && m.Label == "Payroll").Hint);
    }

    [Fact]
    public void Vouchers_menu_hides_the_payroll_vouchers_when_payroll_is_off()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "Nav Off Co";
        vm.CreateCompany();
        vm.ShowVouchersMenu();
        var labels = SelectableLabels(vm);
        Assert.DoesNotContain("Attendance / Production", labels);
        Assert.DoesNotContain("Payroll", labels);
    }

    [Fact]
    public void Selecting_payroll_opens_the_payroll_voucher_screen()
    {
        var vm = NewPayrollCompany("Open Co");
        BuildGolden(vm.Company!);
        vm.ShowVouchersMenu();

        var col = vm.Columns[^1];
        var idx = -1;
        for (var i = 0; i < col.Items.Count; i++)
            if (col.Items[i].IsSelectable && col.Items[i].Label == "Payroll") { idx = i; break; }
        Assert.True(idx >= 0);
        col.SetSelected(idx);
        vm.ActivateSelected();

        Assert.Equal(Screen.PayrollVoucherEntry, vm.CurrentScreen);
        Assert.NotNull(vm.PayrollVoucher);
    }

    // ---------------------------------------------------------------- Attendance / Production voucher

    [Fact]
    public void Attendance_voucher_records_entries_all_or_nothing()
    {
        var vm = NewPayrollCompany("Attd Co");
        var c = vm.Company!;
        var pay = new PayrollService(c);
        var emp = pay.CreateEmployee("Worker", pay.CreateEmployeeGroup("Shop").Id);
        var present = pay.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid);
        var (from, to) = Period(c);

        vm.ShowAttendanceVoucher();
        var page = vm.AttendanceVoucher!;
        page.PeriodFromText = D(from);
        page.PeriodToText = D(to);

        var row = page.Rows[0];
        row.SelectedEmployee = c.FindEmployee(emp.Id);
        row.SelectedAttendanceType = present;
        row.ValueText = "26";

        Assert.True(page.Accept(), page.Message);
        Assert.True(page.LastAcceptSucceeded);

        var entry = Assert.Single(c.AttendanceEntries);
        Assert.Equal(emp.Id, entry.EmployeeId);
        Assert.Equal(present.Id, entry.AttendanceTypeId);
        Assert.Equal(26m, entry.Value);
        Assert.Equal(from, entry.FromDate);
        Assert.Equal(to, entry.ToDate);

        // The recorded entry reads back on the screen.
        Assert.Contains(page.RecentEntries, r => r.Employee == "Worker" && r.Value == "26");
    }

    [Fact]
    public void Attendance_voucher_surfaces_a_guard_on_a_negative_value_and_records_nothing()
    {
        var vm = NewPayrollCompany("Attd Guard Co");
        var c = vm.Company!;
        var pay = new PayrollService(c);
        var emp = pay.CreateEmployee("Worker", pay.CreateEmployeeGroup("Shop").Id);
        var present = pay.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid);

        vm.ShowAttendanceVoucher();
        var page = vm.AttendanceVoucher!;
        var row = page.Rows[0];
        row.SelectedEmployee = c.FindEmployee(emp.Id);
        row.SelectedAttendanceType = present;
        row.ValueText = "-5";

        Assert.False(page.Accept());
        Assert.False(page.LastAcceptSucceeded);
        Assert.NotNull(page.Message);
        Assert.Empty(c.AttendanceEntries);
    }

    [Fact]
    public void Attendance_voucher_keeps_a_trailing_blank_row()
    {
        var vm = NewPayrollCompany("Attd Row Co");
        var c = vm.Company!;
        var pay = new PayrollService(c);
        var emp = pay.CreateEmployee("Worker", pay.CreateEmployeeGroup("Shop").Id);
        var present = pay.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid);

        vm.ShowAttendanceVoucher();
        var page = vm.AttendanceVoucher!;
        Assert.Single(page.Rows); // one blank row to start

        page.Rows[0].SelectedEmployee = c.FindEmployee(emp.Id);
        page.Rows[0].SelectedAttendanceType = present;
        page.Rows[0].ValueText = "20";
        Assert.Equal(2, page.Rows.Count); // a fresh trailing blank appeared
        Assert.True(page.Rows[^1].IsBlank);
    }

    // ---------------------------------------------------------------- Payroll voucher — golden payslip

    [Fact]
    public void Payroll_voucher_computes_the_golden_breakdown_balanced_to_the_paisa()
    {
        var vm = NewPayrollCompany("Golden Co");
        var empId = BuildGolden(vm.Company!);
        var (from, to) = Period(vm.Company!);

        vm.ShowPayrollVoucher();
        var page = vm.PayrollVoucher!;
        page.PeriodFromText = D(from);
        page.PeriodToText = D(to);
        page.EmployeeSelections.Single(s => s.Employee.Id == empId).IsIncluded = true;

        Assert.True(page.Compute(), page.Message);
        Assert.True(page.HasComputed);

        // Aggregates: gross 42,000 / deductions 2,000 / net 40,000.
        Assert.Equal("42,000.00", page.GrossText);
        Assert.Equal("2,000.00", page.DeductionsText);
        Assert.Equal("40,000.00", page.NetText);
        Assert.Equal("0.00", page.EmployerText);

        // Balances to the paisa: Σ Dr == Σ Cr == 42,000.
        Assert.Equal("42,000.00", page.TotalDebitText);
        Assert.Equal("42,000.00", page.TotalCreditText);
        Assert.True(page.IsBalanced);
        Assert.Contains("Balanced", page.BalanceText);

        // The exact four breakdown lines (Basic Dr, HRA Dr, Advance Cr, Net Cr).
        var lines = page.Breakdown.Where(r => !r.IsEmployeeHeader).ToList();
        Assert.Contains(lines, r => r.PayHead == "Basic" && r.DebitText == "30,000.00" && r.Category == "Earning");
        Assert.Contains(lines, r => r.PayHead == "HRA" && r.DebitText == "12,000.00" && r.Category == "Earning");
        Assert.Contains(lines, r => r.PayHead == "Advance Recovery" && r.CreditText == "2,000.00" && r.Category == "Deduction");
        Assert.Contains(lines, r => r.PayHead == PayrollVoucherService.SalaryPayableLedgerName
                                    && r.CreditText == "40,000.00" && r.Category == "Net Pay");
    }

    [Fact]
    public void Payroll_voucher_accept_posts_one_balanced_payroll_voucher()
    {
        var vm = NewPayrollCompany("Post Co");
        var empId = BuildGolden(vm.Company!);
        var c = vm.Company!;
        var (from, to) = Period(c);

        vm.ShowPayrollVoucher();
        var page = vm.PayrollVoucher!;
        page.PeriodFromText = D(from);
        page.PeriodToText = D(to);
        page.VoucherDateText = D(to);
        page.EmployeeSelections.Single(s => s.Employee.Id == empId).IsIncluded = true;
        Assert.True(page.Compute(), page.Message);

        Assert.True(page.Accept(), page.Message);
        Assert.True(page.LastPostSucceeded);

        var posted = Assert.Single(c.Vouchers, v => c.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Payroll);
        Assert.True(VoucherValidator.IsBalanced(posted));
        Assert.Equal(new Money(42000m), posted.TotalDebit);
        Assert.Equal(new Money(42000m), posted.TotalCredit);

        // The auto-created payroll ledgers exist and the net line credits Salary Payable 40,000.
        var payable = c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!;
        var net = posted.Lines.Single(l => l.LedgerId == payable.Id);
        Assert.Equal(new Money(40000m), net.Amount);
        Assert.Equal(DrCr.Credit, net.Side);

        // The preview is cleared after posting (a second post needs a deliberate re-Compute).
        Assert.False(page.HasComputed);

        // The voucher survived the round-trip to storage.
        var reloaded = _storage.Load(_storage.ListCompanies().Single(e => e.Name == "Post Co"));
        Assert.Contains(reloaded.Vouchers, v => reloaded.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Payroll);
    }

    [Fact]
    public void Payroll_voucher_posts_the_same_totals_as_the_engine_service()
    {
        // Independent oracle: the VM's posted totals equal a direct engine post on a twin company.
        var vm = NewPayrollCompany("Twin UI Co");
        var empId = BuildGolden(vm.Company!);
        var (from, to) = Period(vm.Company!);
        vm.ShowPayrollVoucher();
        var page = vm.PayrollVoucher!;
        page.PeriodFromText = D(from);
        page.PeriodToText = D(to);
        page.EmployeeSelections.Single(s => s.Employee.Id == empId).IsIncluded = true;
        Assert.True(page.Accept(), page.Message);
        var uiVoucher = vm.Company!.Vouchers.Single(v => vm.Company!.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Payroll);

        var engineCompany = CompanyFactory.CreateSeeded("Twin Engine Co", from, from);
        new PayrollService(engineCompany).EnablePayroll();
        var engEmp = BuildGolden(engineCompany);
        var engineVoucher = new PayrollVoucherService(engineCompany).Post(from, to, new[] { engEmp });

        Assert.Equal(engineVoucher.TotalDebit, uiVoucher.TotalDebit);
        Assert.Equal(engineVoucher.TotalCredit, uiVoucher.TotalCredit);
    }

    // ---------------------------------------------------------------- Payroll voucher — attendance flows in

    [Fact]
    public void Recorded_production_flows_into_an_on_production_pay_head()
    {
        var vm = NewPayrollCompany("Piece Co");
        var c = vm.Company!;
        var pay = new PayrollService(c);
        var units = pay.CreateSimplePayrollUnit("Nos", "Numbers");
        var produced = pay.CreateAttendanceType("Units Produced", AttendanceTypeKind.Production, payrollUnitId: units.Id);
        var ph = new PayHeadService(c);
        var piece = ph.CreatePayHead("Piece Wages", PayHeadType.Earnings, PayHeadCalculationType.OnProduction,
            underGroupId: IndirectExpenses(c), attendanceTypeId: produced.Id);
        var emp = pay.CreateEmployee("Pieceworker", pay.CreateEmployeeGroup("Line").Id);
        var (from, to) = Period(c);
        new SalaryStructureService(c).DefineForEmployee(emp.Id, from, new[]
        {
            new SalaryStructureLine(piece.Id, 0, new Money(50m)), // ₹50 per unit
        });

        // Record 100 produced units through the Attendance voucher.
        vm.ShowAttendanceVoucher();
        var attd = vm.AttendanceVoucher!;
        attd.PeriodFromText = D(from);
        attd.PeriodToText = D(to);
        attd.Rows[0].SelectedEmployee = c.FindEmployee(emp.Id);
        attd.Rows[0].SelectedAttendanceType = produced;
        attd.Rows[0].ValueText = "100";
        Assert.True(attd.Accept(), attd.Message);

        // The payroll run values the piece wages at 50 × 100 = 5,000.
        vm.ShowPayrollVoucher();
        var page = vm.PayrollVoucher!;
        page.PeriodFromText = D(from);
        page.PeriodToText = D(to);
        page.EmployeeSelections.Single(s => s.Employee.Id == emp.Id).IsIncluded = true;
        Assert.True(page.Compute(), page.Message);
        Assert.Equal("5,000.00", page.GrossText);
        Assert.Equal("5,000.00", page.NetText);
        Assert.True(page.IsBalanced);
    }

    // ---------------------------------------------------------------- guards

    [Fact]
    public void Payroll_voucher_needs_at_least_one_employee_ticked()
    {
        var vm = NewPayrollCompany("No Pick Co");
        BuildGolden(vm.Company!);
        var (from, to) = Period(vm.Company!);
        vm.ShowPayrollVoucher();
        var page = vm.PayrollVoucher!;
        page.PeriodFromText = D(from);
        page.PeriodToText = D(to);

        Assert.False(page.Compute());
        Assert.False(page.HasComputed);
        Assert.Contains("at least one employee", page.Message);
    }

    [Fact]
    public void Payroll_voucher_surfaces_the_no_structure_guard()
    {
        var vm = NewPayrollCompany("No Struct Co");
        var c = vm.Company!;
        var pay = new PayrollService(c);
        var emp = pay.CreateEmployee("Unstructured", pay.CreateEmployeeGroup("Staff").Id); // no salary structure
        var (from, to) = Period(c);

        vm.ShowPayrollVoucher();
        var page = vm.PayrollVoucher!;
        page.PeriodFromText = D(from);
        page.PeriodToText = D(to);
        page.EmployeeSelections.Single(s => s.Employee.Id == emp.Id).IsIncluded = true;

        Assert.False(page.Compute());
        Assert.False(page.HasComputed);
        Assert.Contains("salary structure", page.Message);
    }

    [Fact]
    public void Payroll_voucher_surfaces_the_negative_net_guard()
    {
        var vm = NewPayrollCompany("Neg Co");
        var c = vm.Company!;
        var ph = new PayHeadService(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: IndirectExpenses(c));
        var advance = ph.CreatePayHead("Advance", PayHeadType.LoansAndAdvances, PayHeadCalculationType.FlatRate, underGroupId: CurrentLiabilities(c));
        var pay = new PayrollService(c);
        var emp = pay.CreateEmployee("Overdrawn", pay.CreateEmployeeGroup("Staff").Id);
        var (from, to) = Period(c);
        new SalaryStructureService(c).DefineForEmployee(emp.Id, from, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(10000m)),
            new SalaryStructureLine(advance.Id, 1, new Money(12000m)), // deduction > earning
        });

        vm.ShowPayrollVoucher();
        var page = vm.PayrollVoucher!;
        page.PeriodFromText = D(from);
        page.PeriodToText = D(to);
        page.EmployeeSelections.Single(s => s.Employee.Id == emp.Id).IsIncluded = true;

        // Compute shows the imbalance warning; Accept is rejected by the engine and posts nothing.
        page.Compute();
        Assert.Contains("negative net", page.Message);
        Assert.False(page.Accept());
        Assert.DoesNotContain(c.Vouchers, v => c.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Payroll);
    }
}
