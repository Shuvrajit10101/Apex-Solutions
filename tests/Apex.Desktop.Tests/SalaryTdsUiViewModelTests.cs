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
/// End-to-end coverage for the Phase-8 slice-7 <b>§192 salary-TDS</b> UI surfaced in the cascade (RQ-12/RQ-13; catalog
/// §14; Finance Act 2025): the F11 Statutory-Configuration page enables §192 salary TDS (Enable Salary TDS + the
/// deductor category) and persists across a reload; the per-employee Income-Tax-Declaration (Form 12BB) master
/// captures the old-regime declared deductions and round-trips; the Form 24Q return renders Annexure I (deductee TDS)
/// every quarter + the Q4 Annexure II annual computation; and the Form 16 certificate renders Part A (quarter-wise
/// TDS) + Part B (salary/tax computation). The headline oracle is the A14 golden fixture — NEW gross ₹15,00,000
/// (₹1,25,000/mo) → ₹8,125/month → ₹97,500/year — surfaced through the real shell view models over a throwaway .db.
/// No UI toolkit.
/// </summary>
public sealed class SalaryTdsUiViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    private const string ValidPan = "ABCDE1234F";

    public SalaryTdsUiViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexSalaryTdsUiTests_" + Guid.NewGuid().ToString("N"));
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
        vm.Back();
        return vm;
    }

    private MainWindowViewModel NewSalaryTdsCompany(string name)
    {
        var vm = NewPayrollCompany(name);
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.SalaryTdsEnabled = true;
        Assert.True(page.ApplySalaryTds());
        vm.Back();
        return vm;
    }

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    private static Guid IndirectExpenses(Company c) => c.FindGroupByName("Indirect Expenses")!.Id;
    private static Guid CurrentLiabilities(Company c) => c.FindGroupByName("Current Liabilities")!.Id;

    private readonly record struct Heads(Guid Basic, Guid Tds);

    private static Heads CreateHeads(PayHeadService ph, Company c)
    {
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c));
        var tds = ph.CreatePayHead("TDS on Salary", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: CurrentLiabilities(c),
            incomeTaxComponent: IncomeTaxComponent.TaxDeductedAtSource);
        return new Heads(basic.Id, tds.Id);
    }

    private static SalaryStructureLine[] Lines(Heads h, decimal basic) => new[]
    {
        new SalaryStructureLine(h.Basic, 0, new Money(basic)),
        new SalaryStructureLine(h.Tds, 1),
    };

    /// <summary>Adds a §192 employee (Basic gross + a TDS head, NEW regime + PAN by default) with the structure
    /// effective from the company FY start.</summary>
    private static Guid AddEmployee(Company c, Heads heads, Guid groupId, string name, decimal basic,
        TaxRegime regime = TaxRegime.New, bool withPan = true)
    {
        var pay = new PayrollService(c);
        var e = pay.CreateEmployee(name, groupId);
        var emp = c.FindEmployee(e.Id)!;
        emp.ApplicableTaxRegime = regime;
        if (withPan) emp.Pan = ValidPan;
        new SalaryStructureService(c).DefineForEmployee(e.Id, c.FinancialYearStart, Lines(heads, basic));
        return e.Id;
    }

    private static void PostTwelveMonths(Company c, Guid empId)
    {
        var svc = new PayrollVoucherService(c);
        var d = c.FinancialYearStart;
        for (var i = 0; i < 12; i++)
        {
            var from = d;
            var to = new DateOnly(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));
            svc.Post(from, to, new[] { empId });
            d = d.AddMonths(1);
        }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    // ---------------------------------------------------------------- (1) F11 §192 config: enable + persist + gate

    [Fact]
    public void Enable_salary_tds_config_persists_across_reload()
    {
        const string companyName = "Salary TDS Enable Co";
        var vm = NewPayrollCompany(companyName);
        vm.ShowGstConfig();
        var page = vm.GstConfig!;

        Assert.True(page.ShowSalaryTdsConfig); // shown because Payroll Statutory is on
        Assert.False(vm.Company!.SalaryTdsEnabled);

        page.SalaryTdsEnabled = true;
        page.SelectedSalarySectionCode = page.SalarySectionCodes.First(o => o.Code == "92B");
        Assert.True(page.ApplySalaryTds());

        Assert.True(vm.Company.SalaryTdsEnabled);
        Assert.True(Reload(companyName).SalaryTdsEnabled);

        // The AY label is the FY + 1 (surfaced on the config block).
        Assert.Equal($"{vm.Company.FinancialYearStart.Year + 1}-{(vm.Company.FinancialYearStart.Year + 2) % 100:00}",
            page.SalaryTdsAssessmentYearLabel);
    }

    [Fact]
    public void Disabling_salary_tds_clears_the_switch_and_persists()
    {
        const string companyName = "Salary TDS Off Co";
        var vm = NewSalaryTdsCompany(companyName);
        Assert.True(vm.Company!.SalaryTdsEnabled);

        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.SalaryTdsEnabled = false;
        Assert.True(page.ApplySalaryTds());

        Assert.False(vm.Company.SalaryTdsEnabled);
        Assert.False(Reload(companyName).SalaryTdsEnabled);
    }

    [Fact]
    public void Salary_tds_config_is_hidden_when_payroll_statutory_is_off()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "No Stat Salary TDS Co";
        vm.CreateCompany();
        vm.ShowGstConfig();
        Assert.False(vm.GstConfig!.ShowSalaryTdsConfig);
    }

    // ---------------------------------------------------------------- (2) Form 12BB declaration: persist + round-trip

    [Fact]
    public void Tax_declaration_persists_and_round_trips()
    {
        const string companyName = "Form12BB Co";
        var vm = NewSalaryTdsCompany(companyName);
        var c = vm.Company!;
        var ph = new PayHeadService(c);
        var heads = CreateHeads(ph, c);
        var groupId = new PayrollService(c).CreateEmployeeGroup("Staff").Id;
        var empId = AddEmployee(c, heads, groupId, "Ravi Menon", 125_000m, TaxRegime.Old);
        _storage.Save(c);

        vm.ShowTaxDeclarationMaster();
        Assert.Equal(Screen.TaxDeclarationMaster, vm.CurrentScreen);
        var page = vm.TaxDeclarationMaster!;
        Assert.True(page.HasEmployee);
        Assert.Equal("Old Regime", page.RegimeText);
        Assert.True(page.IsOldRegime);

        page.Section80CText = "150000";
        page.Section80DText = "25000";
        page.HomeLoanInterest24bText = "200000";
        Assert.True(page.Save());

        var decl = c.FindTaxDeclaration(empId);
        Assert.NotNull(decl);
        Assert.Equal(150_000m, decl!.Section80C.Amount);
        Assert.Equal(25_000m, decl.Section80D.Amount);
        Assert.Equal(200_000m, decl.HomeLoanInterest24b.Amount);

        var reloaded = Reload(companyName).FindTaxDeclaration(empId);
        Assert.NotNull(reloaded);
        Assert.Equal(150_000m, reloaded!.Section80C.Amount);
        Assert.Equal(25_000m, reloaded.Section80D.Amount);

        // A re-opened screen loads the persisted figures back into the fields.
        vm.Back();
        vm.ShowTaxDeclarationMaster();
        var reopened = vm.TaxDeclarationMaster!;
        Assert.Equal("150000", reopened.Section80CText);
        Assert.Equal("25000", reopened.Section80DText);
    }

    [Fact]
    public void Tax_declaration_rejects_a_negative_amount()
    {
        var vm = NewSalaryTdsCompany("Form12BB Bad Co");
        var c = vm.Company!;
        var heads = CreateHeads(new PayHeadService(c), c);
        var groupId = new PayrollService(c).CreateEmployeeGroup("Staff").Id;
        AddEmployee(c, heads, groupId, "Sara Iqbal", 100_000m, TaxRegime.Old);
        _storage.Save(c);

        vm.ShowTaxDeclarationMaster();
        var page = vm.TaxDeclarationMaster!;
        page.Section80CText = "-500";
        Assert.False(page.Save());
        Assert.NotNull(page.Message);
    }

    // ---------------------------------------------------------------- (3) Form 24Q — golden NEW ₹15L

    [Fact]
    public void Form24Q_renders_annexure_i_and_ii_for_the_golden_new_15L()
    {
        var vm = NewSalaryTdsCompany("Form24Q Golden Co");
        var c = vm.Company!;
        var heads = CreateHeads(new PayHeadService(c), c);
        var groupId = new PayrollService(c).CreateEmployeeGroup("Staff").Id;
        var empId = AddEmployee(c, heads, groupId, "Anita Rao", 125_000m); // NEW regime, ₹1.25L/mo → ₹15L/yr
        PostTwelveMonths(c, empId);
        _storage.Save(c);

        vm.OpenForm24Q();
        Assert.Equal(Screen.Form24Q, vm.CurrentScreen);
        var page = vm.Form24Q!;
        page.SelectedYear = page.FinancialYears.First(y => y.StartYear == c.FinancialYearStart.Year);

        // Q1 (Apr-Jun): three Annexure-I deductions, each the golden ₹8,125 monthly TDS.
        page.SelectedQuarter = page.Quarters.First(q => q.Quarter == 1);
        Assert.False(page.IsEmpty);
        Assert.Equal(3, page.Deductees.Count);
        Assert.All(page.Deductees, r => Assert.Equal("8,125.00", r.Tds));
        Assert.All(page.Deductees, r => Assert.Equal("92B", r.Section));
        Assert.Equal("Anita Rao", page.Deductees[0].Name);
        Assert.Equal("24,375.00", page.TotalTdsDeducted);   // 3 × 8,125
        Assert.False(page.IsQ4);                             // Annexure II is Q4-only

        // Q4 (Jan-Mar): three more deductions + the Annexure II annual computation (gross ₹15L → ₹97,500 total tax).
        page.SelectedQuarter = page.Quarters.First(q => q.Quarter == 4);
        Assert.True(page.IsQ4);
        var ann = Assert.Single(page.AnnexureII);
        Assert.Equal("New", ann.Regime);
        Assert.Equal("15,00,000.00", ann.GrossSalary);
        Assert.Equal("75,000.00", ann.StandardDeduction);
        Assert.Equal("14,25,000.00", ann.TaxableIncome);
        Assert.Equal("97,500.00", ann.TotalTax);
        Assert.Equal("97,500.00", ann.TaxDeducted);         // fully trued-up year: TDS == total tax

        // Export writes the offline flat file through the injectable seam (no disk); it carries the golden figures.
        string? writtenPath = null; byte[]? written = null;
        Assert.True(page.ExportFvu((p, b) => { writtenPath = p; written = b; }));
        Assert.NotNull(written);
        var text = Encoding.UTF8.GetString(written!);
        Assert.Contains("FORM 24Q", text);
        Assert.Contains("Anita Rao", text);
        Assert.Contains("ANNEXURE II", text);               // Q4 carries the annual computation
        Assert.DoesNotContain("Tally", text, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".txt", writtenPath);
    }

    // ---------------------------------------------------------------- (4) Form 16 — Part A + Part B

    [Fact]
    public void Form16_renders_part_a_and_b_and_exports_a_debranded_pdf()
    {
        var vm = NewSalaryTdsCompany("Form16 Golden Co");
        var c = vm.Company!;
        var heads = CreateHeads(new PayHeadService(c), c);
        var groupId = new PayrollService(c).CreateEmployeeGroup("Staff").Id;
        var empId = AddEmployee(c, heads, groupId, "Anita Rao", 125_000m);
        PostTwelveMonths(c, empId);
        _storage.Save(c);

        vm.OpenForm16();
        Assert.Equal(Screen.Form16, vm.CurrentScreen);
        var page = vm.Form16!;
        page.SelectedYear = page.FinancialYears.First(y => y.StartYear == c.FinancialYearStart.Year);

        var emp = Assert.Single(page.Employees);
        Assert.Equal("Anita Rao", emp.Name);
        Assert.Equal(ValidPan, page.EmployeePan);

        // Part A — four quarters, each 3 × ₹8,125 = ₹24,375, totalling the annual ₹97,500.
        Assert.Equal(4, page.PartA.Count);
        Assert.All(page.PartA, q => Assert.Equal("24,375.00", q.TdsDeducted));
        Assert.Equal("97,500.00", page.TotalTdsDeducted);

        // Part B — the salary/tax computation from the Q4 Annexure II row.
        Assert.True(page.HasPartB);
        Assert.Equal("New Regime", page.Regime);
        Assert.Equal("15,00,000.00", page.GrossSalary);
        Assert.Equal("14,25,000.00", page.TaxableIncome);
        Assert.Equal("97,500.00", page.TotalTax);

        // Export renders a de-branded certificate PDF through the injectable seam (no disk).
        string? writtenPath = null; byte[]? written = null;
        Assert.True(page.ExportPdf((p, b) => { writtenPath = p; written = b; }));
        Assert.NotNull(written);
        Assert.True(written!.Length > 0);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(written, 0, 4)); // a real PDF
        Assert.EndsWith(".pdf", writtenPath);
    }

    // ---------------------------------------------------------------- (5) computation reconciliation (golden ₹8,125)

    [Fact]
    public void The_computed_monthly_tds_is_the_golden_8125_and_matches_the_register()
    {
        var vm = NewSalaryTdsCompany("Golden Recon Co");
        var c = vm.Company!;
        var heads = CreateHeads(new PayHeadService(c), c);
        var groupId = new PayrollService(c).CreateEmployeeGroup("Staff").Id;
        var empId = AddEmployee(c, heads, groupId, "Anita Rao", 125_000m);

        var from = c.FinancialYearStart;
        var to = new DateOnly(from.Year, from.Month, DateTime.DaysInMonth(from.Year, from.Month));
        var result = new PayrollComputationService(c).Compute(empId, from, to);
        Assert.Equal(new Money(8_125m), result.SalaryTdsDeducted);

        // The posted first month surfaces the same ₹8,125 in the Form 24Q Annexure-I register.
        new PayrollVoucherService(c).Post(from, to, new[] { empId });
        _storage.Save(c);

        vm.OpenForm24Q();
        var page = vm.Form24Q!;
        page.SelectedYear = page.FinancialYears.First(y => y.StartYear == c.FinancialYearStart.Year);
        page.SelectedQuarter = page.Quarters.First(q => q.Quarter == 1);
        var row = Assert.Single(page.Deductees);
        Assert.Equal("8,125.00", row.Tds);
    }

    // ---------------------------------------------------------------- (5b) WI-6 — the §192 TDS pay-head option is
    // reachable from the Pay Head master PICKER, and a payroll run through the SAME path the UI uses computes a
    // NON-ZERO salary TDS that matches the SalaryIncomeTax §192 engine. This is the regression lock for WI-6: the
    // picker used to omit IncomeTaxComponent.TaxDeductedAtSource, so PayrollComputationService's §192 gate could
    // never be satisfied by a UI-created head and salary TDS was unconditionally ₹0 on every payslip.

    [Fact]
    public void Pay_head_master_picker_offers_the_salary_tds_marker_and_a_run_computes_non_zero_tds()
    {
        var vm = NewSalaryTdsCompany("PayHead TDS Picker Co");
        var c = vm.Company!;

        // (a) A Basic earnings head, created through the master screen (₹1.25L/mo flat → ₹15L/yr, the NEW-regime
        //     golden). NEGATIVE GATE: the §192 TDS marker is NOT offered on an Earnings head.
        vm.ShowPayHeadMaster();
        var m = vm.PayHeadMaster!;
        m.Name = "Basic";
        m.SelectedType = m.Types.Single(t => t.Value == PayHeadType.Earnings);
        m.SelectedCalcType = m.CalcTypes.Single(cc => cc.Value == PayHeadCalculationType.FlatRate);
        m.SelectedGroup = m.GroupOptions.Single(o => o.Group is { } g && g.Name == "Indirect Expenses");
        Assert.DoesNotContain(m.IncomeTaxComponents, i => i.Value == IncomeTaxComponent.TaxDeductedAtSource);
        m.SelectedIncomeTaxComponent = m.IncomeTaxComponents.Single(i => i.Value == IncomeTaxComponent.BasicSalary);
        Assert.True(m.Create(), m.Message);

        // (b) The salary-TDS head, created through the master screen. The picker MUST now offer the §192 marker on an
        //     Employees' Statutory Deductions head — the WI-6 fix. (Before the fix this option did not exist, so the
        //     head below could not be tagged and salary TDS stayed ₹0.)
        vm.ShowPayHeadMaster();
        m = vm.PayHeadMaster!;
        m.Name = "TDS on Salary";
        m.SelectedType = m.Types.Single(t => t.Value == PayHeadType.EmployeesStatutoryDeductions);
        m.SelectedCalcType = m.CalcTypes.Single(cc => cc.Value == PayHeadCalculationType.AsUserDefinedValue);
        m.SelectedGroup = m.GroupOptions.Single(o => o.Group is { } g && g.Name == "Current Liabilities");
        var tdsOption = m.IncomeTaxComponents.SingleOrDefault(i => i.Value == IncomeTaxComponent.TaxDeductedAtSource);
        Assert.NotNull(tdsOption); // WI-6: reachable from the UI picker
        m.SelectedIncomeTaxComponent = tdsOption;
        Assert.True(m.Create(), m.Message);

        var tdsHead = c.FindPayHeadByName("TDS on Salary")!;
        Assert.Equal(IncomeTaxComponent.TaxDeductedAtSource, tdsHead.IncomeTaxComponent);

        // (c) Wire an employee + structure and run payroll through the real §192 engine.
        var basic = c.FindPayHeadByName("Basic")!;
        var groupId = new PayrollService(c).CreateEmployeeGroup("Staff").Id;
        var empId = AddEmployee(c, new Heads(basic.Id, tdsHead.Id), groupId, "Anita Rao", 125_000m);

        var from = c.FinancialYearStart;
        var to = new DateOnly(from.Year, from.Month, DateTime.DaysInMonth(from.Year, from.Month));
        var result = new PayrollComputationService(c).Compute(empId, from, to);

        // HEADLINE: salary TDS is NON-ZERO (the bug made it unconditionally ₹0) …
        Assert.NotEqual(Money.Zero, result.SalaryTdsDeducted);

        // … and equals the SalaryIncomeTax §192 average-rate monthly figure (golden ₹8,125/mo on ₹15L NEW).
        var annualTax = SalaryIncomeTax.ComputeAnnual(
            SalaryIncomeTax.TaxableIncome(15_00_000m, 0m, TaxRegime.New), TaxRegime.New).AnnualTax;
        var expectedMonthly = SalaryIncomeTax.MonthlyTds(annualTax, Money.Zero, SalaryIncomeTax.MonthsRemainingInFy(to));
        Assert.Equal(expectedMonthly, result.SalaryTdsDeducted);
        Assert.Equal(new Money(8_125m), result.SalaryTdsDeducted);
    }

    // ---------------------------------------------------------------- (6) gating

    [Fact]
    public void Salary_tds_reports_are_gated_on_the_enable_switch()
    {
        var vm = NewPayrollCompany("Salary TDS Gated Co"); // payroll on, salary-TDS OFF
        vm.OpenForm24Q();
        Assert.NotEqual(Screen.Form24Q, vm.CurrentScreen);
        Assert.Null(vm.Form24Q);

        vm.OpenForm16();
        Assert.NotEqual(Screen.Form16, vm.CurrentScreen);
        Assert.Null(vm.Form16);

        vm.ShowTaxDeclarationMaster();
        Assert.NotEqual(Screen.TaxDeclarationMaster, vm.CurrentScreen);
        Assert.Null(vm.TaxDeclarationMaster);
    }
}
