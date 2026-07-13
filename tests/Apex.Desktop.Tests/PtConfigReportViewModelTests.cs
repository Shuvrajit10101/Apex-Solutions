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
/// End-to-end coverage for the Phase-8 slice-6 <b>Professional Tax</b> UI surfaced in the cascade (RQ-11; catalog §14;
/// Article 276(2)): the F11 Statutory-Configuration page enrols the establishment for PT (Enable PT + the state picker
/// [MH/KA/WB/None] + the enrolment number), shows the selected state's seeded slab bands and persists across a reload;
/// and the PT Deduction Register report renders the A14-verified golden per-employee PT (MH man ₹12,000 → ₹200, ₹300
/// in February; MH woman → ₹0; KA ₹30,000 → ₹200), the FY-to-date cumulative bounded by the ₹2,500 annual cap, and
/// exports the register CSV. Drives the real shell view models over a throwaway .db — no UI toolkit.
/// </summary>
public sealed class PtConfigReportViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    private const string MH = "27";   // Maharashtra
    private const string KA = "29";   // Karnataka
    private const string WB = "19";   // West Bengal

    public PtConfigReportViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexPtUiTests_" + Guid.NewGuid().ToString("N"));
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

    private readonly record struct PtHeads(Guid Basic, Guid Pt);

    private static PtHeads CreatePtHeads(PayHeadService ph, Company c)
    {
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c));
        var pt = ph.CreatePayHead("Professional Tax", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: CurrentLiabilities(c),
            ptComponent: PtStatutoryComponent.ProfessionalTax);
        return new PtHeads(basic.Id, pt.Id);
    }

    private static SalaryStructureLine[] Lines(PtHeads h, decimal basic) => new[]
    {
        new SalaryStructureLine(h.Basic, 0, new Money(basic)),
        new SalaryStructureLine(h.Pt, 1),
    };

    /// <summary>Adds a PT employee (Basic as gross PT-wages, a PT head) with the structure effective from 1-Apr of
    /// the company FY.</summary>
    private static Guid AddPtEmployee(Company c, PtHeads heads, Guid groupId, string name, string? number, string gender, decimal basic)
    {
        var pay = new PayrollService(c);
        var e = pay.CreateEmployee(name, groupId, employeeNumber: number);
        c.FindEmployee(e.Id)!.Gender = gender;
        new SalaryStructureService(c).DefineForEmployee(e.Id, c.FinancialYearStart, Lines(heads, basic));
        return e.Id;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    // ---------------------------------------------------------------- (1) F11 PT config: enable + persist + slab

    [Fact]
    public void Enable_pt_config_sets_the_state_and_registration_and_persists_across_reload()
    {
        const string companyName = "PT Enable Co";
        var vm = NewPayrollCompany(companyName);
        vm.ShowGstConfig();
        var page = vm.GstConfig!;

        Assert.True(page.ShowPtConfig); // shown because Payroll Statutory is on
        page.PtEnabled = true;
        page.SelectedPtState = page.PtStateOptions.First(o => o.Code == MH);
        page.PtRegistrationNumber = "27999999999";
        Assert.True(page.ApplyPt());

        Assert.NotNull(vm.Company!.PtConfig);
        Assert.Equal(MH, vm.Company.PtConfig!.StateCode);
        Assert.Equal("27999999999", vm.Company.PtConfig.RegistrationNumber);
        // The seeded state tables (MH men/women, KA, WB) are all present so the state can be switched without re-seed.
        Assert.Contains(vm.Company.PtConfig.SlabTables, s => s.StateCode == MH);
        Assert.Contains(vm.Company.PtConfig.SlabTables, s => s.StateCode == KA);
        Assert.Contains(vm.Company.PtConfig.SlabTables, s => s.StateCode == WB);

        var reloaded = Reload(companyName);
        Assert.NotNull(reloaded.PtConfig);
        Assert.Equal(MH, reloaded.PtConfig!.StateCode);
        Assert.Equal("27999999999", reloaded.PtConfig.RegistrationNumber);
    }

    [Fact]
    public void State_selection_drives_the_visible_slab_table()
    {
        var vm = NewPayrollCompany("PT Slab Drive Co");
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.PtEnabled = true;

        // Maharashtra — gender-scoped: 3 male bands + 2 female bands = 5 rows; the men top band is ₹200 (₹300 Feb).
        page.SelectedPtState = page.PtStateOptions.First(o => o.Code == MH);
        Assert.True(page.ApplyPt());
        Assert.True(page.HasPtSlabBands);
        Assert.Equal(5, page.PtSlabBands.Count);
        Assert.Contains(page.PtSlabBands, r => r.AppliesTo == "Men" && r.MonthlyText == "200" && r.FebText == "300");
        Assert.Contains(page.PtSlabBands, r => r.AppliesTo == "Women");

        // Switch to Karnataka — gender-agnostic: 2 bands (Nil + ₹200/₹300-Feb), no gendered rows.
        page.SelectedPtState = page.PtStateOptions.First(o => o.Code == KA);
        Assert.True(page.ApplyPt());
        Assert.Equal(KA, vm.Company!.PtConfig!.StateCode);
        Assert.Equal(2, page.PtSlabBands.Count);
        Assert.All(page.PtSlabBands, r => Assert.Equal("All employees", r.AppliesTo));

        // Switch to None — no PT levied: empty slab, the "no PT" note shows.
        page.SelectedPtState = page.PtStateOptions.First(o => o.Code == null);
        Assert.True(page.ApplyPt());
        Assert.Null(vm.Company.PtConfig!.StateCode);
        Assert.False(page.HasPtSlabBands);
        Assert.True(page.PtStateHasNoSlab);
    }

    [Fact]
    public void Disabling_pt_clears_the_config_and_persists()
    {
        const string companyName = "PT Off Co";
        var vm = NewPayrollCompany(companyName);
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.PtEnabled = true;
        page.SelectedPtState = page.PtStateOptions.First(o => o.Code == MH);
        Assert.True(page.ApplyPt());
        Assert.NotNull(vm.Company!.PtConfig);

        page.PtEnabled = false;
        Assert.True(page.ApplyPt());
        Assert.Null(vm.Company.PtConfig);
        Assert.Null(Reload(companyName).PtConfig);
    }

    [Fact]
    public void Pt_config_is_hidden_when_payroll_statutory_is_off()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "No Stat PT Co";
        vm.CreateCompany();
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        Assert.False(page.ShowPtConfig);
    }

    // ---------------------------------------------------------------- (2) PT register — golden Maharashtra

    [Fact]
    public void Pt_register_renders_the_golden_mh_man_200_and_woman_0()
    {
        var vm = NewPayrollCompany("PT MH Report Co");
        var c = vm.Company!;
        new PayrollService(c).EnableProfessionalTax(stateCode: MH);
        var ph = new PayHeadService(c);
        var heads = CreatePtHeads(ph, c);
        var groupId = new PayrollService(c).CreateEmployeeGroup("Staff").Id;
        AddPtEmployee(c, heads, groupId, "Arjun Rao", "E001", "Male", 12000m);
        AddPtEmployee(c, heads, groupId, "Meera Rao", "E002", "Female", 12000m);
        _storage.Save(c);

        vm.OpenProfessionalTaxRegister();
        Assert.Equal(Screen.ProfessionalTaxRegister, vm.CurrentScreen);
        var page = vm.ProfessionalTaxRegister!;
        page.SelectedYear = page.FinancialYears.First(y => y.StartYear == c.FinancialYearStart.Year);

        // April — man ₹200, woman ₹0 (exempt); ordered by name so Arjun (man) is first.
        page.SelectedMonth = page.Months.First(mo => mo.FirstDay == c.FinancialYearStart);
        Assert.False(page.IsEmpty);
        Assert.Equal(2, page.Rows.Count);
        var man = page.Rows[0];
        var woman = page.Rows[1];
        Assert.Equal("Arjun Rao", man.EmployeeName);
        Assert.Equal("12,000", man.PtWages);
        Assert.Equal("200", man.MonthlyPt);
        Assert.Equal("200", man.FyCumulative);     // April is the FY's first month
        Assert.Equal("Meera Rao", woman.EmployeeName);
        Assert.Equal("0", woman.MonthlyPt);
        Assert.Equal("200", page.TotalPtText);

        // February over-charge — the man is ₹300 (the woman stays ₹0); cumulative Apr..Feb = 200×10 + 300 = 2,300.
        page.SelectedMonth = page.Months.First(mo => mo.FirstDay.Month == 2);
        Assert.Equal("300", page.Rows[0].MonthlyPt);
        Assert.Equal("2,300", page.Rows[0].FyCumulative);
        Assert.Equal("0", page.Rows[1].MonthlyPt);
        Assert.Equal("300", page.TotalPtText);

        // March — the ₹2,500 annual cap is reached exactly (200×11 + 300 = 2,500); the register reflects it.
        page.SelectedMonth = page.Months.First(mo => mo.FirstDay.Month == 3);
        Assert.Equal("200", page.Rows[0].MonthlyPt);
        Assert.Equal("2,500", page.Rows[0].FyCumulative);
    }

    [Fact]
    public void Pt_register_renders_the_golden_ka_30000_is_200()
    {
        var vm = NewPayrollCompany("PT KA Report Co");
        var c = vm.Company!;
        new PayrollService(c).EnableProfessionalTax(stateCode: KA);
        var ph = new PayHeadService(c);
        var heads = CreatePtHeads(ph, c);
        var groupId = new PayrollService(c).CreateEmployeeGroup("Staff").Id;
        AddPtEmployee(c, heads, groupId, "Kiran Shet", "K01", "Male", 30000m);
        _storage.Save(c);

        vm.OpenProfessionalTaxRegister();
        var page = vm.ProfessionalTaxRegister!;
        page.SelectedYear = page.FinancialYears.First(y => y.StartYear == c.FinancialYearStart.Year);
        page.SelectedMonth = page.Months.First(mo => mo.FirstDay == c.FinancialYearStart);

        var row = Assert.Single(page.Rows);
        Assert.Equal("30,000", row.PtWages);
        Assert.Equal("200", row.MonthlyPt);
        Assert.Equal("200", page.TotalPtText);

        // Export writes the register CSV through the injectable seam (no disk); it carries the member + figure.
        string? writtenPath = null;
        byte[]? written = null;
        Assert.True(page.ExportRegister((p, b) => { writtenPath = p; written = b; }));
        Assert.NotNull(written);
        var text = Encoding.UTF8.GetString(written!);
        Assert.Contains("Kiran Shet", text);
        Assert.Contains("200", text);
        Assert.DoesNotContain("Tally", text, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".csv", writtenPath);
    }

    [Fact]
    public void Pt_register_is_gated_on_payroll_statutory()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "PT Gated Co";
        vm.CreateCompany();
        vm.OpenProfessionalTaxRegister();          // no-op: Payroll Statutory is off
        Assert.NotEqual(Screen.ProfessionalTaxRegister, vm.CurrentScreen);
        Assert.Null(vm.ProfessionalTaxRegister);
    }
}
