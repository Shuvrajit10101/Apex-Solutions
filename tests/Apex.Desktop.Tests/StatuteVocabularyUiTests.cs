using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Services;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// CA slice S9 — the Income-tax Act <b>1961 → 2025 vocabulary gate in the UI</b>.
///
/// <para>The dangerous part of a statutory relabelling is not the label; it is that menu activation in this shell
/// <b>dispatches on the label string</b>. Renaming "Form 24Q" to "Form 138" without teaching the dispatcher the new
/// name would leave the item visibly present but <b>dead</b> — the worst kind of regression, because a
/// presence-only assertion still passes. Every test below therefore <b>drives the real handler</b>
/// (<see cref="MainWindowViewModel.DrillIn"/>) and asserts the screen actually opened, rather than asserting the
/// item merely exists.</para>
///
/// <para>Drives the real view models headlessly — no UI toolkit, no rendering.</para>
/// </summary>
public sealed class StatuteVocabularyUiTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public StatuteVocabularyUiTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexStatuteVocabUiTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    // ---------------------------------------------------------------- harness

    /// <summary>A salary-TDS-enabled company whose financial year starts in <paramref name="fyStartYear"/> — the one
    /// input the whole vocabulary gate keys off.</summary>
    private MainWindowViewModel SalaryTdsCompany(string name, int fyStartYear)
    {
        var vm = OpenCompanyWithFy(name, fyStartYear);
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.PayrollEnabled = true;
        page.PayrollStatutoryEnabled = true;
        page.SalaryTdsEnabled = true;
        Assert.True(page.ApplySalaryTds());
        vm.Back();
        return vm;
    }

    /// <summary>Saves a company whose financial year starts in <paramref name="fyStartYear"/> and opens it through the
    /// real company-select path — the shell exposes no other public way in, and a test must not widen production
    /// visibility to get one.</summary>
    private MainWindowViewModel OpenCompanyWithFy(string name, int fyStartYear)
    {
        var fyStart = new DateOnly(fyStartYear, 4, 1);
        _storage.Save(CompanyFactory.CreateSeeded(name, fyStart, fyStart));

        var vm = new MainWindowViewModel(_storage);
        vm.ShowCompanySelect();
        vm.Menu.Single(m => m.Label == name).Activate();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        Assert.Equal(fyStartYear, vm.Company!.FinancialYearStart.Year);
        return vm;
    }

    /// <summary>The rightmost cascade column — the one the keyboard acts on.</summary>
    private static GatewayColumn Active(MainWindowViewModel vm) => vm.Columns[vm.ActiveColumnIndex];

    /// <summary>Highlights the menu row whose label is <paramref name="label"/> and drills into it through the real
    /// keyboard handler — the assertion that the label is not just present but <b>wired</b>.</summary>
    private static void DrillInto(MainWindowViewModel vm, string label)
    {
        var col = Active(vm);
        var index = col.Items.ToList().FindIndex(i => i.Label == label);
        Assert.True(index >= 0, $"No menu row labelled '{label}'. Present: {string.Join(" | ", col.Items.Select(i => i.Label))}");
        col.SetSelected(index);
        Assert.Same(col.Items[index], col.Selected);   // the row really is highlighted before we drill
        vm.DrillIn();
    }

    // ---------------------------------------------------------------- FY 2025-26 — the 1961 Act (unchanged)

    /// <summary>ER-13: a prior-year company's menu is exactly what it was before S9.</summary>
    [Fact]
    public void A_prior_year_company_still_shows_the_1961_act_form_numbers()
    {
        var vm = SalaryTdsCompany("Prior Year Co", 2025);
        vm.ShowPayrollStatutoryReportsMenu();

        var labels = Active(vm).Items.Select(i => i.Label).ToList();
        Assert.Contains("Form 24Q", labels);
        Assert.Contains("Form 16", labels);
        Assert.DoesNotContain("Form 138", labels);
        Assert.DoesNotContain("Form 130", labels);

        DrillInto(vm, "Form 24Q");
        Assert.Equal(Screen.Form24Q, vm.CurrentScreen);
        Assert.NotNull(vm.Form24Q);
    }

    // ---------------------------------------------------------------- FY 2026-27 — the 2025 Act

    /// <summary>
    /// <b>The reachability guard.</b> From FY 2026-27 the salary-TDS return is Form 138 and the certificate is
    /// Form 130 — and both must still <b>open</b>. If the label/dispatch pair ever drifts apart, this goes red where
    /// a "the item is in the list" assertion would not.
    /// </summary>
    [Fact]
    public void A_2025_act_company_shows_the_renumbered_forms_and_they_still_open()
    {
        var vm = SalaryTdsCompany("Tax Year Co", 2026);
        vm.ShowPayrollStatutoryReportsMenu();

        var labels = Active(vm).Items.Select(i => i.Label).ToList();
        Assert.Contains("Form 138", labels);
        Assert.Contains("Form 130", labels);
        Assert.DoesNotContain("Form 24Q", labels);
        Assert.DoesNotContain("Form 16", labels);

        DrillInto(vm, "Form 138");
        Assert.Equal(Screen.Form24Q, vm.CurrentScreen);
        Assert.NotNull(vm.Form24Q);

        vm.Back();
        vm.ShowPayrollStatutoryReportsMenu();
        DrillInto(vm, "Form 130");
        Assert.Equal(Screen.Form16, vm.CurrentScreen);
        Assert.NotNull(vm.Form16);
    }

    /// <summary>The TDS/TCS return menus renumber too — and stay reachable.</summary>
    [Fact]
    public void The_tds_and_tcs_return_menus_renumber_and_stay_reachable()
    {
        var vm = OpenCompanyWithFy("TDS Tax Year Co", 2026);

        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.TdsEnabled = true;
        page.Tan = "MUMA12345B";
        Assert.True(page.ApplyTds());
        vm.Back();

        vm.ShowGstReportsMenu();
        var labels = Active(vm).Items.Select(i => i.Label).ToList();
        Assert.Contains("Form 140", labels);   // 26Q
        Assert.Contains("Form 131", labels);   // 16A

        // Form 27A has NO confirmed renumbering — it must survive the gate untouched.
        Assert.Contains("Form 27A (TDS)", labels);

        DrillInto(vm, "Form 140");
        Assert.Equal(Screen.Form26Q, vm.CurrentScreen);
    }

    // ---------------------------------------------------------------- the period caption

    /// <summary>
    /// The F11 salary-TDS block's period caption is FY-gated, and the <b>value</b> moves with it. This is the
    /// numeric collision made concrete in the UI: both companies below display <b>"2026-27"</b>, for different
    /// years — only the caption tells them apart, which is why the caption is bound rather than hardcoded.
    /// </summary>
    [Fact]
    public void The_config_period_caption_and_value_are_both_fy_gated()
    {
        var prior = SalaryTdsCompany("Caption Prior Co", 2025);
        prior.ShowGstConfig();
        Assert.Equal("Assessment Year", prior.GstConfig!.SalaryTdsPeriodCaption);
        Assert.Equal("2026-27", prior.GstConfig!.SalaryTdsAssessmentYearLabel);
        Assert.Equal("2025-26", prior.GstConfig!.SalaryTdsFinancialYearLabel);

        var current = SalaryTdsCompany("Caption Tax Year Co", 2026);
        current.ShowGstConfig();
        Assert.Equal("Tax Year", current.GstConfig!.SalaryTdsPeriodCaption);
        Assert.Equal("2026-27", current.GstConfig!.SalaryTdsAssessmentYearLabel);
        Assert.Equal("2026-27", current.GstConfig!.SalaryTdsFinancialYearLabel);

        // Same numerals, different years — the captions are the only discriminator.
        Assert.Equal(prior.GstConfig!.SalaryTdsAssessmentYearLabel, current.GstConfig!.SalaryTdsAssessmentYearLabel);
        Assert.NotEqual(prior.GstConfig!.SalaryTdsPeriodCaption, current.GstConfig!.SalaryTdsPeriodCaption);
    }

    // ---------------------------------------------------------------- XAML wiring (headless-safe, no rendering)

    private static string AxamlPath([CallerFilePath] string thisFile = "")
    {
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
        return Path.Combine(repoRoot, "src", "Apex.Desktop", "Views", "MainWindow.axaml");
    }

    /// <summary>
    /// <b>The regression lock on the view.</b> Every statute-dependent caption must be a <b>binding</b>, never a
    /// literal — a hardcoded "Assessment Year"/"AY"/"§192" would silently display the wrong vocabulary for a
    /// 2025-Act company, and no view-model test would catch it because the view model would be perfectly correct.
    /// <para>Asserted by parsing the real MainWindow.axaml, so it is green on the plain-headless 3-OS CI (no Skia,
    /// no TextLayout, no CaptureRenderedFrame).</para>
    /// </summary>
    [Fact]
    public void The_statute_dependent_captions_are_bound_not_hardcoded_in_the_view()
    {
        var axaml = File.ReadAllText(AxamlPath());

        // The captions S9 replaced must no longer appear as literal Text="…" attributes anywhere in the view.
        Assert.DoesNotContain("Text=\"Assessment Year\"", axaml);
        Assert.DoesNotContain("Text=\"AY\"", axaml);
        Assert.DoesNotContain("Text=\"Annexure I — deductee-wise §192 TDS\"", axaml);
        Assert.DoesNotContain("Text=\"TCS — Nature of Goods (§206C)\"", axaml);

        // …and the bindings that replaced them are actually wired.
        Assert.Contains("Text=\"{Binding SalaryTdsPeriodCaption}\"", axaml);
        Assert.Contains("Text=\"{Binding PeriodCaptionShort}\"", axaml);
        Assert.Contains("Text=\"{Binding AnnexureIHeading}\"", axaml);
        Assert.Contains("Text=\"{Binding TcsNatureOfGoodsCaption}\"", axaml);
    }

    /// <summary>
    /// The §206AA / §206CC label comments in the view guard a REAL defect (a fixed-width label sliced "…§206AA)"
    /// down to "…§206A", a different section). S9 renamed nothing in that family, and this pins that: neither
    /// no-PAN section may acquire a 2025-Act number, because neither was verified against a primary source.
    /// </summary>
    [Fact]
    public void The_unverified_no_pan_sections_are_not_relabelled_in_the_view()
    {
        var axaml = File.ReadAllText(AxamlPath());
        Assert.Contains("§206AA", axaml);
        Assert.Contains("§206CC", axaml);
        Assert.Equal("206AA", StatuteVocabulary.SectionLabel("206AA", 2026));
        Assert.Equal("206CC", StatuteVocabulary.SectionLabel("206CC", 2026));
    }

    /// <summary>The Form 24Q page title and Annexure I heading cite §192 before the cutover and §392 after it.</summary>
    [Fact]
    public void The_form_24q_page_cites_the_right_salary_section_for_its_year()
    {
        var prior = SalaryTdsCompany("Section Prior Co", 2025);
        prior.OpenForm24Q();
        Assert.Contains("§192", prior.Form24Q!.Title);
        Assert.Equal("Annexure I — deductee-wise §192 TDS", prior.Form24Q!.AnnexureIHeading);
        Assert.Contains("Form 24Q", prior.Form24Q!.Title);

        var current = SalaryTdsCompany("Section Tax Year Co", 2026);
        current.OpenForm24Q();
        Assert.Contains("§392", current.Form24Q!.Title);
        Assert.Equal("Annexure I — deductee-wise §392 TDS", current.Form24Q!.AnnexureIHeading);
        Assert.Contains("Form 138", current.Form24Q!.Title);
    }

    // ------------------------------------------------- the four-surface agreement (CA S9 closeout)

    /// <summary>One renumbered statutory form and the four places its number is rendered.</summary>
    private sealed record FormSurface(
        string Legacy,          // the 1961-Act number  (FY <= 2025)
        string Renumbered,      // the confirmed 2025-Act number (FY >= 2026)
        string Descriptor,      // the parenthetical in the screen title
        Screen Screen,
        Action<MainWindowViewModel> OpenMenu)
    {
        public string Expected(int fyStartYear) => fyStartYear >= 2026 ? Renumbered : Legacy;
    }

    /// <summary>All six renumbered TDS/TCS forms, with the numbers written out <b>literally</b> — deliberately not
    /// derived from <see cref="StatuteVocabulary"/>, so this test cannot agree with a mistake in the very table it
    /// exists to police.</summary>
    private static FormSurface[] AllSixForms() =>
    [
        new("24Q",  "138", "Quarterly Salary-TDS Return", Screen.Form24Q,  v => v.ShowPayrollStatutoryReportsMenu()),
        new("16",   "130", "Salary-TDS Certificate",      Screen.Form16,   v => v.ShowPayrollStatutoryReportsMenu()),
        new("26Q",  "140", "Quarterly TDS Return",        Screen.Form26Q,  v => v.ShowGstReportsMenu()),
        new("16A",  "131", "TDS Certificate",             Screen.Form16A,  v => v.ShowGstReportsMenu()),
        new("27EQ", "143", "Quarterly TCS Return",        Screen.Form27EQ, v => v.ShowGstReportsMenu()),
        new("27D",  "133", "TCS Certificate",             Screen.Form27D,  v => v.ShowGstReportsMenu()),
    ];

    /// <summary>A company with salary-TDS, TDS <b>and</b> TCS on, so all six forms are reachable from one company.</summary>
    private MainWindowViewModel AllStatutoryCompany(string name, int fyStartYear)
    {
        var vm = SalaryTdsCompany(name, fyStartYear);

        vm.ShowGstConfig();
        vm.GstConfig!.TdsEnabled = true;
        vm.GstConfig!.Tan = "MUMA12345B";
        Assert.True(vm.GstConfig!.ApplyTds());
        vm.Back();

        vm.ShowGstConfig();
        vm.GstConfig!.TcsEnabled = true;
        vm.GstConfig!.Tan = "MUMA12345B";
        Assert.True(vm.GstConfig!.ApplyTcs());
        vm.Back();
        return vm;
    }

    /// <summary>
    /// <b>The four surfaces of a form number must agree.</b> For every one of the six renumbered forms, at both
    /// sides of the cutover, the <b>menu label</b>, the <b>column header</b>, the <b>screen title</b> and the
    /// <b>page heading</b> must all cite the same number.
    ///
    /// <para><b>Why this shape.</b> This shell is a <b>Miller cascade</b>: opening a page does not replace the menu
    /// that opened it, it appends a column beside it. Menu label and page heading are therefore on screen
    /// <i>simultaneously</i>, and a disagreement is a directly self-contradicting screen — "Form 130" highlighted in
    /// the left pane next to a pane headed "Form 16". Four of the six handlers shipped exactly that, because the
    /// only guard was a reachability test that asserted <c>Screen.Form26Q</c> was reached and never looked at the
    /// resulting <b>title</b>: every one of those four <i>did</i> open, so a screen-enum assertion stayed green while
    /// the screen contradicted itself. This test therefore drives the real handler <i>and reads the text</i>.</para>
    /// </summary>
    [Theory]
    [InlineData(2025)]
    [InlineData(2026)]
    public void Menu_label_column_header_screen_title_and_page_heading_agree_for_every_renumbered_form(int fyStartYear)
    {
        var vm = AllStatutoryCompany($"Agreement Co {fyStartYear}", fyStartYear);

        foreach (var form in AllSixForms())
        {
            var expected = $"Form {form.Expected(fyStartYear)}";

            vm.ShowGateway();
            form.OpenMenu(vm);

            // 1. the MENU LABEL — and it must really be wired, so we drill through the keyboard handler.
            var menuLabels = Active(vm).Items.Select(i => i.Label).ToList();
            Assert.True(menuLabels.Contains(expected),
                $"FY {fyStartYear}: no menu row '{expected}'. Present: {string.Join(" | ", menuLabels)}");
            DrillInto(vm, expected);

            // The label opened the screen it claims to open.
            Assert.Equal(form.Screen, vm.CurrentScreen);

            // 2. the COLUMN HEADER of the page column that just opened.
            var pageColumn = Active(vm);
            Assert.True(pageColumn.IsPage, $"FY {fyStartYear}: '{expected}' did not open a page column.");
            Assert.Equal(expected, pageColumn.Title);

            // 3. the SCREEN TITLE.
            Assert.Equal($"{expected} ({form.Descriptor})", vm.ScreenTitle);

            // 4. the PAGE HEADING — the largest text on the page, and the one the user reads first.
            var heading = pageColumn.Page!.GetType().GetProperty("Title")!.GetValue(pageColumn.Page) as string;
            Assert.NotNull(heading);
            Assert.StartsWith($"{expected} —", heading);

            // The superseded vocabulary must not survive anywhere on the four surfaces.
            var superseded = $"Form {(fyStartYear >= 2026 ? form.Legacy : form.Renumbered)}";
            foreach (var surface in new[] { pageColumn.Title, vm.ScreenTitle, heading! })
                Assert.DoesNotContain(superseded, surface);
        }
    }

    /// <summary>
    /// <b>ER-13 for the printed artifact.</b> An FY 2025-26 certificate prints the 1961-Act header and exports under
    /// the 1961-Act file name, exactly as it always has; an FY 2026-27 certificate prints the 2025-Act header and
    /// exports under the 2025-Act name. The printed header and the period row on the same page must never cite
    /// different statutes.
    /// </summary>
    [Fact]
    public void The_printed_form_16_header_and_export_name_are_gated_on_the_certificate_year()
    {
        var prior = SalaryTdsCompany("Print Prior Co", 2025);
        prior.OpenForm16();
        Assert.Equal("Form 16 — Salary-TDS Certificate (§192)", prior.Form16!.Title);
        Assert.Equal("AY", prior.Form16!.PeriodCaptionShort);
        Assert.StartsWith("Form16_2025_26_", prior.Form16!.ExportResolvedFileName);
        Assert.EndsWith(".pdf", prior.Form16!.ExportResolvedFileName);

        var current = SalaryTdsCompany("Print Tax Year Co", 2026);
        current.OpenForm16();
        Assert.Equal("Form 130 — Salary-TDS Certificate (§392)", current.Form16!.Title);
        Assert.Equal("Tax Year", current.Form16!.PeriodCaptionShort);
        Assert.StartsWith("Form130_2026_27_", current.Form16!.ExportResolvedFileName);

        // The repealed vocabulary is gone from the 2025-Act artifact entirely.
        Assert.DoesNotContain("Form 16 ", current.Form16!.Title);
        Assert.DoesNotContain("§192", current.Form16!.Title);
        Assert.DoesNotContain("Form16_", current.Form16!.ExportResolvedFileName);
    }

    /// <summary>
    /// <b>The printed certificate itself.</b> The two assertions above read view-model strings; this one exports the
    /// real PDF through the injectable seam and reads the <b>bytes that would reach the printer</b>, because the
    /// printed header is a <i>separate literal</i> from the on-screen heading and a test on one proves nothing about
    /// the other. The gate is keyed on the certificate's own financial year, so FY 2025-26 prints the 1961-Act
    /// header verbatim (ER-13) and FY 2026-27 prints the 2025-Act one — and neither page may carry both vocabularies,
    /// which is what an ungated header did: "Form 16 … (Section 192)" over a period row reading "Tax Year 2026-27".
    /// </summary>
    [Theory]
    [InlineData(2025, "Form 16", "Section 192", "Form 130", "Section 392")]
    [InlineData(2026, "Form 130", "Section 392", "Form 16", "Section 192")]
    public void The_exported_form_16_pdf_prints_the_vocabulary_of_its_own_year(
        int fyStartYear, string expectedForm, string expectedSection, string forbiddenForm, string forbiddenSection)
    {
        var vm = SalaryTdsCompany($"Printed Cert Co {fyStartYear}", fyStartYear);
        var company = vm.Company!;
        SeedOneSalariedEmployee(company);
        _storage.Save(company);

        vm.OpenForm16();
        var page = vm.Form16!;
        page.SelectedYear = page.FinancialYears.First(y => y.StartYear == fyStartYear);
        Assert.NotNull(page.Certificate);   // a real certificate, not an empty page

        byte[]? pdf = null;
        string? path = null;
        Assert.True(page.ExportPdf((p, b) => { path = p; pdf = b; }));
        Assert.NotNull(pdf);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf!, 0, 4));

        // The PDF content stream is uncompressed, so the printed header is readable in the bytes.
        var printed = Encoding.Latin1.GetString(pdf!);
        Assert.Contains(expectedForm, printed);
        Assert.Contains(expectedSection, printed);
        Assert.DoesNotContain(forbiddenSection, printed);

        // "Form 16" is a prefix of nothing else here, but "Form 130" contains no "Form 16" either way — assert the
        // superseded FORM number is absent as a whole token by checking the em-dash-terminated title fragment.
        Assert.DoesNotContain($"{forbiddenForm} \\227", printed);
        Assert.DoesNotContain($"{forbiddenForm} —", printed);

        // The file name carries the same year's form number.
        Assert.StartsWith($"{expectedForm.Replace(" ", "")}_", Path.GetFileName(path)!);
    }

    /// <summary>One NEW-regime employee on ₹1,25,000/month with twelve posted payroll vouchers — enough §192 activity
    /// for Form 16 to produce a certificate. Mirrors the Phase-8 salary-TDS harness.</summary>
    private static void SeedOneSalariedEmployee(Company c)
    {
        var ph = new PayHeadService(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: c.FindGroupByName("Indirect Expenses")!.Id);
        var tds = ph.CreatePayHead("TDS on Salary", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: c.FindGroupByName("Current Liabilities")!.Id,
            incomeTaxComponent: IncomeTaxComponent.TaxDeductedAtSource);

        var groupId = new PayrollService(c).CreateEmployeeGroup("Staff").Id;
        var e = new PayrollService(c).CreateEmployee("Anita Rao", groupId);
        var emp = c.FindEmployee(e.Id)!;
        emp.ApplicableTaxRegime = TaxRegime.New;
        emp.Pan = "ABCDE1234F";
        new SalaryStructureService(c).DefineForEmployee(e.Id, c.FinancialYearStart, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(1_25_000m)),
            new SalaryStructureLine(tds.Id, 1),
        });

        var svc = new PayrollVoucherService(c);
        var d = c.FinancialYearStart;
        for (var i = 0; i < 12; i++)
        {
            svc.Post(d, new DateOnly(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month)), new[] { e.Id });
            d = d.AddMonths(1);
        }
    }
}
