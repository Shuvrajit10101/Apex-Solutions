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
/// 🔴 <b>THE STATUTORY FORM NOBODY COULD READ.</b>
///
/// <para><b>The defect this file exists for.</b> The eight W7-D2 payroll statutory forms (PF 3A/5/6A/10/12A —
/// census 7.20; ESI 3/5/6 — census 7.21) projected correctly into three view-model collections that <b>no
/// DataTemplate rendered</b>: <c>PayrollFootnotes</c>, <c>PayrollColumns2</c> and <c>PayrollRows2</c>. The
/// consequences were not cosmetic:</para>
/// <list type="bullet">
///   <item><b>PF Form 6A page 2 was computed and thrown away.</b> Page 2 is the twelve monthly challan
///   remittances — it is where the annual statement reconciles to the challans, and it is the only reason to
///   consult 6A rather than 3A. It was built, held in <c>PayrollRows2</c>, and never drawn, printed or
///   exported.</item>
///   <item><b>Every "not maintained" footnote was invisible.</b> These forms deliberately print several columns
///   ruled and BLANK — Father's/Husband's Name, Reason for Leaving, the IP's dispensary — because this book does
///   not maintain their source. The footnote is the <i>only</i> thing that distinguishes "we do not hold this,
///   complete it by hand" from "this software has a bug", and on Form 12A it is the only thing that says the
///   remitted column is a challan fact rather than an amount we are claiming was paid. Silently blank columns on
///   a statutory register are worse than no register.</item>
/// </list>
///
/// <para><b>Why these tests walk the realised visual tree instead of asserting the collections.</b> Asserting
/// <c>PayrollFootnotes.Count &gt; 0</c> is exactly the test that passed on the broken build — the collection was
/// never the problem, and it was populated the whole time. The only claim worth making is that the text is on
/// screen, so each test below opens the report through the real shell, pumps a real layout, and looks for a
/// realised, effectively-visible <see cref="TextBlock"/> carrying the string.</para>
///
/// <para>Headless-safe: visual-tree and layout inspection only. No Skia, no rendered frame.</para>
/// </summary>
public sealed class PayrollStatutoryFormReachabilityTests : IDisposable
{
    private const string Uan = "100123456789";
    private const string Ip = "1234567890";

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public PayrollStatutoryFormReachabilityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexStatFormsUi_" + Guid.NewGuid().ToString("N"));
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

    // ------------------------------------------------------------------------------------------ the routes

    /// <summary>The eight rows this wave adds, and the report each must open. A row whose label does not reach its
    /// <see cref="ReportKind"/> is a menu entry that does nothing — which is how six census cells have already been
    /// wrong.</summary>
    public static IEnumerable<object[]> Rows() => new[]
    {
        new object[] { "PF Form 3A", ReportKind.PfForm3A },
        new object[] { "PF Form 5", ReportKind.PfForm5 },
        new object[] { "PF Form 6A", ReportKind.PfForm6A },
        new object[] { "PF Form 10", ReportKind.PfForm10 },
        new object[] { "PF Form 12A", ReportKind.PfForm12A },
        new object[] { "ESI Form 3", ReportKind.EsiForm3 },
        new object[] { "ESI Form 5", ReportKind.EsiForm5 },
        new object[] { "ESI Form 6", ReportKind.EsiForm6 },
    };

    /// <summary>
    /// 🔴 <b>THE REACHABILITY TEST.</b> Every row is driven the way an operator drives it — open the payroll
    /// statutory menu, arrow down to the label, press Enter — and must land on its own report. This is the test
    /// that fails if a form is built as a service method with no route in.
    /// </summary>
    [Theory]
    [MemberData(nameof(Rows))]
    public void Each_statutory_form_row_is_reachable_from_the_menu_and_opens_its_own_report(
        string label, ReportKind expected)
    {
        var vm = BuildPfAndEsiCompany();

        vm.ShowPayrollStatutoryReportsMenu();
        var guard = 0;
        while (vm.Menu[vm.SelectedIndex].Label != label)
        {
            vm.MoveDown();
            Assert.True(++guard < 100, $"'{label}' is not reachable by arrowing through the payroll statutory menu.");
        }
        vm.ActivateSelected();

        Assert.NotNull(vm.Reports);
        Assert.Equal(expected, vm.Reports!.Kind);
        Assert.False(string.IsNullOrWhiteSpace(vm.Reports.Title), "the form opened with no title");
    }

    /// <summary>
    /// The four multi-month forms carry the <b>statutory period</b> picker and the four monthly ones carry the
    /// <b>wage month</b> picker — never both, and never neither. A form scoped to the wrong window silently reports
    /// a different twelve (or six) months than the form it claims to be.
    /// </summary>
    [Theory]
    [InlineData(ReportKind.PfForm3A, true)]
    [InlineData(ReportKind.PfForm6A, true)]
    [InlineData(ReportKind.EsiForm5, true)]
    [InlineData(ReportKind.EsiForm6, true)]
    [InlineData(ReportKind.PfForm5, false)]
    [InlineData(ReportKind.PfForm10, false)]
    [InlineData(ReportKind.PfForm12A, false)]
    [InlineData(ReportKind.EsiForm3, false)]
    public void A_form_carries_exactly_one_period_picker_and_it_is_the_right_one(ReportKind kind, bool multiMonth)
    {
        var vm = BuildPfAndEsiCompany();
        vm.OpenPayrollStatutoryForm(kind);
        var r = vm.Reports!;

        Assert.Equal(multiMonth, r.ShowStatutoryPeriodPicker);
        Assert.Equal(!multiMonth, r.ShowPayrollMonthPicker);

        if (!multiMonth) return;

        // The offered windows are real statutory periods, not arbitrary dates.
        Assert.NotEmpty(r.StatutoryPeriods);
        foreach (var p in r.StatutoryPeriods)
        {
            if (kind is ReportKind.PfForm3A or ReportKind.PfForm6A)
            {
                Assert.Equal(3, p.From.Month);      // the PF currency period opens on 1 March
                Assert.Equal(1, p.From.Day);
                Assert.Equal(2, p.To.Month);        // ...and closes on 28/29 February
            }
            else
            {
                Assert.Contains(p.From.Month, new[] { 4, 10 });   // ESI contribution periods: Apr–Sep / Oct–Mar
                Assert.Equal(1, p.From.Day);
                Assert.Contains(p.To.Month, new[] { 9, 3 });
            }
        }
    }

    // --------------------------------------------------------------------- the realised visual tree

    /// <summary>
    /// 🔴 <b>THE FOOTNOTE IS ON SCREEN.</b> Every one of the eight forms prints at least one deliberately blank
    /// column, and each carries the footnote that says so. Before this wave's view work the footnotes were
    /// populated and undrawn, so this walks the realised tree for the text rather than asking the view model
    /// whether it has any.
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(Rows))]
    public void The_not_maintained_footnote_is_actually_rendered_on_every_statutory_form(
        string label, ReportKind kind)
    {
        _ = label;
        var (window, vm) = OpenWindow();
        try
        {
            vm.OpenPayrollStatutoryForm(kind);
            Pump(window);

            var notes = vm.Reports!.PayrollFootnotes.ToList();
            Assert.NotEmpty(notes);          // the projection's own claim...
            Assert.True(vm.Reports.HasPayrollFootnotes);

            // ...and the claim that actually matters: an operator can read it.
            foreach (var note in notes)
                Assert.True(IsTextVisible(window, note),
                    $"{kind} projected the footnote \"{note}\" but nothing on screen renders it — a blank "
                    + "statutory column with no visible footnote reads as a defect in the software.");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>FORM 6A PAGE 2 IS ON SCREEN, UNDER ITS OWN HEADINGS.</b> Page 2 is the twelve monthly challan
    /// remittances and its account heads are nothing like page 1's columns, so it needs its own column band; it was
    /// projected into <c>PayrollColumns2</c>/<c>PayrollRows2</c> and drawn by nothing at all. The assertion is on
    /// the realised tree: the page-2 caption, a page-2-only column heading, and a month cell from a page-2 row.
    /// </summary>
    [AvaloniaFact]
    public void Form_6A_page_two_and_its_own_column_headings_are_actually_rendered()
    {
        var (window, vm) = OpenWindow();
        try
        {
            vm.OpenPayrollStatutoryForm(ReportKind.PfForm6A);
            Pump(window);
            var r = vm.Reports!;

            Assert.True(r.HasPayrollSection2);
            Assert.Equal(12, r.PayrollRows2.Count);   // twelve remittance months

            Assert.True(IsTextVisible(window, r.PayrollSection2Title),
                "Form 6A's page-2 caption was projected but never drawn.");

            // A heading that exists ONLY on page 2 — proving page 2's own band was rendered rather than page 1's
            // headings being reused for page 2's figures.
            var pageTwoOnly = r.PayrollColumns2
                .Select(c => c.Header)
                .First(h => r.PayrollColumns.All(c1 => c1.Header != h));
            Assert.True(IsTextVisible(window, pageTwoOnly),
                $"Form 6A page 2's own column heading \"{pageTwoOnly}\" is not on screen — page 2's figures would "
                + "be reading under page 1's headings, or not showing at all.");

            // And a cell from a page-2 row.
            var firstMonthCell = r.PayrollRows2[0].Cells[1].Text;
            Assert.True(IsTextVisible(window, firstMonthCell),
                $"Form 6A page 2's first remittance row (\"{firstMonthCell}\") is not on screen.");
        }
        finally { window.Close(); }
    }

    /// <summary>Only the form that has a second page shows a second band — a section that drew unconditionally
    /// would satisfy the test above while putting an empty band under every other form.</summary>
    [AvaloniaFact]
    public void No_second_section_is_shown_on_a_form_that_has_only_one_page()
    {
        var (window, vm) = OpenWindow();
        try
        {
            vm.OpenPayrollStatutoryForm(ReportKind.PfForm12A);
            Pump(window);

            Assert.False(vm.Reports!.HasPayrollSection2);
            Assert.Empty(vm.Reports.PayrollRows2);
            Assert.Empty(vm.Reports.PayrollColumns2);
        }
        finally { window.Close(); }
    }

    /// <summary>Switching away from Form 6A clears its second section — a stale page 2 left under the next form
    /// would print another form's challan figures under this one's title.</summary>
    [AvaloniaFact]
    public void The_second_section_and_the_footnotes_are_cleared_when_another_report_is_opened()
    {
        var (window, vm) = OpenWindow();
        try
        {
            vm.OpenPayrollStatutoryForm(ReportKind.PfForm6A);
            Pump(window);
            Assert.True(vm.Reports!.HasPayrollSection2);
            var staleCaption = vm.Reports.PayrollSection2Title;

            vm.Reports.Show(ReportKind.TrialBalance);
            Pump(window);

            Assert.False(vm.Reports.HasPayrollSection2);
            Assert.Empty(vm.Reports.PayrollRows2);
            Assert.False(vm.Reports.HasPayrollFootnotes);
            Assert.Empty(vm.Reports.PayrollFootnotes);
            Assert.False(IsTextVisible(window, staleCaption),
                "Form 6A's page-2 caption is still on screen under a different report.");
        }
        finally { window.Close(); }
    }

    // ------------------------------------------------------------------------------- print and export

    /// <summary>
    /// 🔴 <b>Page 2 and the footnotes survive onto paper.</b> The print projector reads only the primary band, so
    /// before this wave a printed Form 6A carried page 1 and silently dropped the challan reconciliation, and every
    /// printed form dropped the footnote that explains its blank columns. A printed statutory form that omits its
    /// own footnotes misrepresents itself.
    /// </summary>
    [Fact]
    public void The_printed_form_carries_page_two_and_the_footnotes()
    {
        var vm = BuildPfAndEsiCompany();
        vm.OpenPayrollStatutoryForm(ReportKind.PfForm6A);
        var r = vm.Reports!;

        var print = ReportPrintProjector.Project(r);
        var text = string.Join("\n", print.Rows.Select(row => string.Join("|", row.Cells)));

        Assert.Contains("Page 2", text, StringComparison.Ordinal);
        Assert.Contains("A/c No. 10", text, StringComparison.Ordinal);

        // The print path transliterates to ASCII for the PDF's built-in font, so the footnotes' em dash does not
        // survive verbatim. Assert the segments that DO survive — every footnote's leading clause is plain ASCII —
        // rather than weakening this to "some footnote-ish text exists".
        Assert.NotEmpty(r.PayrollFootnotes);
        foreach (var note in r.PayrollFootnotes)
        {
            var asciiClause = note.Split('—')[0].Trim();
            Assert.False(string.IsNullOrEmpty(asciiClause));
            Assert.Contains(asciiClause, text, StringComparison.Ordinal);
        }
        Assert.Contains("complete by hand", text, StringComparison.Ordinal);

        // Page 1 is still there and still first — page 2 was appended, not substituted.
        Assert.Contains("Grand Total", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("Grand Total", StringComparison.Ordinal)
                  < text.IndexOf("Page 2", StringComparison.Ordinal));
    }

    /// <summary>The same for the spreadsheet export, with page 2's challan figures still typed as numbers so the
    /// one arithmetic anybody does with page 2 still works.</summary>
    [Fact]
    public void The_exported_form_carries_page_two_with_its_challan_figures_still_numeric()
    {
        var vm = BuildPfAndEsiCompany();
        vm.OpenPayrollStatutoryForm(ReportKind.PfForm6A);
        var r = vm.Reports!;

        var export = ReportTabularProjector.Project(r);
        var flat = export.Rows.SelectMany(row => row.Cells).ToList();

        Assert.Contains(flat, c => c.TextValue.Contains("Page 2", StringComparison.Ordinal));
        foreach (var note in r.PayrollFootnotes)
            Assert.Contains(flat, c => c.TextValue == note);

        // A page-2 remittance figure came through as a real NUMBER, not a string — page 2's whole use is summing
        // the challan account heads in a spreadsheet, which text cells cannot do.
        var biggestMonthlyAc1 = PfStatutoryForms
            .BuildForm6A(vm.Company!, r.SelectedStatutoryPeriod!.From)
            .Remittances.Max(x => x.EpfContributionsAccount1);
        Assert.True(biggestMonthlyAc1 > 0,
            "the fixture produced a nil challan — the numeric assertion would be vacuous");
        Assert.Contains(flat, c => c.HasNumber && c.NumberValue == biggestMonthlyAc1);
    }

    // ------------------------------------------------------------------- the half-set-up payroll

    /// <summary>
    /// 🔴 <b>An incompletely set-up payroll must not take the shell down.</b> A PF-applicable member with no valid
    /// 12-digit UAN makes <see cref="PfEcr.Build"/> throw, and <c>ReportsViewModel.Show</c> has no handler around
    /// its build switch — so before the guard, opening PF Form 3A on such a company threw straight out of the menu
    /// activation. The form must instead open and say what is wrong, exactly as the shipped PF ECR page does.
    /// </summary>
    [Fact]
    public void A_member_with_no_valid_uan_makes_the_form_explain_itself_instead_of_crashing_the_shell()
    {
        var vm = BuildPfAndEsiCompany();
        vm.Company!.Employees.First(e => e.PfApplicable).Uan = "12345";   // not a 12-digit UAN

        var ex = Record.Exception(() => vm.OpenPayrollStatutoryForm(ReportKind.PfForm3A));

        Assert.Null(ex);
        Assert.True(vm.Reports!.IsPayrollEmpty);
        Assert.Contains("UAN", vm.Reports.PayrollEmptyNote, StringComparison.Ordinal);
    }

    /// <summary>A company not enrolled for payroll statutory never reaches a statutory form at all.</summary>
    [Fact]
    public void A_company_not_enrolled_for_payroll_statutory_cannot_open_a_statutory_form()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "Bare Co";
        vm.CreateCompany();

        vm.OpenPayrollStatutoryForm(ReportKind.PfForm3A);

        Assert.True(vm.Reports is null || vm.Reports.Kind != ReportKind.PfForm3A);
    }

    // ------------------------------------------------------------------------------------------- harness

    private (MainWindow Window, MainWindowViewModel Vm) OpenWindow()
    {
        var vm = BuildPfAndEsiCompany();
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
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

    /// <summary>True when some realised, effectively-visible, non-degenerate <see cref="TextBlock"/> in the window
    /// carries <paramref name="text"/>. Non-degenerate matters: a zero-height TextBlock in a collapsed panel is not
    /// something an operator can read.</summary>
    private static bool IsTextVisible(MainWindow window, string text)
        => !string.IsNullOrEmpty(text)
        && Descendants(window).Any(v =>
            v is TextBlock { IsEffectivelyVisible: true } t
            && t.Bounds.Width > 0 && t.Bounds.Height > 0
            && t.Text is not null
            && t.Text.Contains(text, StringComparison.Ordinal));

    /// <summary>
    /// A company enrolled for BOTH Provident Fund and ESI, with one member on a ₹20,000 structure from the
    /// financial-year start, and the payroll run posted. Deliberately structured from the FY start (1 April) while
    /// the PF currency period opens on 1 March — the ordinary shape that used to make Forms 3A / 6A / 12A throw.
    /// </summary>
    private MainWindowViewModel BuildPfAndEsiCompany()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "Statutory Forms Co " + Guid.NewGuid().ToString("N")[..8];
        vm.CreateCompany();
        var c = vm.Company!;
        var fyStart = new DateOnly(c.FinancialYearStart.Year, c.FinancialYearStart.Month, 1);
        var fyEnd = fyStart.AddMonths(1).AddDays(-1);

        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableProvidentFund(capWagesAtCeiling: true);
        pay.EnableEsi(employerCode: "12345678901234567");

        var ph = new PayHeadService(c);
        var indirect = c.FindGroupByName("Indirect Expenses")!.Id;
        var liab = c.FindGroupByName("Current Liabilities")!.Id;

        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: indirect, partOfPfWages: true, partOfEsiWages: true);
        var eePf = ph.CreatePayHead("Employee EPF", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            pfComponent: PfStatutoryComponent.EmployeeProvidentFund);
        var erPf = ph.CreatePayHead("Employer EPF", PayHeadType.EmployersStatutoryContributions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            pfComponent: PfStatutoryComponent.EmployerProvidentFund);
        var eps = ph.CreatePayHead("Employer Pension", PayHeadType.EmployersStatutoryContributions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            pfComponent: PfStatutoryComponent.EmployerPension);
        var edli = ph.CreatePayHead("EDLI", PayHeadType.EmployersOtherCharges,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            pfComponent: PfStatutoryComponent.EmployeesDepositLinkedInsurance);
        var eeEsi = ph.CreatePayHead("Employee ESI", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            esiComponent: EsiStatutoryComponent.EmployeeStateInsurance);
        var erEsi = ph.CreatePayHead("Employer ESI", PayHeadType.EmployersStatutoryContributions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            esiComponent: EsiStatutoryComponent.EmployerStateInsurance);

        var e = pay.CreateEmployee("Sanjay Kumar", pay.CreateEmployeeGroup("Staff").Id,
            employeeNumber: "E-100", uan: Uan, esiNumber: Ip);
        pay.SetEmployeePfDetails(e.Id, applicable: true, contributeOnHigherWages: false);
        pay.SetEmployeeEsiDetails(e.Id, applicable: true);
        e.PfAccountNumber = "MH/BAN/0000001/000/0000123";
        e.DateOfJoining = fyStart;
        e.PfJoinDate = fyStart;
        e.DateOfBirth = new DateOnly(1990, 5, 4);
        e.Gender = "Male";
        e.Designation = "Machine Operator";

        new SalaryStructureService(c).DefineForEmployee(e.Id, fyStart, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(20000m)),
            new SalaryStructureLine(eePf.Id, 1),
            new SalaryStructureLine(erPf.Id, 2),
            new SalaryStructureLine(eps.Id, 3),
            new SalaryStructureLine(edli.Id, 4),
            new SalaryStructureLine(eeEsi.Id, 5),
            new SalaryStructureLine(erEsi.Id, 6),
        });

        new PayrollVoucherService(c).Post(fyStart, fyEnd, new[] { e.Id });
        _storage.Save(c);
        return vm;
    }
}
