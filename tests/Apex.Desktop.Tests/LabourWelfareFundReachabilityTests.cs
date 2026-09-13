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
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS ROW 7.19 — THE LABOUR WELFARE FUND DEDUCTION, REACHED THE WAY AN OPERATOR REACHES IT.</b>
///
/// <para>The bar this project works to is that <b>a capability no user can reach is not complete</b>. A dated
/// computation slab that existed only in the domain — settable from a test and from nowhere on screen — would be
/// the next dead feature filed here. So nothing below constructs a <c>PayHeadComputationSlab</c>: every test
/// drives the real <see cref="PayHeadMasterViewModel"/> behind the shell's <b>Masters → Create → Pay Head</b>
/// route, types the effective dates into the same boxes the operator types into, saves, and reloads from a real
/// <c>.db</c>.</para>
///
/// <para><b>R7.</b> The route matches the vendor's own instruction for this pay head:
/// <i>"Select <b>Deductions From Employees</b> in the <b>Pay head type</b> field. In <b>Computation
/// Information</b> section, define the <b>Effective From</b> and <b>Value</b> as applicable."</i>
/// (<c>help.tallysolutions.com/tally-prime/payroll/payroll-faq/</c>, read 2026-09-14.)</para>
///
/// <para>The rendering tests walk the <b>realised visual tree</b> — asserting a view-model property would pass on
/// a build whose date boxes draw nothing, which is exactly the pattern that let three slices ship green holding
/// 34, 42 and 17 defects. Headless-safe: visual-tree and layout inspection only, no rendered frame.</para>
///
/// <para>🔴 <b>No rate is asserted.</b> ₹100 below is a fixture an operator typed, NOT any State's Labour Welfare
/// Fund contribution — this product seeds none, because LWF amounts, ceilings and periodicity are per-State law.</para>
/// </summary>
public sealed class LabourWelfareFundReachabilityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    private const decimal OperatorTypedContribution = 100m;

    public LabourWelfareFundReachabilityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexLwfUi_" + Guid.NewGuid().ToString("N"));
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
    /// 🔴 <b>THE ROW, END TO END, THROUGH THE SCREEN.</b> A Labour Welfare Fund head is created on the vendor's
    /// route — pay head type <b>Deductions from Employees</b>, an As-Computed-Value slab with the effective dates
    /// typed in — and after a reload from a real <c>.db</c> it still deducts in December <b>alone</b>.
    ///
    /// <para>The assertion is the <b>computed payslip over a whole financial year</b>, not the stored date. A
    /// screen that captured the dates but failed to pass them to the domain would satisfy any weaker check and
    /// still take an annual levy twelve times.</para>
    /// </summary>
    [Fact]
    public void A_december_only_lwf_head_is_created_through_the_master_screen_and_deducts_once_a_year_after_reload()
    {
        const string companyName = "LWF Master Co";
        var vm = NewPayrollCompany(companyName);

        var basicId = CreateBasic(vm);
        CreateLwfHead(vm, basicId, effectiveFrom: "01-Dec-2026", effectiveTo: "31-Dec-2026");

        var reloaded = Reload(companyName);
        var head = reloaded.PayHeads.Single(p => p.Name == "Labour Welfare Fund");

        // The window actually reached storage…
        var slab = Assert.Single(head.Computation!.Slabs);
        Assert.Equal(new DateOnly(2026, 12, 1), slab.EffectiveFrom);
        Assert.Equal(new DateOnly(2026, 12, 31), slab.EffectiveTo);

        // …and the book computed from it deducts once, not twelve times.
        var employee = AttachEmployee(reloaded, basicId, head.Id);
        var engine = new PayrollComputationService(reloaded);
        var fyStart = new DateOnly(2026, 4, 1);

        decimal annual = 0m;
        for (var m = 0; m < 12; m++)
        {
            var first = fyStart.AddMonths(m);
            var last = new DateOnly(first.Year, first.Month, DateTime.DaysInMonth(first.Year, first.Month));
            var amount = engine.Compute(employee, first, last)
                .Lines.Single(l => l.PayHead.Id == head.Id).Amount.Amount;
            annual += amount;
            var expected = m == 8 ? OperatorTypedContribution : 0m;   // offset 8 = December 2026
            Assert.True(amount == expected,
                $"{first:MMM yyyy} expected {expected} but deducted {amount}.");
        }

        Assert.Equal(OperatorTypedContribution, annual);
    }

    /// <summary>
    /// <b>Leaving both boxes blank keeps the pre-v63 behaviour</b> — the slab is perpetual and deducts every
    /// period. This is the control: it proves the December result above comes from what was typed and not from
    /// some unrelated default the screen applies.
    /// </summary>
    [Fact]
    public void Leaving_both_date_boxes_blank_creates_a_perpetual_slab()
    {
        const string companyName = "LWF Blank Co";
        var vm = NewPayrollCompany(companyName);

        var basicId = CreateBasic(vm);
        CreateLwfHead(vm, basicId, effectiveFrom: null, effectiveTo: null);

        var head = Reload(companyName).PayHeads.Single(p => p.Name == "Labour Welfare Fund");
        var slab = Assert.Single(head.Computation!.Slabs);

        Assert.Null(slab.EffectiveFrom);
        Assert.Null(slab.EffectiveTo);
        Assert.False(slab.IsDated);
        Assert.True(slab.IsInForceOn(new DateOnly(2026, 7, 31)));
    }

    /// <summary>
    /// <b>An inverted window is refused at the screen, with a message that says which field is wrong.</b> A slab
    /// whose end precedes its start can never be in force, so it produces a deduction that simply never happens —
    /// invisible on the payslip, and discovered only when the statutory remittance fails to reconcile. The screen
    /// must refuse it rather than let the domain throw into an unhandled path.
    /// </summary>
    [Fact]
    public void An_inverted_effective_window_is_refused_at_the_screen()
    {
        var vm = NewPayrollCompany("LWF Invert Co");
        var basicId = CreateBasic(vm);

        vm.ShowPayHeadMaster();
        var m = vm.PayHeadMaster!;
        m.SelectedSlabType = m.SlabTypes.Single(s => s.Value == PayHeadComputationSlabType.FlatValue);
        m.SlabRateOrValueText = OperatorTypedContribution.ToString("0.##");
        m.SlabEffectiveFromText = "31-Dec-2026";
        m.SlabEffectiveToText = "01-Dec-2026";

        m.AddSlab();

        Assert.Empty(m.Slabs);
        Assert.NotNull(m.Message);
        Assert.Contains("effective to", m.Message!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <b>An unparseable date is refused rather than silently treated as blank.</b> Silently dropping a typo
    /// would turn a once-a-year levy into a monthly one — the single worst outcome this row exists to prevent —
    /// and the operator would have typed a date and been given a perpetual deduction without being told.
    /// </summary>
    [Fact]
    public void An_unparseable_effective_date_is_refused_rather_than_treated_as_blank()
    {
        var vm = NewPayrollCompany("LWF Bad Date Co");
        CreateBasic(vm);

        vm.ShowPayHeadMaster();
        var m = vm.PayHeadMaster!;
        m.SelectedSlabType = m.SlabTypes.Single(s => s.Value == PayHeadComputationSlabType.FlatValue);
        m.SlabRateOrValueText = OperatorTypedContribution.ToString("0.##");
        m.SlabEffectiveFromText = "not a date";

        m.AddSlab();

        Assert.Empty(m.Slabs);
        Assert.NotNull(m.Message);
        Assert.Contains("effective from", m.Message!, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------------------------ the realised tree

    /// <summary>
    /// 🔴 <b>THE TWO DATE BOXES ARE ACTUALLY DRAWN, AND ARE KEYBOARD-REACHABLE.</b> Binding a view-model property
    /// that no control renders is precisely how a feature becomes unreachable while every unit test stays green.
    /// This walks the realised visual tree of the real Pay Head master screen and requires both boxes to be on
    /// screen with non-zero bounds and focusable — a control with <c>Focusable = false</c> cannot be Tab-reached,
    /// and the keyboard-first contract this product ships under makes that a defect, not a nicety.
    /// </summary>
    [AvaloniaFact]
    public void The_pay_head_master_draws_both_effective_date_boxes_and_they_are_focusable()
    {
        var (window, vm) = OpenWindowOnPayHeadMaster();
        try
        {
            var boxes = Descendants(window).OfType<TextBox>()
                .Where(t => t.IsEffectivelyVisible && t.Bounds.Width > 0 && t.Bounds.Height > 0)
                .ToList();

            var from = boxes.SingleOrDefault(t => t.Watermark == "from");
            var to = boxes.SingleOrDefault(t => t.Watermark == "to");

            Assert.True(from is not null,
                "the slab 'effective from' box is not on screen — census row 7.19 would be unreachable.");
            Assert.True(to is not null,
                "the slab 'effective to' box is not on screen — census row 7.19 would be unreachable.");

            Assert.True(from!.Focusable, "the 'effective from' box cannot be reached from the keyboard.");
            Assert.True(to!.Focusable, "the 'effective to' box cannot be reached from the keyboard.");

            // Typing into the drawn control must reach the view model — the binding, not just the control.
            from.Text = "01-Dec-2026";
            Pump(window);
            Assert.Equal("01-Dec-2026", vm.PayHeadMaster!.SlabEffectiveFromText);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// <b>The hint that names the dangerous default is on screen.</b> Blank dates mean "deduct in every period",
    /// which is the correct default for an ordinary deduction and catastrophically wrong for an annual levy.
    /// A blank pair of boxes communicates nothing, so the screen says it in words; if that sentence is ever
    /// deleted, this test fails rather than the meaning quietly disappearing.
    /// </summary>
    [AvaloniaFact]
    public void The_screen_states_that_blank_dates_mean_every_period()
    {
        var (window, _) = OpenWindowOnPayHeadMaster();
        try
        {
            Assert.True(IsTextVisible(window, "Leave both blank to deduct in every period"),
                "the screen no longer tells the operator what blank effective dates mean.");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>NOTHING ON THE SLAB EDITOR IS TRUNCATED.</b> The effective-window row was added to a panel that
    /// already carried a six-column picker row; this project has filed repeated fixed-track truncation defects,
    /// and a clipped date is indistinguishable from a different date. A <see cref="TextBlock"/> whose desired
    /// width exceeds its realised width is showing the operator clipped text.
    /// </summary>
    [AvaloniaFact]
    public void No_text_on_the_slab_editor_is_truncated()
    {
        var (window, _) = OpenWindowOnPayHeadMaster();
        try
        {
            foreach (var t in Descendants(window).OfType<TextBlock>())
            {
                if (!t.IsEffectivelyVisible || t.Bounds.Width <= 0 || t.Bounds.Height <= 0) continue;
                if (t.Text is not { Length: > 0 } text) continue;
                if (!text.Contains("In force", StringComparison.Ordinal)
                    && !text.Contains("Slab", StringComparison.Ordinal)) continue;
                // A cell that declares trimming or wrapping is not making a fits-exactly claim.
                if (t.TextTrimming != Avalonia.Media.TextTrimming.None
                    || t.TextWrapping != Avalonia.Media.TextWrapping.NoWrap) continue;

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

    /// <summary>Creates the Basic earning the LWF head is computed on, through the real master screen.</summary>
    private static Guid CreateBasic(MainWindowViewModel vm)
    {
        vm.ShowPayHeadMaster();
        var m = vm.PayHeadMaster!;
        m.Name = "Basic";
        m.SelectedType = m.Types.Single(t => t.Value == PayHeadType.Earnings);
        m.SelectedCalcType = m.CalcTypes.Single(c => c.Value == PayHeadCalculationType.FlatRate);
        Assert.True(m.Create(), m.Message);
        return vm.Company!.PayHeads.Single(p => p.Name == "Basic").Id;
    }

    /// <summary>
    /// Creates the Labour Welfare Fund head on the vendor's route: pay head type <b>Deductions from
    /// Employees</b>, As-Computed-Value on Basic, with the effective dates typed into the same boxes the operator
    /// types into.
    /// </summary>
    private static void CreateLwfHead(
        MainWindowViewModel vm, Guid basicId, string? effectiveFrom, string? effectiveTo)
    {
        vm.ShowPayHeadMaster();
        Assert.Equal(Screen.PayHeadMaster, vm.CurrentScreen);
        var m = vm.PayHeadMaster!;

        m.Name = "Labour Welfare Fund";
        m.SelectedType = m.Types.Single(t => t.Value == PayHeadType.Deductions);
        m.SelectedCalcType = m.CalcTypes.Single(c => c.Value == PayHeadCalculationType.AsComputedValue);

        m.SelectedBasisPayHead = m.BasisPayHeadOptions.Single(p => p.PayHead.Id == basicId);
        m.AddBasisComponent();

        m.SelectedSlabType = m.SlabTypes.Single(s => s.Value == PayHeadComputationSlabType.FlatValue);
        m.SlabRateOrValueText = OperatorTypedContribution.ToString("0.##");
        m.SlabEffectiveFromText = effectiveFrom ?? string.Empty;
        m.SlabEffectiveToText = effectiveTo ?? string.Empty;
        m.AddSlab();
        Assert.True(m.Slabs.Count == 1, m.Message ?? "the slab was not added");

        Assert.True(m.Create(), m.Message);
    }

    /// <summary>Puts an employee on a structure carrying Basic + the LWF head, so a payslip can be computed.</summary>
    private static Guid AttachEmployee(Company company, Guid basicId, Guid lwfId)
    {
        var payroll = new PayrollService(company);
        var grp = payroll.CreateEmployeeGroup("Staff").Id;
        var emp = payroll.CreateEmployee("Rajkumar Sharma", grp, employeeNumber: "E-001", pan: "ABCPD5678L");
        var fyStart = new DateOnly(2026, 4, 1);
        emp.DateOfJoining = fyStart;

        new SalaryStructureService(company).DefineForEmployee(emp.Id, fyStart, new[]
        {
            new SalaryStructureLine(basicId, 0, new Money(30_000m)),
            new SalaryStructureLine(lwfId, 1),
        });
        return emp.Id;
    }

    private (MainWindow Window, MainWindowViewModel Vm) OpenWindowOnPayHeadMaster()
    {
        var vm = NewPayrollCompany("LWF Draw Co");
        CreateBasic(vm);

        var window = new MainWindow { DataContext = vm, Width = 1600, Height = 900 };
        window.Show();
        vm.ShowPayHeadMaster();
        var m = vm.PayHeadMaster!;
        // The slab editor only renders for a computed head — the operator selects this before typing a slab.
        m.SelectedCalcType = m.CalcTypes.Single(c => c.Value == PayHeadCalculationType.AsComputedValue);
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
}
