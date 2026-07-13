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
/// End-to-end coverage for the Phase-8 slice-4 <b>Provident Fund</b> UI surfaced in the cascade (RQ-9; catalog §14):
/// the F11 Statutory-Configuration page enrols the establishment for PF (EPF 12%/10% toggle + establishment code +
/// cap-at-ceiling) and persists it across a reload; the Employee master captures the per-member PF details and
/// enforces the 12-digit UAN (<c>^\d{12}$</c>) before a member can be PF-applicable; and the PF ECR / Challan
/// report renders the hand-derived golden challan totals (A/c 1/2/10/21/22) for the ₹15,000 member and exports the
/// EPFO ECR 2.0 flat file. Drives the real shell view models over a throwaway .db — no UI toolkit.
/// </summary>
public sealed class PfConfigReportViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public PfConfigReportViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexPfUiTests_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Builds the ₹15,000 PF golden member (Basic in PF wages, PF enrolled, member PF-applicable).</summary>
    private static Guid BuildPfGolden(Company c)
    {
        var pay = new PayrollService(c);
        pay.EnableProvidentFund(); // 12% default, establishment enrolled
        var ph = new PayHeadService(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c), partOfPfWages: true);
        var emp = pay.CreateEmployee("Sanjay Kumar", pay.CreateEmployeeGroup("Staff").Id, uan: "100000000001");
        pay.SetEmployeePfDetails(emp.Id, applicable: true);
        new SalaryStructureService(c).DefineForEmployee(emp.Id, c.FinancialYearStart, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(15000m)),
        });
        return emp.Id;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    // ---------------------------------------------------------------- (1) F11 PF config: enable + persist

    [Fact]
    public void Enable_pf_config_sets_rate_code_cap_and_persists_across_reload()
    {
        const string companyName = "PF Enable Co";
        var vm = NewPayrollCompany(companyName);
        vm.ShowGstConfig();
        var page = vm.GstConfig!;

        Assert.True(page.ShowPfConfig); // shown because Payroll Statutory is on
        page.PfEnabled = true;
        page.PfEstablishmentCode = "MHBAN0012345000";
        page.PfCapWagesAtCeiling = true;
        Assert.True(page.ApplyPf());

        Assert.NotNull(vm.Company!.PfConfig);
        Assert.Equal(PfConfig.DefaultEpfRateBasisPoints, vm.Company.PfConfig!.EpfRateBasisPoints); // 12%
        Assert.Equal("MHBAN0012345000", vm.Company.PfConfig.EstablishmentCode);
        Assert.True(vm.Company.PfConfig.CapWagesAtCeiling);

        var reloaded = Reload(companyName);
        Assert.NotNull(reloaded.PfConfig);
        Assert.Equal(1200, reloaded.PfConfig!.EpfRateBasisPoints);
        Assert.Equal("MHBAN0012345000", reloaded.PfConfig.EstablishmentCode);
    }

    [Fact]
    public void Enable_pf_reduced_rate_records_10_percent()
    {
        var vm = NewPayrollCompany("PF 10pc Co");
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.PfEnabled = true;
        page.PfReducedRate = true; // 10% special establishment
        Assert.True(page.ApplyPf());
        Assert.Equal(PfConfig.ReducedEpfRateBasisPoints, vm.Company!.PfConfig!.EpfRateBasisPoints); // 1000
    }

    [Fact]
    public void Disabling_pf_clears_the_config_and_persists()
    {
        const string companyName = "PF Off Co";
        var vm = NewPayrollCompany(companyName);
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.PfEnabled = true;
        Assert.True(page.ApplyPf());
        Assert.NotNull(vm.Company!.PfConfig);

        page.PfEnabled = false;
        Assert.True(page.ApplyPf());
        Assert.Null(vm.Company.PfConfig);
        Assert.Null(Reload(companyName).PfConfig);
    }

    [Fact]
    public void Pf_config_is_hidden_when_payroll_statutory_is_off()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "No Stat Co";
        vm.CreateCompany();
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        Assert.False(page.ShowPfConfig);
    }

    // ---------------------------------------------------------------- (2) per-employee PF details + UAN guard

    [Fact]
    public void Pf_applicable_employee_requires_a_valid_12_digit_uan()
    {
        var vm = NewPayrollCompany("PF Emp Guard Co");
        var c = vm.Company!;
        new PayrollService(c).CreateEmployeeGroup("Staff");

        vm.ShowEmployeeMaster();
        var m = vm.EmployeeMaster!;
        Assert.True(m.ShowPfDetails); // Payroll Statutory is on

        // PF-applicable with a blank UAN is rejected with a friendly message; nothing is created.
        m.Name = "No UAN";
        m.SelectedGroup = m.GroupOptions.First();
        m.PfApplicable = true;
        m.Uan = string.Empty;
        Assert.False(m.Create());
        Assert.NotNull(m.Message);
        Assert.Contains("UAN", m.Message!);
        Assert.Null(c.FindEmployeeByName("No UAN"));

        // A malformed UAN (not 12 digits) is likewise rejected.
        m.Name = "Bad UAN";
        m.Uan = "12345";
        Assert.False(m.Create());
        Assert.Contains("UAN", m.Message!);
        Assert.Null(c.FindEmployeeByName("Bad UAN"));

        // A valid 12-digit UAN creates a PF-applicable member.
        m.Name = "Good UAN";
        m.Uan = "100000000002";
        m.PfContributeOnHigherWages = true;
        Assert.True(m.Create(), m.Message);
        var emp = c.FindEmployeeByName("Good UAN")!;
        Assert.True(emp.PfApplicable);
        Assert.True(emp.PfContributeOnHigherWages);
        Assert.Equal("100000000002", emp.Uan);
    }

    // ---------------------------------------------------------------- (3) PF ECR / Challan report — golden

    [Fact]
    public void Pf_ecr_report_renders_the_golden_challan_totals_for_the_15000_member()
    {
        var vm = NewPayrollCompany("PF ECR Co");
        var c = vm.Company!;
        BuildPfGolden(c);
        _storage.Save(c);

        vm.OpenPfEcrReport();
        Assert.Equal(Screen.PfEcrReport, vm.CurrentScreen);
        var page = vm.PfEcrReport!;

        // Pay the establishment's first wage month (April of the FY the golden structure is effective from).
        page.SelectedYear = page.FinancialYears.First(y => y.StartYear == c.FinancialYearStart.Year);
        page.SelectedMonth = page.Months.First(mo => mo.FirstDay == c.FinancialYearStart);

        Assert.False(page.IsEmpty);
        Assert.Single(page.Members);

        // Challan account-head totals (whole rupees): A/c1 = EE 1,800 + ER 550 = 2,350; A/c2 = 500 (floored);
        // A/c10 = EPS 1,250; A/c21 = EDLI 75; A/c22 = 0.
        Assert.Equal("2,350", page.Account1Text);
        Assert.Equal("500", page.Account2Text);
        Assert.Equal("1,250", page.Account10Text);
        Assert.Equal("75", page.Account21Text);
        Assert.Equal("0", page.Account22Text);

        // The member row carries the exact EPF/EPS/ER split on ₹15,000 PF wages.
        var row = page.Members.Single();
        Assert.Equal("100000000001", row.Uan);
        Assert.Equal("15,000", row.EpfWages);
        Assert.Equal("1,800", row.EmployeeShareEpf);
        Assert.Equal("1,250", row.EpsContribution);
        Assert.Equal("550", row.EmployerShareEpf);

        // Export writes the ECR 2.0 flat file through the injectable seam (no disk).
        string? writtenPath = null;
        byte[]? written = null;
        Assert.True(page.ExportEcr((p, b) => { writtenPath = p; written = b; }));
        Assert.NotNull(written);
        var text = Encoding.UTF8.GetString(written!);
        Assert.Contains("100000000001", text);   // the member UAN
        Assert.Contains("#~#", text);             // ECR 2.0 delimiter
        // The ECR 2.0 upload file is member-detail lines ONLY — the challan account-head totals are surfaced on the
        // report (Account1Text..Account22Text), never embedded as a line (EPFO auto-generates the challan on upload).
        Assert.DoesNotContain("CHALLAN", text);
        foreach (var l in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            Assert.Matches(@"^\d{12}#~#", l);     // every line is a member record keyed by a 12-digit UAN
        Assert.EndsWith(".txt", writtenPath);
    }

    [Fact]
    public void Pf_ecr_report_is_gated_on_payroll_statutory()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "PF Gated Co";
        vm.CreateCompany();
        vm.OpenPfEcrReport();          // no-op: Payroll Statutory is off
        Assert.NotEqual(Screen.PfEcrReport, vm.CurrentScreen);
        Assert.Null(vm.PfEcrReport);
    }
}
