using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// End-to-end coverage for the Phase-8 slice-9 <b>Gratuity + Bonus</b> UI surfaced in the cascade (RQ-14/RQ-15;
/// catalog §14): the F11 Statutory-Configuration page enrols the establishment for Gratuity (cap + population) and
/// statutory Bonus (rate + ceilings + prorate) and persists across a reload; the Gratuity Provision register renders
/// the A14-verified golden accrual (Basic + DA ₹26,000 over 10 years ⇒ ₹1,50,000, vested) and posts the delta voucher;
/// the Bonus register renders the golden ₹6,997 at 8.33% (Basic + DA ₹18,000 capped at ₹7,000); and the consolidated
/// Statutory Reports → Payroll nav lists all five payroll statutory reports. Drives the real shell view models over a
/// throwaway .db — no UI toolkit.
/// </summary>
public sealed class GratuityBonusConfigReportViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public GratuityBonusConfigReportViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexGratuityBonusUiTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
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

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    private static Guid IndirectExpenses(Company c) => c.FindGroupByName("Indirect Expenses")!.Id;

    /// <summary>Adds an employee with a single Basic + DA earning (flagged UseForGratuity) as the whole gratuity /
    /// bonus wage base, effective from the company FY start, with the given join date.</summary>
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

    private static Guid CreateBasicHead(Company c) =>
        new PayHeadService(c).CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c), useForGratuity: true).Id;

    // ---------------------------------------------------------------- (1) F11 gratuity config: enable + persist

    [Fact]
    public void Enable_gratuity_config_sets_cap_and_population_and_persists_across_reload()
    {
        const string companyName = "Gratuity Enable Co";
        var vm = NewPayrollCompany(companyName);
        vm.ShowGstConfig();
        var page = vm.GstConfig!;

        Assert.True(page.ShowGratuityConfig); // shown because Payroll Statutory is on
        page.GratuityEnabled = true;
        page.GratuityCapText = "2000000";
        page.SelectedGratuityPopulation = page.GratuityPopulations.First(o => o.Value == GratuityProvisionPopulation.VestedOnly);
        Assert.True(page.ApplyGratuity());

        Assert.NotNull(vm.Company!.GratuityConfig);
        Assert.Equal(2_000_000m, vm.Company.GratuityConfig!.CapAmount.Amount);
        Assert.Equal(GratuityProvisionPopulation.VestedOnly, vm.Company.GratuityConfig.Population);

        var reloaded = Reload(companyName);
        Assert.NotNull(reloaded.GratuityConfig);
        Assert.Equal(2_000_000m, reloaded.GratuityConfig!.CapAmount.Amount);
        Assert.Equal(GratuityProvisionPopulation.VestedOnly, reloaded.GratuityConfig.Population);
    }

    [Fact]
    public void Disabling_gratuity_clears_the_config_and_persists()
    {
        const string companyName = "Gratuity Off Co";
        var vm = NewPayrollCompany(companyName);
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.GratuityEnabled = true;
        Assert.True(page.ApplyGratuity());
        Assert.NotNull(vm.Company!.GratuityConfig);

        page.GratuityEnabled = false;
        Assert.True(page.ApplyGratuity());
        Assert.Null(vm.Company.GratuityConfig);
        Assert.Null(Reload(companyName).GratuityConfig);
    }

    [Fact]
    public void Gratuity_config_is_hidden_when_payroll_statutory_is_off()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "No Stat Gratuity Co";
        vm.CreateCompany();
        vm.ShowGstConfig();
        Assert.False(vm.GstConfig!.ShowGratuityConfig);
    }

    // ---------------------------------------------------------------- (2) F11 bonus config: enable + persist

    [Fact]
    public void Enable_bonus_config_sets_rate_ceiling_prorate_and_persists_across_reload()
    {
        const string companyName = "Bonus Enable Co";
        var vm = NewPayrollCompany(companyName);
        vm.ShowGstConfig();
        var page = vm.GstConfig!;

        Assert.True(page.ShowBonusConfig);
        page.BonusEnabled = true;
        page.BonusRatePercentText = "8.33";
        page.BonusCalculationCeilingText = "7000";
        page.BonusMinimumWageText = "0";
        page.BonusProrate = true;
        Assert.True(page.ApplyBonus());

        Assert.NotNull(vm.Company!.BonusConfig);
        Assert.Equal(833, vm.Company.BonusConfig!.RateBasisPoints);   // 8.33% ⇒ 833 bp
        Assert.Equal(7_000m, vm.Company.BonusConfig.CalculationCeiling.Amount);
        Assert.True(vm.Company.BonusConfig.Prorate);

        var reloaded = Reload(companyName);
        Assert.NotNull(reloaded.BonusConfig);
        Assert.Equal(833, reloaded.BonusConfig!.RateBasisPoints);
        Assert.Equal(7_000m, reloaded.BonusConfig.CalculationCeiling.Amount);
    }

    [Fact]
    public void Bonus_rate_is_clamped_into_the_statutory_band_on_apply()
    {
        var vm = NewPayrollCompany("Bonus Clamp Co");
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.BonusEnabled = true;
        page.BonusRatePercentText = "25"; // above the §11 20% ceiling ⇒ clamped to 2000 bp
        Assert.True(page.ApplyBonus());
        Assert.Equal(2000, vm.Company!.BonusConfig!.RateBasisPoints);
        Assert.Equal("20", page.BonusRatePercentText); // reflected back
    }

    [Fact]
    public void Bonus_config_is_hidden_when_payroll_statutory_is_off()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "No Stat Bonus Co";
        vm.CreateCompany();
        vm.ShowGstConfig();
        Assert.False(vm.GstConfig!.ShowBonusConfig);
    }

    // ---------------------------------------------------------------- (3) Gratuity register — golden ₹1,50,000

    [Fact]
    public void Gratuity_register_renders_the_golden_150000_and_posts_the_delta()
    {
        var vm = NewPayrollCompany("Gratuity Report Co");
        var c = vm.Company!;
        new PayrollService(c).EnableGratuity();
        var basic = CreateBasicHead(c);
        var groupId = new PayrollService(c).CreateEmployeeGroup("Staff").Id;
        // As-on 30-Apr-<fyStart>: joined 30-Apr-(fyStart-10) ⇒ exactly 10 completed years, vested. Basic+DA 26,000.
        var fyStart = c.FinancialYearStart;
        AddEmployee(c, basic, groupId, "Anil Rao", "E001", fyStart.AddYears(-10).AddDays(29), 26_000m);
        _storage.Save(c);

        vm.OpenGratuityProvisionRegister();
        Assert.Equal(Screen.GratuityProvisionRegister, vm.CurrentScreen);
        var page = vm.GratuityProvisionRegister!;
        page.SelectedYear = page.FinancialYears.First(y => y.StartYear == fyStart.Year);
        page.SelectedMonth = page.Months.First(m => m.FirstDay == fyStart); // as-on = 30-Apr-<fyStart>

        Assert.False(page.IsEmpty);
        var row = Assert.Single(page.Rows);
        Assert.Equal("Anil Rao", row.EmployeeName);
        Assert.Equal("10", row.CompletedYears);
        Assert.Equal("Yes", row.Vested);
        Assert.Equal("26,000", row.BasicPlusDa);
        Assert.Equal("1,50,000", row.AccruedGratuity);
        Assert.Equal("1,50,000", page.TotalLiabilityText);
        Assert.Equal("0", page.PriorProvisionText);
        Assert.Equal("1,50,000", page.DeltaText);
        Assert.True(page.CanPost);

        // Post the provision: Dr Gratuity Expense / Cr Gratuity Provision ₹1,50,000, balanced + persisted.
        Assert.True(page.PostProvision());
        Assert.Equal(150_000m,
            new PayrollVoucherService(c).PriorGratuityProvisionBalance(page.AsOn.AddDays(1)).Amount);
        // After posting, the delta falls to ₹0 and a re-post is refused (no same-date double-count).
        Assert.Equal("1,50,000", page.PriorProvisionText);
        Assert.Equal("0", page.DeltaText);
        Assert.False(page.CanPost);
        Assert.False(page.PostProvision());

        // The persisted company carries the posted provision liability.
        Assert.Equal(150_000m,
            Reload("Gratuity Report Co").Vouchers
                .SelectMany(v => v.Lines)
                .Where(l => l.LedgerId == c.FindLedgerByName(PayrollVoucherService.GratuityProvisionLedgerName)!.Id
                            && l.Side == DrCr.Credit)
                .Sum(l => l.Amount.Amount));
    }

    [Fact]
    public void Re_posting_the_same_period_end_after_the_accrual_rises_posts_only_the_delta_and_reconciles()
    {
        // Regression (adversarial finding): the register VM gates on the INCLUSIVE prior (asOn+1), so a genuine same-date
        // true-up passes the guard, but the engine derived its prior STRICTLY-BEFORE the voucher date and re-posted the
        // whole accrued — leaving the Gratuity Provision ledger double-counted and no longer reconciling to the register.
        var vm = NewPayrollCompany("Gratuity Same Date Co");
        var c = vm.Company!;
        new PayrollService(c).EnableGratuity();
        var basic = CreateBasicHead(c);
        var groupId = new PayrollService(c).CreateEmployeeGroup("Staff").Id;
        var fyStart = c.FinancialYearStart;
        // Anil: exactly 10 completed years as-on the FY-start month end ⇒ ₹1,50,000.
        AddEmployee(c, basic, groupId, "Anil Rao", "E001", fyStart.AddYears(-10).AddDays(29), 26_000m);
        _storage.Save(c);

        vm.OpenGratuityProvisionRegister();
        var page = vm.GratuityProvisionRegister!;
        page.SelectedYear = page.FinancialYears.First(y => y.StartYear == fyStart.Year);
        page.SelectedMonth = page.Months.First(m => m.FirstDay == fyStart); // as-on = FY-start month end
        Assert.True(page.PostProvision()); // posts ₹1,50,000
        Assert.Equal(150_000m, new PayrollVoucherService(c).PriorGratuityProvisionBalance(page.AsOn.AddDays(1)).Amount);

        // A late-added active member (exactly 5 years, vested) lifts the accrued to ₹2,25,000 for the SAME period end.
        AddEmployee(c, basic, groupId, "Bimal Sen", "E002", fyStart.AddYears(-5).AddDays(29), 26_000m);
        page.Rebuild();
        Assert.Equal("2,25,000", page.TotalLiabilityText);
        Assert.Equal("1,50,000", page.PriorProvisionText); // inclusive of the same-date voucher already posted
        Assert.Equal("75,000", page.DeltaText);
        Assert.True(page.CanPost);

        // Re-strike the SAME as-on: only the ₹75,000 increment posts, and the ledger reconciles to the register total
        // ₹2,25,000 — NOT a double-counted ₹3,75,000.
        Assert.True(page.PostProvision());
        Assert.Equal(225_000m, new PayrollVoucherService(c).PriorGratuityProvisionBalance(page.AsOn.AddDays(1)).Amount);
        Assert.Equal("2,25,000", page.TotalLiabilityText);
        Assert.Equal("2,25,000", page.PriorProvisionText);
        Assert.Equal("0", page.DeltaText);
        Assert.False(page.CanPost);
    }

    [Fact]
    public void Gratuity_register_is_gated_on_enrolment()
    {
        var vm = NewPayrollCompany("Gratuity Gated Co"); // payroll statutory on, but gratuity NOT enrolled
        vm.OpenGratuityProvisionRegister();
        Assert.NotEqual(Screen.GratuityProvisionRegister, vm.CurrentScreen);
        Assert.Null(vm.GratuityProvisionRegister);
    }

    // ---------------------------------------------------------------- (4) Bonus register — golden ₹6,997

    [Fact]
    public void Bonus_register_renders_the_golden_6997_at_8_33_percent()
    {
        var vm = NewPayrollCompany("Bonus Report Co");
        var c = vm.Company!;
        new PayrollService(c).EnableStatutoryBonus(); // 8.33%, ₹7,000 ceiling, no min-wage, prorate
        var basic = CreateBasicHead(c);
        var groupId = new PayrollService(c).CreateEmployeeGroup("Staff").Id;
        // Basic+DA 18,000 (≤ ₹21,000 eligible) → capped at ₹7,000; full year (joined before FY) → 7,000×12×8.33% = 6,997.
        AddEmployee(c, basic, groupId, "Bina Roy", "B001", c.FinancialYearStart.AddYears(-2), 18_000m);
        // A high earner (Basic+DA 25,000 > ₹21,000) is excluded from the register entirely.
        AddEmployee(c, basic, groupId, "Chetan Roy", "B002", c.FinancialYearStart.AddYears(-2), 25_000m);
        _storage.Save(c);

        vm.OpenBonusRegister();
        Assert.Equal(Screen.BonusRegister, vm.CurrentScreen);
        var page = vm.BonusRegister!;
        page.SelectedYear = page.FinancialYears.First(y => y.StartYear == c.FinancialYearStart.Year);

        Assert.False(page.IsEmpty);
        var row = Assert.Single(page.Rows); // only the ≤ ₹21,000 member appears
        Assert.Equal("Bina Roy", row.EmployeeName);
        Assert.Equal("Yes", row.Eligible);
        Assert.Equal("18,000", row.ActualBasicDa);
        Assert.Equal("7,000", row.CappedBase);
        Assert.Equal("8.33%", row.RatePercent);
        Assert.Equal("6,997", row.AnnualBonus);
        Assert.Equal("6,997", page.TotalBonusText);
    }

    [Fact]
    public void Bonus_register_is_gated_on_enrolment()
    {
        var vm = NewPayrollCompany("Bonus Gated Co"); // payroll statutory on, but bonus NOT enrolled
        vm.OpenBonusRegister();
        Assert.NotEqual(Screen.BonusRegister, vm.CurrentScreen);
        Assert.Null(vm.BonusRegister);
    }

    // ---------------------------------------------------------------- (5) consolidated Statutory Reports → Payroll nav

    [Fact]
    public void Payroll_statutory_nav_lists_all_five_reports_when_gratuity_and_bonus_are_enrolled()
    {
        var vm = NewPayrollCompany("Payroll Stat Nav Co");
        var c = vm.Company!;
        new PayrollService(c).EnableGratuity();
        new PayrollService(c).EnableStatutoryBonus();
        _storage.Save(c);

        vm.ShowPayrollStatutoryReportsMenu();
        Assert.Equal(GatewayMenu.PayrollStatutoryReports, vm.CurrentGatewayMenu);

        var items = vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Equal(
            new[]
            {
                "PF ECR / Challan", "ESI Monthly Contribution", "PT Deduction Register",
                "Gratuity Provision", "Bonus Register",
            },
            items);

        // Never a flat dump — one "Payroll Statutory" section header.
        var headers = vm.Menu.Where(m => m.IsHeader).Select(m => m.Label).ToArray();
        Assert.Equal(new[] { "Payroll Statutory" }, headers);
    }

    [Fact]
    public void Payroll_statutory_nav_omits_gratuity_and_bonus_until_enrolled()
    {
        var vm = NewPayrollCompany("Payroll Stat Nav Bare Co"); // no gratuity / bonus enrolment
        vm.ShowPayrollStatutoryReportsMenu();

        var items = vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.DoesNotContain("Gratuity Provision", items);
        Assert.DoesNotContain("Bonus Register", items);
        // The three earlier payroll statutory registers still list (gated only on Payroll Statutory).
        Assert.Contains("PF ECR / Challan", items);
    }

    [Theory]
    [InlineData("Gratuity Provision", Screen.GratuityProvisionRegister)]
    [InlineData("Bonus Register", Screen.BonusRegister)]
    public void Activating_a_payroll_statutory_report_opens_that_screen(string label, Screen expected)
    {
        var vm = NewPayrollCompany("Payroll Stat Route Co");
        var c = vm.Company!;
        new PayrollService(c).EnableGratuity();
        new PayrollService(c).EnableStatutoryBonus();
        _storage.Save(c);

        vm.ShowPayrollStatutoryReportsMenu();
        while (vm.Menu[vm.SelectedIndex].Label != label) vm.MoveDown();
        vm.ActivateSelected();

        Assert.Equal(expected, vm.CurrentScreen);
    }
}
