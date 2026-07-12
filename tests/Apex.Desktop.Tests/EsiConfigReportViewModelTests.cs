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
/// End-to-end coverage for the Phase-8 slice-5 <b>Employees' State Insurance</b> UI surfaced in the cascade (RQ-10;
/// catalog §14): the F11 Statutory-Configuration page enrols the establishment for ESI (Enable ESI + the 17-digit
/// employer code) and persists it across a reload; the Employee master enforces the per-member <b>10-digit IP /
/// Insurance Number</b> (<c>^\d{10}$</c>) before a member can be ESI-applicable; and the ESI Monthly Contribution
/// report renders the hand-derived golden split for the ₹20,000 member (employee ₹150 / employer ₹650), reflects the
/// contribution-period continuation, and exports the ESIC monthly-contribution offline file. Drives the real shell
/// view models over a throwaway .db — no UI toolkit.
/// </summary>
public sealed class EsiConfigReportViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    private const string Ip = "3100123456";           // 10-digit IP / Insurance Number
    private const string EmployerCode = "31001234560000123"; // 17-digit ESIC establishment code

    public EsiConfigReportViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexEsiUiTests_" + Guid.NewGuid().ToString("N"));
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

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    private static Guid IndirectExpenses(Company c) => c.FindGroupByName("Indirect Expenses")!.Id;
    private static Guid CurrentLiabilities(Company c) => c.FindGroupByName("Current Liabilities")!.Id;

    private readonly record struct EsiHeads(Guid Basic, Guid EmployeeEsi, Guid EmployerEsi);

    private static EsiHeads CreateEsiHeads(PayHeadService ph, Company c)
    {
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c), partOfEsiWages: true);
        var ee = ph.CreatePayHead("Employee ESI", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: CurrentLiabilities(c),
            esiComponent: EsiStatutoryComponent.EmployeeStateInsurance);
        var er = ph.CreatePayHead("Employer ESI", PayHeadType.EmployersStatutoryContributions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: CurrentLiabilities(c),
            esiComponent: EsiStatutoryComponent.EmployerStateInsurance);
        return new EsiHeads(basic.Id, ee.Id, er.Id);
    }

    private static SalaryStructureLine[] Lines(EsiHeads h, decimal basic) => new[]
    {
        new SalaryStructureLine(h.Basic, 0, new Money(basic)),
        new SalaryStructureLine(h.EmployeeEsi, 1),
        new SalaryStructureLine(h.EmployerEsi, 2),
    };

    /// <summary>Builds the ₹20,000 ESI golden member (Basic as ESI wages, ESI enrolled, member ESI-applicable with a
    /// 10-digit IP), with the structure effective from 1-Apr of the company FY.</summary>
    private static (Guid EmployeeId, EsiHeads Heads) BuildEsiGolden(Company c, decimal basic = 20000m)
    {
        var pay = new PayrollService(c);
        pay.EnableEsi(employerCode: EmployerCode);        // 0.75% / 3.25% default, establishment enrolled
        var ph = new PayHeadService(c);
        var heads = CreateEsiHeads(ph, c);
        var emp = pay.CreateEmployee("Sanjay Kumar", pay.CreateEmployeeGroup("Staff").Id, esiNumber: Ip);
        pay.SetEmployeeEsiDetails(emp.Id, applicable: true);
        new SalaryStructureService(c).DefineForEmployee(emp.Id, c.FinancialYearStart, Lines(heads, basic));
        return (emp.Id, heads);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    // ---------------------------------------------------------------- (1) F11 ESI config: enable + persist

    [Fact]
    public void Enable_esi_config_sets_the_employer_code_and_persists_across_reload()
    {
        const string companyName = "ESI Enable Co";
        var vm = NewPayrollCompany(companyName);
        vm.ShowGstConfig();
        var page = vm.GstConfig!;

        Assert.True(page.ShowEsiConfig); // shown because Payroll Statutory is on
        page.EsiEnabled = true;
        page.EsiEmployerCode = EmployerCode;
        Assert.True(page.ApplyEsi());

        Assert.NotNull(vm.Company!.EsiConfig);
        Assert.Equal(EsiConfig.DefaultEmployeeRateBasisPoints, vm.Company.EsiConfig!.EmployeeRateBasisPoints); // 75 (0.75%)
        Assert.Equal(EsiConfig.DefaultEmployerRateBasisPoints, vm.Company.EsiConfig.EmployerRateBasisPoints);  // 325 (3.25%)
        Assert.Equal(EmployerCode, vm.Company.EsiConfig.EmployerCode);

        var reloaded = Reload(companyName);
        Assert.NotNull(reloaded.EsiConfig);
        Assert.Equal(EmployerCode, reloaded.EsiConfig!.EmployerCode);
        Assert.Equal(75, reloaded.EsiConfig.EmployeeRateBasisPoints);
        Assert.Equal(325, reloaded.EsiConfig.EmployerRateBasisPoints);
    }

    [Fact]
    public void A_malformed_employer_code_is_rejected_and_leaves_esi_unenrolled()
    {
        var vm = NewPayrollCompany("ESI Bad Code Co");
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.EsiEnabled = true;
        page.EsiEmployerCode = "12345";      // not 17 digits
        Assert.False(page.ApplyEsi());
        Assert.NotNull(page.EsiMessage);
        Assert.Null(vm.Company!.EsiConfig);  // nothing enrolled
        Assert.False(page.EsiEnabled);       // toggle reverted to the real (off) state
    }

    [Fact]
    public void Disabling_esi_clears_the_config_and_persists()
    {
        const string companyName = "ESI Off Co";
        var vm = NewPayrollCompany(companyName);
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.EsiEnabled = true;
        Assert.True(page.ApplyEsi());
        Assert.NotNull(vm.Company!.EsiConfig);

        page.EsiEnabled = false;
        Assert.True(page.ApplyEsi());
        Assert.Null(vm.Company.EsiConfig);
        Assert.Null(Reload(companyName).EsiConfig);
    }

    [Fact]
    public void Esi_config_is_hidden_when_payroll_statutory_is_off()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "No Stat ESI Co";
        vm.CreateCompany();
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        Assert.False(page.ShowEsiConfig);
    }

    // ---------------------------------------------------------------- (2) per-employee ESI details + 10-digit IP

    [Fact]
    public void Esi_applicable_employee_requires_a_valid_10_digit_ip_number()
    {
        var vm = NewPayrollCompany("ESI Emp Guard Co");
        var c = vm.Company!;
        new PayrollService(c).CreateEmployeeGroup("Staff");

        vm.ShowEmployeeMaster();
        var m = vm.EmployeeMaster!;
        Assert.True(m.ShowEsiDetails); // Payroll Statutory is on

        // ESI-applicable with a blank IP number is rejected with a friendly message; nothing is created.
        m.Name = "No IP";
        m.SelectedGroup = m.GroupOptions.First();
        m.EsiApplicable = true;
        m.EsiNumber = string.Empty;
        Assert.False(m.Create());
        Assert.NotNull(m.Message);
        Assert.Contains("IP", m.Message!);
        Assert.Null(c.FindEmployeeByName("No IP"));

        // A malformed IP (not 10 digits — e.g. the 17-digit employer code) is likewise rejected.
        m.Name = "Bad IP";
        m.EsiNumber = EmployerCode;
        Assert.False(m.Create());
        Assert.Contains("IP", m.Message!);
        Assert.Null(c.FindEmployeeByName("Bad IP"));

        // A valid 10-digit IP number creates an ESI-applicable member.
        m.Name = "Good IP";
        m.EsiNumber = Ip;
        Assert.True(m.Create(), m.Message);
        var emp = c.FindEmployeeByName("Good IP")!;
        Assert.True(emp.EsiApplicable);
        Assert.Equal(Ip, emp.EsiNumber);
    }

    // ---------------------------------------------------------------- (3) ESI Monthly Contribution report — golden

    [Fact]
    public void Esi_report_renders_the_golden_ee_150_er_650_for_the_20000_member()
    {
        var vm = NewPayrollCompany("ESI Report Co");
        var c = vm.Company!;
        BuildEsiGolden(c, basic: 20000m);
        _storage.Save(c);

        vm.OpenEsiContributionReport();
        Assert.Equal(Screen.EsiContributionReport, vm.CurrentScreen);
        var page = vm.EsiContributionReport!;

        // The establishment's first wage month (April of the FY the golden structure is effective from).
        page.SelectedYear = page.FinancialYears.First(y => y.StartYear == c.FinancialYearStart.Year);
        page.SelectedMonth = page.Months.First(mo => mo.FirstDay == c.FinancialYearStart);

        Assert.False(page.IsEmpty);
        var row = Assert.Single(page.Members);
        Assert.Equal(Ip, row.IpNumber);
        Assert.Equal("20,000", row.EsiWages);
        Assert.Equal("150", row.EmployeeContribution);   // ceil(0.75% × 20,000)
        Assert.Equal("650", row.EmployerContribution);   // ceil(3.25% × 20,000)

        // Footings: EE 150, ER 650, total 800.
        Assert.Equal("20,000", page.TotalWagesText);
        Assert.Equal("150", page.TotalEmployeeText);
        Assert.Equal("650", page.TotalEmployerText);
        Assert.Equal("800", page.GrandTotalText);

        // Export writes the ESIC monthly-contribution file through the injectable seam (no disk).
        string? writtenPath = null;
        byte[]? written = null;
        Assert.True(page.ExportReturn((p, b) => { writtenPath = p; written = b; }));
        Assert.NotNull(written);
        var text = Encoding.UTF8.GetString(written!);
        Assert.Contains(Ip, text);            // the member IP number keys the line
        Assert.Contains("20000", text);       // the ESI wages (whole rupees, no paisa)
        Assert.EndsWith(".csv", writtenPath);
    }

    [Fact]
    public void Esi_report_is_gated_on_payroll_statutory()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "ESI Gated Co";
        vm.CreateCompany();
        vm.OpenEsiContributionReport();          // no-op: Payroll Statutory is off
        Assert.NotEqual(Screen.EsiContributionReport, vm.CurrentScreen);
        Assert.Null(vm.EsiContributionReport);
    }

    // ---------------------------------------------------------------- (4) contribution-period continuation

    [Fact]
    public void Report_reflects_continuation_covered_mid_period_then_out_from_the_next_period()
    {
        var vm = NewPayrollCompany("ESI Continuation Co");
        var c = vm.Company!;
        var fyStart = c.FinancialYearStart;              // 1-Apr of the seeded FY (CP1 starts here)
        var august = fyStart.AddMonths(4);               // month 5 of CP1 (Apr–Sep)
        var october = fyStart.AddMonths(6);              // first month of CP2 (Oct–Mar)
        var (empId, heads) = BuildEsiGolden(c, basic: 20000m); // covered at the CP1 start (≤ 21,000)
        // A revision to ₹22,000 effective from August pushes the wage over the ceiling mid-CP1.
        new SalaryStructureService(c).DefineForEmployee(empId, august, Lines(heads, 22000m));
        _storage.Save(c);

        vm.OpenEsiContributionReport();
        var page = vm.EsiContributionReport!;
        page.SelectedYear = page.FinancialYears.First(y => y.StartYear == fyStart.Year);

        // August (still CP1): covered (frozen from the CP1 start), charged on the FULL ₹22,000 — EE ceil(0.75%×22,000)=165.
        page.SelectedMonth = page.Months.First(mo => mo.FirstDay == august);
        var aug = Assert.Single(page.Members);
        Assert.Equal("22,000", aug.EsiWages);
        Assert.Equal("165", aug.EmployeeContribution);
        Assert.Equal("715", aug.EmployerContribution);   // ceil(3.25% × 22,000)

        // October (CP2 re-evaluates from the 1-Oct wage ₹22,000 > 21,000) ⇒ member drops out: 0 wages / 0 / 0.
        page.SelectedMonth = page.Months.First(mo => mo.FirstDay == october);
        var oct = Assert.Single(page.Members);
        Assert.Equal("0", oct.EsiWages);
        Assert.Equal("0", oct.EmployeeContribution);
        Assert.Equal("0", oct.EmployerContribution);
        Assert.Equal("0", page.GrandTotalText);
    }
}
