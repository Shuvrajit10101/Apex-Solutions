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
/// 🔴 <b>CENSUS ROW 7.18 — THE NPS PAY HEAD, REACHED THE WAY AN OPERATOR REACHES IT.</b>
///
/// <para>The census bar this project works to is that <b>a capability no user can reach is not complete</b>. An NPS
/// statutory pay type that exists only as an enum value, settable only from a test, would be the fourth dead
/// feature filed here. So nothing below constructs a <c>PayHead</c>: every test drives the real
/// <see cref="PayHeadMasterViewModel"/> behind the shell's <b>Masters → Create → Pay Head</b> route, picks the NPS
/// option out of the same picker the operator picks from, saves, and reloads from a real <c>.db</c>.</para>
///
/// <para><b>The picker is the guard's twin, and that is asserted both ways.</b> An option offered but rejected on
/// save is a dead end the operator walks into; an option withheld where the service would accept it is a capability
/// that exists and cannot be used. Both directions are pinned, over every pay-head type.</para>
///
/// <para>The rendering tests walk the <b>realised visual tree</b> — asserting a view-model collection would pass on
/// a build whose combo box or report grid draws nothing, which is exactly the pattern that let three slices ship
/// green holding 34, 42 and 17 defects. Headless-safe: visual-tree and layout inspection only, no rendered frame.</para>
/// </summary>
public sealed class NpsPayHeadReachabilityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public NpsPayHeadReachabilityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexNpsUi_" + Guid.NewGuid().ToString("N"));
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

    // ------------------------------------------------------------------------------------ the master screen

    /// <summary>
    /// 🔴 <b>BOTH NPS HEADS ARE CREATED THROUGH THE MASTER SCREEN AND SURVIVE A RELOAD.</b> The employee deduction
    /// and the employer contribution carry the SAME vendor statutory pay type and differ only by pay-head type, so
    /// this is also the test that proves the two-sided tag survives the round trip through SQLite rather than
    /// collapsing to one side. The tag rides the existing <c>income_tax_component</c> column and took no schema
    /// version — if that column were not actually written, the reload below would come back
    /// <see cref="IncomeTaxComponent.NotApplicable"/> and the whole row would be silently gone.
    /// </summary>
    [Fact]
    public void Both_nps_pay_heads_are_created_through_the_master_screen_and_reload_with_their_statutory_pay_type()
    {
        const string companyName = "NPS Master Co";
        var vm = NewPayrollCompany(companyName);

        CreateNps(vm, "Employee NPS Deduction", PayHeadType.EmployeesStatutoryDeductions,
            IncomeTaxComponent.NationalPensionSchemeTierI);
        CreateNps(vm, "Employer NPS Contribution", PayHeadType.EmployersStatutoryContributions,
            IncomeTaxComponent.NationalPensionSchemeTierI);
        CreateNps(vm, "Employee NPS Tier II", PayHeadType.EmployeesStatutoryDeductions,
            IncomeTaxComponent.NationalPensionSchemeTierII);

        var reloaded = Reload(companyName);

        var employee = reloaded.FindPayHeadByName("Employee NPS Deduction")!;
        var employer = reloaded.FindPayHeadByName("Employer NPS Contribution")!;
        var tierTwo = reloaded.FindPayHeadByName("Employee NPS Tier II")!;

        Assert.Equal(IncomeTaxComponent.NationalPensionSchemeTierI, employee.IncomeTaxComponent);
        Assert.Equal(IncomeTaxComponent.NationalPensionSchemeTierI, employer.IncomeTaxComponent);
        Assert.Equal(IncomeTaxComponent.NationalPensionSchemeTierII, tierTwo.IncomeTaxComponent);

        // The two sides stayed on their own sides across the reload.
        Assert.True(employee.AffectsNetSalary);
        Assert.False(employer.AffectsNetSalary);
    }

    /// <summary>
    /// 🔴 <b>THE PICKER OFFERS EXACTLY WHAT THE SERVICE ACCEPTS — CHECKED IN BOTH DIRECTIONS, ON EVERY TYPE.</b>
    /// Tier-I on both statutory types, Tier-II on the employee one, neither on any other type. An offered-then-
    /// rejected option and a withheld-but-valid option are both silent defects that a one-directional test misses.
    /// </summary>
    [Theory]
    [InlineData(PayHeadType.EmployeesStatutoryDeductions, true, true)]
    [InlineData(PayHeadType.EmployersStatutoryContributions, true, false)]
    [InlineData(PayHeadType.Earnings, false, false)]
    [InlineData(PayHeadType.Deductions, false, false)]
    [InlineData(PayHeadType.EmployersOtherCharges, false, false)]
    [InlineData(PayHeadType.Gratuity, false, false)]
    [InlineData(PayHeadType.LoansAndAdvances, false, false)]
    [InlineData(PayHeadType.Reimbursements, false, false)]
    [InlineData(PayHeadType.Bonus, false, false)]
    [InlineData(PayHeadType.NotApplicable, false, false)]
    public void The_statutory_pay_type_picker_offers_the_nps_options_on_exactly_the_sides_the_service_accepts(
        PayHeadType type, bool tierIOffered, bool tierIIOffered)
    {
        var vm = NewPayrollCompany("NPS Picker Co " + type);
        vm.ShowPayHeadMaster();
        var m = vm.PayHeadMaster!;
        m.SelectedType = m.Types.Single(t => t.Value == type);

        bool Offered(IncomeTaxComponent c) => m.IncomeTaxComponents.Any(o => o.Value == c);

        Assert.Equal(tierIOffered, Offered(IncomeTaxComponent.NationalPensionSchemeTierI));
        Assert.Equal(tierIIOffered, Offered(IncomeTaxComponent.NationalPensionSchemeTierII));

        // The picker and the domain rule agree — the picker is not a second, drifting copy of the side rule.
        Assert.Equal(NationalPensionScheme.IsPayHeadTypeAllowed(IncomeTaxComponent.NationalPensionSchemeTierI, type),
            Offered(IncomeTaxComponent.NationalPensionSchemeTierI));
        Assert.Equal(NationalPensionScheme.IsPayHeadTypeAllowed(IncomeTaxComponent.NationalPensionSchemeTierII, type),
            Offered(IncomeTaxComponent.NationalPensionSchemeTierII));
    }

    /// <summary>The picker shows the vendor's own captions, not a name invented here.</summary>
    [Fact]
    public void The_picker_labels_the_nps_options_with_the_vendor_captions()
    {
        var vm = NewPayrollCompany("NPS Caption Co");
        vm.ShowPayHeadMaster();
        var m = vm.PayHeadMaster!;
        m.SelectedType = m.Types.Single(t => t.Value == PayHeadType.EmployeesStatutoryDeductions);

        Assert.Equal(NationalPensionScheme.TierIName,
            m.IncomeTaxComponents.Single(o => o.Value == IncomeTaxComponent.NationalPensionSchemeTierI).Display);
        Assert.Equal(NationalPensionScheme.TierIIName,
            m.IncomeTaxComponents.Single(o => o.Value == IncomeTaxComponent.NationalPensionSchemeTierII).Display);

        // The pre-existing options are untouched — adding NPS must not have reshuffled the picker.
        Assert.Equal("◦ None", m.IncomeTaxComponents.First().Display);
        Assert.Contains(m.IncomeTaxComponents, o => o.Value == IncomeTaxComponent.TaxDeductedAtSource);
    }

    /// <summary>
    /// Switching the pay-head type away from a side that has no NPS option drops a now-invalid NPS selection rather
    /// than carrying it into a save the service would reject. This is the concrete dead end the picker/guard pairing
    /// exists to prevent: pick employee Tier-II, change your mind about the type, save, get an error you cannot see
    /// the cause of.
    /// </summary>
    [Fact]
    public void Changing_the_pay_head_type_drops_an_nps_selection_the_new_side_cannot_carry()
    {
        var vm = NewPayrollCompany("NPS Switch Co");
        vm.ShowPayHeadMaster();
        var m = vm.PayHeadMaster!;

        m.SelectedType = m.Types.Single(t => t.Value == PayHeadType.EmployeesStatutoryDeductions);
        m.SelectedIncomeTaxComponent =
            m.IncomeTaxComponents.Single(o => o.Value == IncomeTaxComponent.NationalPensionSchemeTierII);

        m.SelectedType = m.Types.Single(t => t.Value == PayHeadType.EmployersStatutoryContributions);

        Assert.NotEqual(IncomeTaxComponent.NationalPensionSchemeTierII, m.SelectedIncomeTaxComponent!.Value);
        Assert.DoesNotContain(m.IncomeTaxComponents, o => o.Value == IncomeTaxComponent.NationalPensionSchemeTierII);

        // Tier-I, which IS valid on both sides, survives the same switch — the drop is about validity, not about
        // clearing the field whenever the type changes.
        m.SelectedIncomeTaxComponent =
            m.IncomeTaxComponents.Single(o => o.Value == IncomeTaxComponent.NationalPensionSchemeTierI);
        m.SelectedType = m.Types.Single(t => t.Value == PayHeadType.EmployeesStatutoryDeductions);
        Assert.Equal(IncomeTaxComponent.NationalPensionSchemeTierI, m.SelectedIncomeTaxComponent!.Value);
    }

    // ------------------------------------------------------------------------------------ drawn on screen

    /// <summary>
    /// 🔴 <b>THE PAYROLL STATUTORY SUMMARY DRAWS AN NPS ROW.</b> Row 7.25 shipped declaring, on the report's own
    /// face, that NPS was named by the vendor and not maintained here. That divergence is closed by this row, and
    /// "closed" has to mean the caption and both pay heads are on screen — not that a sentence was deleted.
    /// </summary>
    [AvaloniaFact]
    public void The_payroll_statutory_summary_draws_the_nps_row_and_both_nps_pay_heads()
    {
        var (window, vm) = OpenWindow();
        try
        {
            vm.OpenPayrollStatutoryForm(ReportKind.PayrollStatutorySummary);
            Pump(window);
            var r = vm.Reports!;

            Assert.False(r.IsPayrollEmpty, r.PayrollEmptyNote);
            Assert.True(IsTextVisible(window, NationalPensionScheme.SummaryCaption),
                "the National Pension Scheme roll-up row is not on screen.");

            Assert.True(r.HasPayrollSection2);
            Assert.True(IsTextVisible(window, "Employee NPS Deduction"),
                "the employee NPS pay head is not on screen in the Statutory Pay Head Details band.");
            Assert.True(IsTextVisible(window, "Employer NPS Contribution"),
                "the employer NPS pay head is not on screen in the Statutory Pay Head Details band.");

            // No footnote may still claim NPS is unsupported — the report would be lying about its own contents.
            Assert.DoesNotContain(r.PayrollFootnotes, n => n.Contains("NPS", StringComparison.Ordinal));
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>NO HEADING OR VALUE ON THE NPS ROW IS TRUNCATED.</b> Copied from the payroll-reports track's
    /// legibility invariant, which caught two real truncation defects on its first run: a <see cref="TextBlock"/>
    /// whose desired width exceeds its realised width is showing the operator a clipped figure, and a clipped money
    /// column is indistinguishable from a smaller number.
    /// </summary>
    [AvaloniaFact]
    public void No_text_on_the_drawn_nps_summary_row_is_truncated()
    {
        var (window, vm) = OpenWindow();
        try
        {
            vm.OpenPayrollStatutoryForm(ReportKind.PayrollStatutorySummary);
            Pump(window);

            foreach (var t in Descendants(window).OfType<TextBlock>())
            {
                if (!t.IsEffectivelyVisible || t.Bounds.Width <= 0 || t.Bounds.Height <= 0) continue;
                if (t.Text is not { Length: > 0 } text) continue;
                if (!text.Contains("NPS", StringComparison.Ordinal)
                    && !text.Contains(NationalPensionScheme.SummaryCaption, StringComparison.Ordinal)) continue;
                if (t.TextTrimming != Avalonia.Media.TextTrimming.None || t.TextWrapping != Avalonia.Media.TextWrapping.NoWrap)
                    continue;   // a cell that declares trimming/wrapping is not making a fits-exactly claim

                Assert.True(t.DesiredSize.Width <= t.Bounds.Width + 0.5,
                    $"\"{text}\" wants {t.DesiredSize.Width:F1}px but was given {t.Bounds.Width:F1}px — it is clipped.");
            }
        }
        finally { window.Close(); }
    }

    // ------------------------------------------------------------------------------------ helpers

    private MainWindowViewModel NewPayrollCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name + " " + Guid.NewGuid().ToString("N")[..6];
        vm.CreateCompany();
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.PayrollEnabled = true;
        page.PayrollStatutoryEnabled = true;
        return vm;
    }

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name.StartsWith(companyName, StringComparison.Ordinal));
        return _storage.Load(entry);
    }

    /// <summary>Creates an NPS pay head through the real master screen, picking the statutory pay type out of the
    /// picker exactly as the operator does.</summary>
    private static void CreateNps(MainWindowViewModel vm, string name, PayHeadType type, IncomeTaxComponent nps)
    {
        vm.ShowPayHeadMaster();
        Assert.Equal(Screen.PayHeadMaster, vm.CurrentScreen);
        var m = vm.PayHeadMaster!;
        m.Name = name;
        m.SelectedType = m.Types.Single(t => t.Value == type);
        m.SelectedCalcType = m.CalcTypes.Single(c => c.Value == PayHeadCalculationType.AsUserDefinedValue);
        m.SelectedIncomeTaxComponent = m.IncomeTaxComponents.Single(o => o.Value == nps);
        Assert.True(m.Create(), m.Message);
    }

    private (MainWindow Window, MainWindowViewModel Vm) OpenWindow()
    {
        var vm = BuildPostedNpsCompany();
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

    /// <summary>One posted wage month for one employee on Basic ₹30,000, with both NPS heads at a typed 10% of
    /// Basic — the same fixture shape the ledger-side row-7.18 tests use, so the two cannot disagree.</summary>
    private MainWindowViewModel BuildPostedNpsCompany()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "NPS Report Co " + Guid.NewGuid().ToString("N")[..8];
        vm.CreateCompany();
        var c = vm.Company!;
        var from = new DateOnly(c.FinancialYearStart.Year, c.FinancialYearStart.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);

        var pay = new PayrollService(c);
        pay.EnablePayroll();

        var ph = new PayHeadService(c);
        var indirect = c.FindGroupByName("Indirect Expenses")!.Id;
        var liab = c.FindGroupByName("Current Liabilities")!.Id;

        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: indirect, incomeTaxComponent: IncomeTaxComponent.BasicSalary);

        PayHeadComputation TenPercentOfBasic() => new(
            new[] { new PayHeadComputationComponent(basic.Id) },
            new[] { PayHeadComputationSlab.Percentage(1000) });

        var employeeNps = ph.CreatePayHead("Employee NPS Deduction", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsComputedValue, underGroupId: liab,
            incomeTaxComponent: IncomeTaxComponent.NationalPensionSchemeTierI, computation: TenPercentOfBasic());
        var employerNps = ph.CreatePayHead("Employer NPS Contribution", PayHeadType.EmployersStatutoryContributions,
            PayHeadCalculationType.AsComputedValue, underGroupId: indirect,
            incomeTaxComponent: IncomeTaxComponent.NationalPensionSchemeTierI, computation: TenPercentOfBasic());

        var grp = pay.CreateEmployeeGroup("Staff").Id;
        var emp = pay.CreateEmployee("Rajkumar Sharma", grp, employeeNumber: "E-001", pan: "ABCPD5678L");
        emp.DateOfJoining = from;

        new SalaryStructureService(c).DefineForEmployee(emp.Id, from, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(30_000m)),
            new SalaryStructureLine(employeeNps.Id, 1),
            new SalaryStructureLine(employerNps.Id, 2),
        });

        new PayrollVoucherService(c).Post(from, to, new[] { emp.Id });
        _storage.Save(c);
        return vm;
    }
}
