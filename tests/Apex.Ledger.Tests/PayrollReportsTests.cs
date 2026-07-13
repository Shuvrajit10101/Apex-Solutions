using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-8 slice-8 <b>payroll presentation reports</b> (RQ-16) — pure projections over the same
/// <see cref="PayrollComputationService"/> the payroll voucher posts, so every figure reconciles to the payroll
/// computation (and hence to the posted voucher) to the paisa. Covers the Payslip, Pay Sheet, Payroll
/// Register/Statement, Attendance Register and Payment/Bank Advice. The headline oracle is the S3 hand-derived
/// golden payslip (Basic ₹30,000 + HRA 40% = ₹12,000 − Advance ₹2,000 ⇒ gross ₹42,000 / net ₹40,000) extended to a
/// second employee (Basic ₹20,000 + HRA ₹8,000 ⇒ gross ₹28,000 / net ₹28,000), so the whole run nets ₹68,000.
/// </summary>
public sealed class PayrollReportsTests
{
    private static readonly DateOnly PeriodFrom = new(2025, 4, 1);
    private static readonly DateOnly PeriodTo = new(2025, 4, 30);

    private static Guid IndirectExpenses(Company c) => c.FindGroupByName("Indirect Expenses")!.Id;
    private static Guid CurrentLiabilities(Company c) => c.FindGroupByName("Current Liabilities")!.Id;

    private sealed record Fixture(
        Company Company, Guid Emp1, Guid Emp2, Guid PresentTypeId, Guid AbsentTypeId);

    /// <summary>Builds the two-employee golden payroll company + records April attendance for emp1.</summary>
    private static Fixture Build()
    {
        var c = CompanyFactory.CreateSeeded("Payroll Reports Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        var ph = new PayHeadService(c);

        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c), incomeTaxComponent: IncomeTaxComponent.BasicSalary);
        var hra = ph.CreatePayHead("HRA", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue,
            underGroupId: IndirectExpenses(c),
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(4000) }));   // 40% of Basic
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
        ss.DefineForEmployee(emp1.Id, PeriodFrom, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(30000m)),
            new SalaryStructureLine(hra.Id, 1),
            new SalaryStructureLine(advance.Id, 2, new Money(2000m)),
        });
        ss.DefineForEmployee(emp2.Id, PeriodFrom, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(20000m)),
            new SalaryStructureLine(hra.Id, 1),
        });

        // Attendance for emp1 only: 24 present days + 2 absent (unpaid) days in April.
        var present = pay.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid);
        var absent = pay.CreateAttendanceType("Absent", AttendanceTypeKind.LeaveWithoutPay);
        var att = new PayrollAttendanceService(c);
        att.Record(emp1.Id, present.Id, PeriodFrom, PeriodTo, 24m);
        att.Record(emp1.Id, absent.Id, PeriodFrom, PeriodTo, 2m);

        return new Fixture(c, emp1.Id, emp2.Id, present.Id, absent.Id);
    }

    // ================================================================ Payslip

    [Fact]
    public void Payslip_reconciles_to_the_golden_computation()
    {
        var f = Build();
        Post(f.Company, f.Emp1);
        var slip = Report.BuildPayslip(f.Company, f.Emp1, PeriodFrom, PeriodTo);

        Assert.Equal("Rajkumar Sharma", slip.EmployeeName);
        Assert.Equal("E-001", slip.EmployeeNumber);
        Assert.Equal("Manager", slip.Designation);
        Assert.Equal("Staff", slip.Department);
        Assert.Equal("ABCPS1234K", slip.Pan);
        Assert.Equal("State Bank", slip.BankName);

        Assert.Equal(new Money(42000m), slip.GrossEarnings);
        Assert.Equal(new Money(2000m), slip.TotalDeductions);
        Assert.Equal(new Money(40000m), slip.NetPayable);
        Assert.Equal(slip.GrossEarnings, slip.TotalDeductions + slip.NetPayable);

        // Earnings + deductions foot to the aggregates.
        Assert.Equal(new Money(42000m), new Money(slip.Earnings.Sum(l => l.Amount.Amount)));
        Assert.Equal(new Money(2000m), new Money(slip.Deductions.Sum(l => l.Amount.Amount)));
        Assert.Equal(new Money(30000m), slip.Earnings.Single(l => l.Name == "Basic").Amount);
        Assert.Equal(new Money(12000m), slip.Earnings.Single(l => l.Name == "HRA").Amount);
        Assert.Equal(new Money(2000m), slip.Deductions.Single(l => l.Name == "Advance Recovery").Amount);

        // Attendance summary.
        Assert.Equal(24m, slip.DaysPaid);
        Assert.Equal(2m, slip.DaysLop);
    }

    [Fact]
    public void Payslip_ytd_telescopes_prior_posted_months_plus_the_current_month()
    {
        var f = Build();
        // Post April AND May (same flat structure in force); the May payslip's YTD = posted April + posted May.
        new PayrollVoucherService(f.Company).Post(PeriodFrom, PeriodTo, new[] { f.Emp1 });

        var mayFrom = new DateOnly(2025, 5, 1);
        var mayTo = new DateOnly(2025, 5, 31);
        new PayrollVoucherService(f.Company).Post(mayFrom, mayTo, new[] { f.Emp1 });
        var slip = Report.BuildPayslip(f.Company, f.Emp1, mayFrom, mayTo);

        Assert.Equal(new Money(42000m), slip.GrossEarnings);           // May alone
        Assert.Equal(new Money(84000m), slip.YtdGrossEarnings);        // Apr + May
        Assert.Equal(new Money(4000m), slip.YtdTotalDeductions);       // 2,000 + 2,000
        Assert.Equal(new Money(80000m), slip.YtdNetPayable);           // 40,000 + 40,000
        Assert.Equal(slip.YtdGrossEarnings, slip.YtdTotalDeductions + slip.YtdNetPayable);
    }

    // ================================================================ Pay Sheet (matrix)

    [Fact]
    public void Pay_sheet_columns_and_rows_foot_to_the_same_grand_total()
    {
        var f = Build();
        Post(f.Company, f.Emp1, f.Emp2);
        var sheet = Report.BuildPaySheet(f.Company, new[] { f.Emp1, f.Emp2 }, PeriodFrom, PeriodTo);

        // Columns = the posted earnings then deductions across all employees, ordered.
        Assert.Equal(new[] { "Basic", "HRA", "Advance Recovery" }, sheet.Columns.Select(col => col.Name).ToArray());
        Assert.Equal(2, sheet.Rows.Count);

        // Column totals.
        Assert.Equal(new Money(50000m), sheet.ColumnTotals[0]); // Basic 30,000 + 20,000
        Assert.Equal(new Money(20000m), sheet.ColumnTotals[1]); // HRA 12,000 + 8,000
        Assert.Equal(new Money(2000m), sheet.ColumnTotals[2]);  // Advance 2,000 + 0

        Assert.Equal(new Money(70000m), sheet.TotalGrossEarnings);
        Assert.Equal(new Money(2000m), sheet.TotalDeductions);
        Assert.Equal(new Money(68000m), sheet.TotalNetPayable);

        // Row totals foot: Σ row nets == grand net; each column total == Σ its cells.
        Assert.Equal(new Money(68000m), new Money(sheet.Rows.Sum(r => r.NetPayable.Amount)));
        for (int j = 0; j < sheet.Columns.Count; j++)
            Assert.Equal(sheet.ColumnTotals[j], new Money(sheet.Rows.Sum(r => r.Values[j].Amount)));

        // Grand total is internally consistent: Σ earning col totals − Σ deduction col totals == grand net.
        var earnTotal = 0m; var dedTotal = 0m;
        for (int j = 0; j < sheet.Columns.Count; j++)
        {
            if (sheet.Columns[j].Role == PayHeadPostingRole.Earning) earnTotal += sheet.ColumnTotals[j].Amount;
            else if (sheet.Columns[j].Role == PayHeadPostingRole.Deduction) dedTotal += sheet.ColumnTotals[j].Amount;
        }
        Assert.Equal(68000m, earnTotal - dedTotal);

        var r1 = sheet.Rows.Single(r => r.EmployeeName == "Rajkumar Sharma");
        Assert.Equal(new Money(42000m), r1.GrossEarnings);
        Assert.Equal(new Money(40000m), r1.NetPayable);
    }

    // ================================================================ Payroll Register / Statement

    [Fact]
    public void Payroll_register_columns_reconcile_and_total()
    {
        var f = Build();
        Post(f.Company, f.Emp1, f.Emp2);
        var reg = Report.BuildPayrollRegister(f.Company, new[] { f.Emp1, f.Emp2 }, PeriodFrom, PeriodTo);

        var r1 = reg.Rows.Single(r => r.EmployeeName == "Rajkumar Sharma");
        Assert.Equal(new Money(42000m), r1.GrossEarnings);
        Assert.Equal(new Money(2000m), r1.OtherDeductions);   // the advance recovery (not a statutory head)
        Assert.Equal(Money.Zero, r1.ProfessionalTax);
        Assert.Equal(Money.Zero, r1.EmployeePf);
        Assert.Equal(new Money(2000m), r1.TotalDeductions);
        Assert.Equal(new Money(40000m), r1.NetPayable);
        // Every row: the statutory + other columns foot to total deductions, and gross − deductions == net.
        foreach (var r in reg.Rows)
        {
            Assert.Equal(r.TotalDeductions,
                new Money(r.ProfessionalTax.Amount + r.EmployeePf.Amount + r.EmployeeEsi.Amount
                    + r.IncomeTax.Amount + r.OtherDeductions.Amount));
            Assert.Equal(r.NetPayable, r.GrossEarnings - r.TotalDeductions);
        }

        Assert.Equal(new Money(70000m), reg.TotalGrossEarnings);
        Assert.Equal(new Money(2000m), reg.TotalDeductions);
        Assert.Equal(new Money(68000m), reg.TotalNetPayable);
    }

    // ================================================================ Attendance Register

    [Fact]
    public void Attendance_register_reports_per_employee_days_and_totals()
    {
        var f = Build();
        var reg = Report.BuildAttendanceRegister(f.Company, new[] { f.Emp1, f.Emp2 }, PeriodFrom, PeriodTo);

        // Only the recorded types appear, ordered by name: Absent, Present.
        Assert.Equal(new[] { "Absent", "Present" }, reg.Types.Select(t => t.Name).ToArray());

        var r1 = reg.Rows.Single(r => r.EmployeeName == "Rajkumar Sharma");
        Assert.Equal(24m, r1.DaysPaid);
        Assert.Equal(2m, r1.DaysLop);
        Assert.Equal(2m, r1.Values[0]);  // Absent column
        Assert.Equal(24m, r1.Values[1]); // Present column

        var r2 = reg.Rows.Single(r => r.EmployeeName == "Anita Desai");
        Assert.Equal(0m, r2.DaysPaid);
        Assert.Equal(0m, r2.DaysLop);

        Assert.Equal(2m, reg.TypeTotals[0]);  // Absent
        Assert.Equal(24m, reg.TypeTotals[1]); // Present
    }

    // ================================================================ Payment / Bank Advice

    [Fact]
    public void Payment_advice_net_matches_the_payslip_and_posted_voucher()
    {
        var f = Build();
        // Post the run, THEN project the advice from it.
        var v = new PayrollVoucherService(f.Company).Post(PeriodFrom, PeriodTo, new[] { f.Emp1, f.Emp2 });
        var advice = Report.BuildPaymentAdvice(f.Company, new[] { f.Emp1, f.Emp2 }, PeriodFrom, PeriodTo);

        Assert.Equal(2, advice.Rows.Count);
        var a1 = advice.Rows.Single(r => r.EmployeeName == "Rajkumar Sharma");
        Assert.Equal(new Money(40000m), a1.NetPayable);
        Assert.Equal("State Bank", a1.BankName);
        Assert.Equal("1234567890", a1.BankAccountNumber);
        Assert.Equal("SBIN0001234", a1.BankIfsc);
        Assert.Equal(new Money(68000m), advice.TotalNetPayable);

        // Reconcile to the payslip net.
        var slip = Report.BuildPayslip(f.Company, f.Emp1, PeriodFrom, PeriodTo);
        Assert.Equal(slip.NetPayable, a1.NetPayable);

        // Reconcile to the POSTED voucher's net-payable Cr lines.
        var postedNet = v.Lines
            .Where(l => l.Payroll?.Category == PayrollLineCategory.NetPayable && l.Side == DrCr.Credit)
            .Sum(l => l.Amount.Amount);
        Assert.Equal(68000m, postedNet);
        Assert.Equal(new Money(postedNet), advice.TotalNetPayable);
    }

    [Fact]
    public void Payment_advice_orders_rows_deterministically_by_name()
    {
        var f = Build();
        Post(f.Company, f.Emp1, f.Emp2);
        var advice = Report.BuildPaymentAdvice(f.Company, new[] { f.Emp2, f.Emp1 }, PeriodFrom, PeriodTo);
        Assert.Equal(new[] { "Anita Desai", "Rajkumar Sharma" }, advice.Rows.Select(r => r.EmployeeName).ToArray());
    }

    // ================================================================ F1: As-User-Defined-Value employee

    [Fact]
    public void User_defined_incentive_employee_is_included_in_the_reports_from_the_posted_voucher()
    {
        // F1: an employee whose in-force structure carries an As-User-Defined-Value 'Incentive' head used to be
        // SILENTLY DROPPED from the Pay Sheet / Payroll Register / Payment Advice, because the reports recomputed
        // from masters (svc.Compute) WITHOUT the per-run amount, threw, and the UI swallowed the error. Reading the
        // POSTED voucher lines (where the ₹5,000 incentive already lives) includes her with the correct net.
        var f = Build();
        var c = f.Company;
        var ph = new PayHeadService(c);
        var pay = new PayrollService(c);

        var incentive = ph.CreatePayHead("Incentive", PayHeadType.Earnings, PayHeadCalculationType.AsUserDefinedValue,
            underGroupId: IndirectExpenses(c));
        var basicId = c.FindPayHeadByName("Basic")!.Id;
        var grp = c.FindEmployeeGroupByName("Staff")!.Id;
        var emp3 = pay.CreateEmployee("Meera Nair", grp, employeeNumber: "E-003");
        emp3.BankName = "ICICI Bank"; emp3.BankAccountNumber = "5555555555"; emp3.BankIfsc = "ICIC0005555";
        new SalaryStructureService(c).DefineForEmployee(emp3.Id, PeriodFrom, new[]
        {
            new SalaryStructureLine(basicId, 0, new Money(25000m)),   // Basic ₹25,000
            new SalaryStructureLine(incentive.Id, 1),                 // + variable Incentive (per-run)
        });

        // Post the run WITH the per-run incentive ⇒ gross ₹30,000, net ₹30,000 for Meera; it is baked into the lines.
        var userDefined = new Dictionary<Guid, IReadOnlyDictionary<Guid, Money>>
        {
            [emp3.Id] = new Dictionary<Guid, Money> { [incentive.Id] = new Money(5000m) },
        };
        new PayrollVoucherService(c).Post(PeriodFrom, PeriodTo, new[] { f.Emp1, f.Emp2, emp3.Id },
            userDefinedAmountsByEmployee: userDefined);

        var ids = new[] { f.Emp1, f.Emp2, emp3.Id };

        var sheet = Report.BuildPaySheet(c, ids, PeriodFrom, PeriodTo);
        var meeraRow = sheet.Rows.Single(r => r.EmployeeName == "Meera Nair");
        Assert.Equal(new Money(30000m), meeraRow.GrossEarnings);
        Assert.Equal(new Money(30000m), meeraRow.NetPayable);
        var incentiveCol = sheet.Columns.Select((col, i) => (col, i)).Single(x => x.col.Name == "Incentive").i;
        Assert.Equal(new Money(5000m), meeraRow.Values[incentiveCol]);

        var reg = Report.BuildPayrollRegister(c, ids, PeriodFrom, PeriodTo);
        Assert.Equal(new Money(30000m), reg.Rows.Single(r => r.EmployeeName == "Meera Nair").NetPayable);

        var advice = Report.BuildPaymentAdvice(c, ids, PeriodFrom, PeriodTo);
        Assert.Contains(advice.Rows, r => r.EmployeeName == "Meera Nair" && r.NetPayable == new Money(30000m));
        // Total INCLUDES Meera: emp1 40,000 + emp2 28,000 + Meera 30,000 = 98,000.
        Assert.Equal(new Money(98000m), advice.TotalNetPayable);
    }

    // ================================================================ F2: cancelled / never-posted month

    [Fact]
    public void A_cancelled_month_shows_no_phantom_figures_in_any_payroll_report()
    {
        // F2: the reports must reflect what was POSTED. A cancelled payroll voucher (the bank must NOT pay it) yields
        // no rows in the Pay Sheet / Payroll Register / Payment Advice and a zero advice total — where the old
        // recompute-from-masters showed full phantom salary regardless of the cancellation.
        var f = Build();
        var v = Post(f.Company, f.Emp1, f.Emp2);
        v.Cancelled = true;

        var ids = new[] { f.Emp1, f.Emp2 };
        Assert.Empty(Report.BuildPaySheet(f.Company, ids, PeriodFrom, PeriodTo).Rows);
        Assert.Empty(Report.BuildPayrollRegister(f.Company, ids, PeriodFrom, PeriodTo).Rows);
        var advice = Report.BuildPaymentAdvice(f.Company, ids, PeriodFrom, PeriodTo);
        Assert.Empty(advice.Rows);
        Assert.Equal(Money.Zero, advice.TotalNetPayable);
        // The payslip likewise shows nothing was paid.
        var slip = Report.BuildPayslip(f.Company, f.Emp1, PeriodFrom, PeriodTo);
        Assert.Empty(slip.Earnings);
        Assert.Empty(slip.Deductions);
        Assert.Equal(Money.Zero, slip.NetPayable);
    }

    [Fact]
    public void A_never_posted_month_shows_no_phantom_figures()
    {
        // F2 (companion): a wage month with NO posted payroll at all is empty, not full phantom salary.
        var f = Build();
        var ids = new[] { f.Emp1, f.Emp2 };
        Assert.Empty(Report.BuildPaySheet(f.Company, ids, PeriodFrom, PeriodTo).Rows);
        Assert.Empty(Report.BuildPaymentAdvice(f.Company, ids, PeriodFrom, PeriodTo).Rows);
    }

    /// <summary>Posts the golden run for the given employees over the April wage month and returns the voucher.</summary>
    private static Voucher Post(Company company, params Guid[] employeeIds) =>
        new PayrollVoucherService(company).Post(PeriodFrom, PeriodTo, employeeIds);
}
