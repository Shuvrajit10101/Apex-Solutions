using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// The five payroll reports added by user ruling 19 as census rows <b>7.22 – 7.26</b>: the Attendance Sheet, the
/// Pay Head Employee Breakup, the Employee Pay Head Breakup, the Payroll Statutory Summary and the per-employee
/// Income Tax Computation. Every one is a projection over payroll data this product already computes — none of
/// them computes a new figure — so each test below pins the projection against a hand-derived oracle and, where
/// the report exists to be distinguished from a neighbour, pins the distinction too.
///
/// <para><b>The oracle.</b> One April wage month, two employees on flat structures:</para>
/// <list type="bullet">
///   <item><b>Rajkumar</b> — Basic ₹30,000, PT ₹200 deducted, Employer EPF ₹1,800 (as-user-defined), so gross
///   ₹30,000, deductions ₹200, net ₹29,800.</item>
///   <item><b>Anita</b> — Basic ₹20,000, PT ₹200, Employer EPF ₹1,200 ⇒ gross ₹20,000, net ₹19,800.</item>
/// </list>
/// <para>Attendance is recorded for Rajkumar only: 24 present days, 2 unpaid days, 300 units on a Production type
/// and 12 hours on a production type an OVERTIME pay head is calculated on.</para>
/// </summary>
public sealed class PayrollJ1ReportsTests
{
    private static readonly DateOnly From = new(2025, 4, 1);
    private static readonly DateOnly To = new(2025, 4, 30);
    private static readonly DateOnly MayFrom = new(2025, 5, 1);
    private static readonly DateOnly MayTo = new(2025, 5, 31);

    private static Guid IndirectExpenses(Company c) => c.FindGroupByName("Indirect Expenses")!.Id;
    private static Guid CurrentLiabilities(Company c) => c.FindGroupByName("Current Liabilities")!.Id;

    private sealed record Fixture(
        Company Company,
        Guid Rajkumar,
        Guid Anita,
        Guid Vikram,
        Guid BasicId,
        Guid PtId,
        Guid EmployeePfId,
        Guid EmployerPfId,
        Guid PresentTypeId,
        Guid AbsentTypeId,
        Guid ProductionTypeId,
        Guid OvertimeTypeId);

    private static Fixture Build()
    {
        var c = CompanyFactory.CreateSeeded("J1 Payroll Reports Co", From, From);
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableProfessionalTax("27");   // Maharashtra, by GST state code
        pay.EnableProvidentFund(capWagesAtCeiling: true);
        var ph = new PayHeadService(c);

        // ---- attendance / production masters ----
        var days = pay.CreateSimplePayrollUnit("Days", "Days");
        var hrs = pay.CreateSimplePayrollUnit("Hrs", "Hours");
        var nos = pay.CreateSimplePayrollUnit("Nos", "Numbers");
        var present = pay.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid, payrollUnitId: days.Id);
        var absent = pay.CreateAttendanceType("Absent", AttendanceTypeKind.LeaveWithoutPay, payrollUnitId: days.Id);
        var production = pay.CreateAttendanceType("Pieces", AttendanceTypeKind.Production, payrollUnitId: nos.Id);
        var overtimeType = pay.CreateAttendanceType("Overtime Hours", AttendanceTypeKind.Production, payrollUnitId: hrs.Id);

        // ---- pay heads ----
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c), incomeTaxComponent: IncomeTaxComponent.BasicSalary,
            partOfPfWages: true);

        // 🔴 The ONLY thing that makes "Overtime Hours" an overtime type is this head: an overtime-flagged pay
        // head calculated on it. Nothing infers overtime from the type's name, which is why the type is
        // deliberately named after hours rather than being detectable by its caption.
        ph.CreatePayHead("Overtime", PayHeadType.Earnings, PayHeadCalculationType.OnProduction,
            underGroupId: IndirectExpenses(c), attendanceTypeId: overtimeType.Id, isOvertime: true,
            partOfEsiWages: true);

        // The statutory heads are AsUserDefinedValue and computed by their own engines (PF / PT), so they carry no
        // structure amount — the structure only says the head is in force for the employee.
        var pt = ph.CreatePayHead("Professional Tax", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: CurrentLiabilities(c),
            ptComponent: PtStatutoryComponent.ProfessionalTax);
        var eePf = ph.CreatePayHead("Employee EPF", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: CurrentLiabilities(c),
            pfComponent: PfStatutoryComponent.EmployeeProvidentFund);
        var erPf = ph.CreatePayHead("Employer EPF", PayHeadType.EmployersStatutoryContributions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: CurrentLiabilities(c),
            pfComponent: PfStatutoryComponent.EmployerProvidentFund);

        var grp = pay.CreateEmployeeGroup("Staff").Id;
        var rajkumar = pay.CreateEmployee("Rajkumar Sharma", grp, employeeNumber: "E-001", pan: "ABCPS1234K",
            uan: "100123456789");
        rajkumar.DateOfJoining = From;
        rajkumar.PfJoinDate = From;
        rajkumar.DateOfBirth = new DateOnly(1985, 6, 1);
        pay.SetEmployeePfDetails(rajkumar.Id, applicable: true, contributeOnHigherWages: false);
        var anita = pay.CreateEmployee("Anita Desai", grp, employeeNumber: "E-002", pan: "ABCPD5678L",
            uan: "100123456790");
        anita.DateOfJoining = From;
        anita.PfJoinDate = From;
        anita.DateOfBirth = new DateOnly(1990, 2, 3);
        pay.SetEmployeePfDetails(anita.Id, applicable: true, contributeOnHigherWages: false);

        // 🔴 A THIRD employee on ₹1,50,000 a month exists for ONE reason: at ₹30,000 the new regime's slab tax,
        // surcharge and cess are all ZERO, so an income-tax test written against Rajkumar would assert 0 == 0 on
        // every tax line and would pass on a report that computed nothing at all. Vikram's ₹18,00,000 makes the
        // tax lines non-vacuous.
        var vikram = pay.CreateEmployee("Vikram Rao", grp, employeeNumber: "E-003", pan: "ABCPR9012M",
            uan: "100123456791");
        vikram.DateOfJoining = From;
        vikram.PfJoinDate = From;
        vikram.DateOfBirth = new DateOnly(1980, 9, 9);

        var ss = new SalaryStructureService(c);
        ss.DefineForEmployee(vikram.Id, From, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(150000m)),
        });
        ss.DefineForEmployee(rajkumar.Id, From, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(30000m)),
            new SalaryStructureLine(pt.Id, 1),
            new SalaryStructureLine(eePf.Id, 2),
            new SalaryStructureLine(erPf.Id, 3),
        });
        ss.DefineForEmployee(anita.Id, From, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(20000m)),
            new SalaryStructureLine(pt.Id, 1),
            new SalaryStructureLine(eePf.Id, 2),
            new SalaryStructureLine(erPf.Id, 3),
        });

        var att = new PayrollAttendanceService(c);
        att.Record(rajkumar.Id, present.Id, From, To, 24m);
        att.Record(rajkumar.Id, absent.Id, From, To, 2m);
        att.Record(rajkumar.Id, production.Id, From, To, 300m);
        att.Record(rajkumar.Id, overtimeType.Id, From, To, 12m);

        return new Fixture(c, rajkumar.Id, anita.Id, vikram.Id, basic.Id, pt.Id, eePf.Id, erPf.Id,
            present.Id, absent.Id, production.Id, overtimeType.Id);
    }

    private static Voucher Post(Company c, DateOnly from, DateOnly to, params Guid[] ids)
        => new PayrollVoucherService(c).Post(from, to, ids);

    private static List<Guid> Everyone(Company c) => c.Employees.Select(e => e.Id).ToList();

    // =========================================================================== 7.22 Attendance Sheet

    /// <summary>The four figures the vendor's page names, against the hand-recorded oracle.</summary>
    [Fact]
    public void Attendance_sheet_reports_present_absent_produced_and_overtime_per_employee()
    {
        var f = Build();
        var sheet = Report.BuildAttendanceSheet(f.Company, Everyone(f.Company), From, To);

        var raj = sheet.Rows.Single(r => r.EmployeeId == f.Rajkumar);
        Assert.Equal(24m, raj.DaysPresent);
        Assert.Equal(2m, raj.DaysAbsent);
        Assert.Equal(300m, raj.UnitsProduced);
        Assert.Equal(12m, raj.OvertimeWorked);

        // Anita has no attendance at all — a row of zeros, not a missing employee.
        var anita = sheet.Rows.Single(r => r.EmployeeId == f.Anita);
        Assert.True(anita.IsZeroValued);

        Assert.Equal(24m, sheet.TotalDaysPresent);
        Assert.Equal(2m, sheet.TotalDaysAbsent);
        Assert.Equal(300m, sheet.TotalUnitsProduced);
        Assert.Equal(12m, sheet.TotalOvertimeWorked);
    }

    /// <summary>
    /// 🔴 <b>OVERTIME IS DERIVED FROM THE PAY HEAD, NOT FROM THE TYPE'S NAME.</b> Clearing
    /// <see cref="PayHead.IsOvertime"/> must move the 12 hours out of the overtime column and into units produced.
    /// A test that only checked "overtime == 12" would pass on an implementation that pattern-matched the caption
    /// "Overtime Hours", which would be an invention rather than a reading of this book's masters.
    /// </summary>
    [Fact]
    public void Attendance_sheet_decides_overtime_from_the_pay_head_flag_and_not_from_the_type_name()
    {
        var f = Build();
        Assert.Contains(f.OvertimeTypeId, AttendanceSheet.OvertimeAttendanceTypes(f.Company));

        foreach (var head in f.Company.PayHeads) head.IsOvertime = false;

        Assert.Empty(AttendanceSheet.OvertimeAttendanceTypes(f.Company));
        var sheet = Report.BuildAttendanceSheet(f.Company, Everyone(f.Company), From, To);
        var raj = sheet.Rows.Single(r => r.EmployeeId == f.Rajkumar);
        Assert.Equal(0m, raj.OvertimeWorked);
        Assert.Equal(312m, raj.UnitsProduced);   // the 12 hours land in production, not nowhere
    }

    /// <summary>The F12 "Remove zero-valued transactions" option drops the empty row and changes no figure.</summary>
    [Fact]
    public void Attendance_sheet_remove_zero_valued_drops_only_the_empty_rows()
    {
        var f = Build();
        var kept = Report.BuildAttendanceSheet(f.Company, Everyone(f.Company), From, To, removeZeroValued: true);

        Assert.Single(kept.Rows);
        Assert.Equal(f.Rajkumar, kept.Rows[0].EmployeeId);
        Assert.Equal(24m, kept.TotalDaysPresent);
        Assert.Equal(300m, kept.TotalUnitsProduced);
    }

    /// <summary>
    /// 🔴 <b>THE SHEET IS NOT THE REGISTER (census 7.15 vs 7.22).</b> Over the same entries the Register produces a
    /// per-type MATRIX whose width follows the masters, and the Sheet produces the fixed four-figure summary. This
    /// pins that they are different shapes over the same data — the fold this row exists to prevent.
    /// </summary>
    [Fact]
    public void The_attendance_sheet_and_the_attendance_register_are_different_reports_over_the_same_entries()
    {
        var f = Build();
        var ids = Everyone(f.Company);
        var register = Report.BuildAttendanceRegister(f.Company, ids, From, To);
        var sheet = Report.BuildAttendanceSheet(f.Company, ids, From, To);

        // The register carries one column per recorded TYPE — four of them here.
        Assert.Equal(4, register.Types.Count);
        Assert.Contains(register.Types, t => t.Name == "Overtime Hours");

        // The sheet carries no per-type column at all: it is four fixed figures, and the register has no notion
        // of "units produced" or "overtime" as such.
        var raj = sheet.Rows.Single(r => r.EmployeeId == f.Rajkumar);
        var regRaj = register.Rows.Single(r => r.EmployeeId == f.Rajkumar);

        Assert.Equal(regRaj.DaysPaid, raj.DaysPresent);       // they agree where they overlap...
        Assert.Equal(regRaj.DaysLop, raj.DaysAbsent);
        Assert.Equal(312m, regRaj.Values.Sum() - regRaj.DaysPaid - regRaj.DaysLop);  // ...and the register
        Assert.Equal(300m, raj.UnitsProduced);                                        // does not split these two,
        Assert.Equal(12m, raj.OvertimeWorked);                                        // while the sheet does.
    }

    /// <summary>The unit symbols behind each production column are reported, so nothing is summed across units
    /// without saying so.</summary>
    [Fact]
    public void Attendance_sheet_names_the_payroll_units_behind_the_production_columns()
    {
        var f = Build();
        var sheet = Report.BuildAttendanceSheet(f.Company, Everyone(f.Company), From, To);
        Assert.Equal(new[] { "Nos" }, sheet.ProductionUnits);
        Assert.Equal(new[] { "Hrs" }, sheet.OvertimeUnits);
    }

    // =============================================================== 7.23 Pay Head Employee Breakup

    [Fact]
    public void Pay_head_employee_breakup_shows_one_employees_heads_with_opening_and_closing()
    {
        var f = Build();
        Post(f.Company, From, To, f.Rajkumar, f.Anita);

        var breakup = Report.BuildPayHeadEmployeeBreakup(f.Company, f.Rajkumar, From, To);

        Assert.Equal(PayHeadBreakupOrientation.ByPayHeadForOneEmployee, breakup.Orientation);
        Assert.Equal("Rajkumar Sharma", breakup.ScopeName);

        var lines = breakup.Groups.SelectMany(g => g.Lines).ToList();
        var basic = lines.Single(l => l.Name == "Basic");
        Assert.Equal(0m, basic.Opening);            // first month posted
        Assert.Equal(30000m, basic.Debit);          // an earning debits its expense ledger
        Assert.Equal(0m, basic.Credit);
        Assert.Equal(30000m, basic.Closing);

        var pt = lines.Single(l => l.Name == "Professional Tax");
        Assert.Equal(200m, pt.Credit);              // a deduction credits its payable ledger
        Assert.Equal(-200m, pt.Closing);            // debit-positive ⇒ a credit balance is negative

        // ONLY this employee. Anita's ₹20,000 Basic is not in here.
        Assert.Equal(30000m, lines.Where(l => l.Name == "Basic").Sum(l => l.Debit));
    }

    /// <summary>
    /// 🔴 <b>THE EMPLOYER-CONTRIBUTION MIRROR IS EXCLUDED, AND THE EXCLUSION IS WHAT MAKES THE FIGURE TRUE.</b>
    /// An employer contribution posts a debit to the expense ledger and a credit to the payable ledger. Counting
    /// both under one pay head nets it to zero — arithmetically tidy, factually wrong. The head must show its own
    /// (payable) ledger's ₹1,800 credit.
    /// </summary>
    [Fact]
    public void Pay_head_breakup_reads_the_pay_heads_own_ledger_and_not_the_employer_expense_mirror()
    {
        var f = Build();
        Post(f.Company, From, To, f.Rajkumar);

        var breakup = Report.BuildPayHeadEmployeeBreakup(f.Company, f.Rajkumar, From, To);
        var erPf = breakup.Groups.SelectMany(g => g.Lines).Single(l => l.Name == "Employer EPF");

        // ₹550 = the 12% employee share on the ₹15,000-capped PF wages, less the ₹1,250 EPS.
        Assert.Equal(550m, erPf.Credit);
        Assert.Equal(0m, erPf.Debit);                 // the mirror debit is NOT counted here
        Assert.Equal(-550m, erPf.Closing);            // and so the head does not net to zero

        // And the mirror leg IS on the voucher — the report is excluding a real posting, not one that never
        // happened. Without this the test above would pass on a build that simply lost the employer legs.
        var voucher = f.Company.Vouchers.Single(v => v.Lines.Any(l => l.Payroll is not null));
        Assert.Equal(550m, voucher.Lines
            .Where(l => l.Payroll is { Category: PayrollLineCategory.EmployerContributionExpense })
            .Sum(l => l.Amount.Amount));
    }

    /// <summary>A prior posted month becomes the opening balance rather than vanishing or being counted twice.</summary>
    [Fact]
    public void Pay_head_employee_breakup_carries_a_prior_month_forward_as_the_opening_balance()
    {
        var f = Build();
        Post(f.Company, From, To, f.Rajkumar);
        Post(f.Company, MayFrom, MayTo, f.Rajkumar);

        var may = Report.BuildPayHeadEmployeeBreakup(f.Company, f.Rajkumar, MayFrom, MayTo);
        var basic = may.Groups.SelectMany(g => g.Lines).Single(l => l.Name == "Basic");

        Assert.Equal(30000m, basic.Opening);          // April
        Assert.Equal(30000m, basic.Debit);            // May
        Assert.Equal(60000m, basic.Closing);
    }

    // =============================================================== 7.24 Employee Pay Head Breakup

    /// <summary>
    /// 🔴 <b>THE TRANSPOSE — AND THE PROOF THAT NEITHER REPORT ANSWERS FOR THE OTHER.</b> 7.24 is one pay head
    /// across ALL employees, so Basic must show both people; 7.23 for one of them must show only that one. Their
    /// figures must nonetheless reconcile, because they read the same postings through the same engine.
    /// </summary>
    [Fact]
    public void Employee_pay_head_breakup_shows_one_head_across_all_employees_and_reconciles_to_its_transpose()
    {
        var f = Build();
        Post(f.Company, From, To, f.Rajkumar, f.Anita);

        var byHead = Report.BuildEmployeePayHeadBreakup(f.Company, f.BasicId, From, To);
        Assert.Equal(PayHeadBreakupOrientation.ByEmployeeForOnePayHead, byHead.Orientation);
        Assert.Equal("Basic", byHead.ScopeName);

        var lines = byHead.Groups.SelectMany(g => g.Lines).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal(30000m, lines.Single(l => l.Name == "Rajkumar Sharma").Debit);
        Assert.Equal(20000m, lines.Single(l => l.Name == "Anita Desai").Debit);
        Assert.Equal(50000m, byHead.TotalDebit);
        Assert.Equal("Staff", byHead.Groups.Single().GroupName);   // grouped by employee group, not flat

        // The transpose agrees on the cell they share, and the per-employee report shows ONE employee.
        var byEmployee = Report.BuildPayHeadEmployeeBreakup(f.Company, f.Anita, From, To);
        Assert.Equal(20000m, byEmployee.Groups.SelectMany(g => g.Lines).Single(l => l.Name == "Basic").Debit);
        Assert.DoesNotContain(byEmployee.Groups.SelectMany(g => g.Lines), l => l.Name == "Rajkumar Sharma");
    }

    /// <summary>A pay head nothing was posted to reports empty rather than fabricating zero rows for everyone.</summary>
    [Fact]
    public void Employee_pay_head_breakup_of_an_unposted_head_is_empty()
    {
        var f = Build();
        Post(f.Company, From, To, f.Rajkumar);
        var overtimeHead = f.Company.PayHeads.Single(p => p.IsOvertime);

        var breakup = Report.BuildEmployeePayHeadBreakup(f.Company, overtimeHead.Id, From, To);

        Assert.True(breakup.IsEmpty);
        Assert.Equal(0m, breakup.TotalDebit);
    }

    // =============================================================== 7.25 Payroll Statutory Summary

    [Fact]
    public void Payroll_statutory_summary_rolls_up_the_statutory_heads_with_payable_and_paid()
    {
        var f = Build();
        Post(f.Company, From, To, f.Rajkumar, f.Anita);

        var summary = Report.BuildPayrollStatutorySummary(f.Company, From, To);

        // PF wages are capped at the ₹15,000 ceiling for both employees, so each contributes the same:
        // Employee EPF 12% × 15,000 = ₹1,800, Employer EPF = that share less the ₹1,250 EPS = ₹550 ⇒ ₹2,350 each.
        var pf = summary.Rows.Single(r => r.HeadType == PayrollStatutoryHeadType.ProvidentFund);
        Assert.Equal(new Money(4700m), pf.Payable);         // 2 × (1,800 + 550)
        Assert.Equal(Money.Zero, pf.Paid);
        Assert.Equal(new Money(4700m), pf.Balance);

        var pt = summary.Rows.Single(r => r.HeadType == PayrollStatutoryHeadType.ProfessionalTax);
        Assert.Equal(new Money(400m), pt.Payable);          // 200 + 200

        Assert.Equal(new Money(5100m), summary.TotalPayable);
        Assert.Equal(new Money(5100m), summary.TotalBalance);

        // The vendor's "Statutory Pay Head Details" level is present, and it foots to its parent.
        Assert.Equal(new Money(4700m), new Money(pf.Details.Sum(d => d.Payable.Amount)));
        Assert.Equal(new Money(3600m), pf.Details.Single(d => d.PayHeadName == "Employee EPF").Payable);
        Assert.Equal(new Money(1100m), pf.Details.Single(d => d.PayHeadName == "Employer EPF").Payable);
    }

    /// <summary>
    /// A payment against the statutory payable ledger lands in "Paid" and reduces the balance. Without this the
    /// Paid column would be a permanently-zero decoration, which is the shape of a dead feature.
    /// </summary>
    [Fact]
    public void Payroll_statutory_summary_counts_a_settlement_of_the_payable_ledger_as_paid()
    {
        var f = Build();
        Post(f.Company, From, To, f.Rajkumar, f.Anita);

        var ptLedger = f.Company.FindPayHead(f.PtId)!.LedgerId!.Value;
        var cash = f.Company.FindLedgerByName("Cash")!.Id;
        var paymentType = f.Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Payment).Id;
        new LedgerService(f.Company).Post(new Voucher(Guid.NewGuid(), paymentType, To, new[]
        {
            new EntryLine(ptLedger, new Money(400m), DrCr.Debit),
            new EntryLine(cash, new Money(400m), DrCr.Credit),
        }));

        var summary = Report.BuildPayrollStatutorySummary(f.Company, From, To);
        var pt = summary.Rows.Single(r => r.HeadType == PayrollStatutoryHeadType.ProfessionalTax);

        Assert.Equal(new Money(400m), pt.Payable);
        Assert.Equal(new Money(400m), pt.Paid);
        Assert.Equal(Money.Zero, pt.Balance);

        // The PF liability is untouched by a PT payment — the "Paid" column is per statutory type, not a pool.
        Assert.Equal(Money.Zero, summary.Rows.Single(r => r.HeadType == PayrollStatutoryHeadType.ProvidentFund).Paid);
    }

    /// <summary>A pay head that is not statutory never appears — this is the roll-up over PF/ESI/PT, not a
    /// second Payroll Register.</summary>
    [Fact]
    public void Payroll_statutory_summary_excludes_ordinary_pay_heads()
    {
        var f = Build();
        Post(f.Company, From, To, f.Rajkumar);
        var summary = Report.BuildPayrollStatutorySummary(f.Company, From, To);

        Assert.DoesNotContain(summary.Rows.SelectMany(r => r.Details), d => d.PayHeadName == "Basic");
        Assert.Null(PayrollStatutorySummary.ClassifyHead(f.Company.FindPayHead(f.BasicId)!));
    }

    // =============================================================== 7.26 Income Tax Computation

    /// <summary>
    /// 🔴 <b>THE REPORT READS THE SAME COMPUTATION FORM 16 PART B READS.</b> The assertion is not "the figures
    /// look plausible" but "they are, line for line, the Annexure II row" — because a second computation written
    /// here is precisely what would drift from the certificate the employee is handed.
    /// </summary>
    [Fact]
    public void Income_tax_computation_reproduces_the_form_16_annexure_row_line_for_line()
    {
        var f = Build();
        new PayrollService(f.Company).EnableSalaryTds();
        for (var m = 0; m < 12; m++)
            Post(f.Company, From.AddMonths(m), From.AddMonths(m + 1).AddDays(-1), f.Vikram);

        var annexure = Form24Q.BuildAnnexureII(f.Company, 2025).Single(r => r.EmployeeId == f.Vikram);
        var report = Report.BuildIncomeTaxComputation(f.Company, f.Vikram, 2025);

        Assert.NotNull(report);
        Assert.Equal("Vikram Rao", report!.EmployeeName);
        Assert.Equal("ABCPR9012M", report.Pan);
        Assert.Equal("2025-26", report.FinancialYearLabel);

        Money Line(string caption) => report.Lines.Single(l => l.Caption == caption).Amount!.Value;

        // 🔴 The tax lines are NON-VACUOUS on this employee. Without this guard every assertion below could be
        // 0 == 0 and would pass on a report that computed nothing.
        Assert.True(Line("Income Tax on Total Income").Amount > 0m, "the tax oracle is zero — the test is vacuous");
        Assert.True(Line("Health and Education Cess").Amount > 0m, "the cess oracle is zero — the test is vacuous");
        Assert.True(Line("Total Tax Payable").Amount > 0m, "the total-tax oracle is zero — the test is vacuous");

        Assert.Equal(annexure.GrossSalary, Line("Gross Salary"));
        Assert.Equal(annexure.StandardDeduction, Line("Less: Standard Deduction u/s 16(ia)"));
        Assert.Equal(annexure.ChapterViaDeductions, Line("Less: Deductions under Chapter VI-A"));
        Assert.Equal(annexure.TaxableIncome, Line("Total Taxable Income"));
        Assert.Equal(annexure.IncomeTax, Line("Income Tax on Total Income"));
        Assert.Equal(annexure.Surcharge, Line("Surcharge"));
        Assert.Equal(annexure.Cess, Line("Health and Education Cess"));
        Assert.Equal(annexure.TotalTax, Line("Total Tax Payable"));
        Assert.Equal(annexure.TaxDeducted, Line("Less: Tax Deducted so far"));
        Assert.Equal(annexure.TotalTax - annexure.TaxDeducted, Line("Balance Tax Payable"));

        Assert.Equal(annexure.TotalTax, report.TotalTaxPayable);
        Assert.Equal(annexure.TaxDeducted, report.TaxDeductedSoFar);
        Assert.Equal(annexure.TotalTax - annexure.TaxDeducted, report.BalanceTaxPayable);
    }

    /// <summary>The gross the computation opens on is the salary actually posted — ₹1,50,000 × 12.</summary>
    [Fact]
    public void Income_tax_computation_opens_on_the_salary_actually_posted()
    {
        var f = Build();
        new PayrollService(f.Company).EnableSalaryTds();
        for (var m = 0; m < 12; m++)
            Post(f.Company, From.AddMonths(m), From.AddMonths(m + 1).AddDays(-1), f.Vikram);

        var report = Report.BuildIncomeTaxComputation(f.Company, f.Vikram, 2025)!;
        Assert.Equal(new Money(1800000m), report.Lines.Single(l => l.Caption == "Gross Salary").Amount!.Value);

        // Every drillable (italic) component actually carries a breakdown — a component flagged drillable with
        // nothing behind it is a keystroke that does nothing.
        foreach (var drillable in report.Lines.Where(l => l.IsDrillable))
            Assert.Contains(report.Details, d => d.Caption == drillable.Caption);
    }

    /// <summary>An employee with nothing posted for the year gets NO computation rather than a page of zeros —
    /// a printed page of nils reads as a computation, which is a different claim from "there is none".</summary>
    [Fact]
    public void Income_tax_computation_is_null_for_an_employee_with_no_salary_in_the_year()
    {
        var f = Build();
        new PayrollService(f.Company).EnableSalaryTds();
        Post(f.Company, From, To, f.Rajkumar);

        Assert.Null(Report.BuildIncomeTaxComputation(f.Company, f.Anita, 2025));
        Assert.NotNull(Report.BuildIncomeTaxComputation(f.Company, f.Rajkumar, 2025));
    }

    /// <summary>The April-to-March year boundary, since the report is scoped from the chosen wage month.</summary>
    [Theory]
    [InlineData(2025, 4, 1, 2025)]
    [InlineData(2026, 3, 31, 2025)]
    [InlineData(2026, 4, 1, 2026)]
    [InlineData(2025, 1, 15, 2024)]
    public void The_financial_year_of_a_wage_month_runs_april_to_march(int y, int m, int d, int expected)
        => Assert.Equal(expected, IncomeTaxComputationReport.FinancialYearStartYearFor(new DateOnly(y, m, d)));
}
