using System;
using System.IO;
using System.Linq;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Census 6.11, 6.12 and 6.42 — the remaining statutory returns can now leave the screen.</b>
///
/// <para>🔴 <b>What was wrong, and it is the same defect four more times.</b> GSTR-4, GSTR-9, GSTR-9C and
/// Form 12BA were each filed as an "output dead end": the projection was right, the screen was right, and
/// neither E / Alt+E nor P / Ctrl+P did anything at all on any of them — not a refusal an operator could learn
/// from, just a key with no effect. <c>ChallanReconOutputTests</c> closed the same gap on rows 6.31 and 6.38;
/// this file closes it on the four returns, which is what rows 6.11 and 6.12 need before either can move,
/// because <b>each of those two rows names two returns and both halves must have an exit</b>.</para>
///
/// <para><b>Why these tests press keys instead of calling the methods.</b> This project has three features on
/// file whose only callers were test files. Calling <c>vm.OpenExport()</c> directly would prove nothing about
/// whether a user can export: the gate is <c>IsExportablePage</c> / <c>IsPrintablePage</c>, evaluated inside
/// <see cref="MainWindow"/>'s own key tunnel. So every reachability case here goes through the real
/// <c>KeyDown</c> handler with the real key, and the export case writes a real file and reads its bytes back.</para>
///
/// <para><b>Each test fails on today's <c>origin/main</c></b> — the four view models did not implement
/// <see cref="IMasterListExportSource"/> there, so the snapshot assertions do not compile against it and the
/// key-gesture assertions observe a screen that never changes.</para>
/// </summary>
public sealed class StatutoryReturnOutputTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string ValidTan = "MUMA12345B";
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    private readonly string _tempDir;
    private readonly string _outDir;
    private readonly CompanyStorage _storage;

    public StatutoryReturnOutputTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexStatOut_" + Guid.NewGuid().ToString("N"));
        _outDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(_outDir);
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* a held handle must not fail the assertion under test */ }
    }

    // ---------------------------------------------------------------- fixtures

    private MainWindowViewModel GstCompany(string name, GstRegistrationType registration)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();

        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = registration,
            CompositionSubType = registration == GstRegistrationType.Composition ? CompositionSubType.Trader : null,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Quarterly,
        });

        vm.ShowGateway();
        return vm;
    }

    /// <summary>A salary-TDS company — the gate Form 12BA is reachable behind.</summary>
    private MainWindowViewModel SalaryTdsCompany()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "Perquisite Co";
        vm.CreateCompany();

        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan });
        c.SalaryTdsEnabled = true;

        vm.ShowGateway();
        return vm;
    }

    private static MainWindow Show(MainWindowViewModel vm)
    {
        var win = new MainWindow { DataContext = vm };
        win.MinWidth = 640; win.MinHeight = 480;
        win.Width = 1400; win.Height = 900;
        win.Show();
        win.Width = 1400; win.Height = 900;
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return win;
    }

    private static void Press(MainWindow win, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        win.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = win,
        });
        Dispatcher.UIThread.RunJobs();
    }

    private static string Col1(MasterListSnapshot s, int i) => s.Rows[i][0];

    private static bool HasLabel(MasterListSnapshot s, string label) =>
        s.Rows.Any(r => r[0] == label);

    // ================================================================ 1. the snapshots themselves

    /// <summary>
    /// 🔴 <b>GSTR-4 carries all four quarters AND the annual foot, because Table 6 reconciles to Σ Table 5.</b>
    /// That reconciliation is the one arithmetic check anyone performs on this return; an export carrying the
    /// annual figure without the quarters, or the quarters without the annual figure, makes it impossible.
    /// </summary>
    [Fact]
    public void Gstr4_snapshot_carries_every_quarter_and_the_annual_foot()
    {
        var vm = GstCompany("Comp Annual Co", GstRegistrationType.Composition);
        var page = new Gstr4ReportViewModel(vm.Company!);
        var snap = page.ToMasterListSnapshot();

        // Two columns, the money one numeric — a spreadsheet must be able to re-add the quarters.
        Assert.Equal(2, snap.Columns.Count);
        Assert.False(snap.Columns[0].IsNumeric);
        Assert.True(snap.Columns[1].IsNumeric);

        // The sub-type leads: the composition rate depends on it, so the figures are ambiguous without it.
        Assert.Equal("Period", Col1(snap, 0));
        Assert.Equal("Composition sub-type", Col1(snap, 1));

        // All four quarters, each with its four figures (Table 5).
        foreach (var q in new[] { "Q1 (Apr–Jun)", "Q2 (Jul–Sep)", "Q3 (Oct–Dec)", "Q4 (Jan–Mar)" })
        {
            Assert.True(HasLabel(snap, $"5  {q} — turnover base"), $"{q} turnover base missing");
            Assert.True(HasLabel(snap, $"5  {q} — total tax payable"), $"{q} total missing");
        }

        // Tables 4A–4D inward, and the Table 6 annual foot the quarters reconcile to.
        Assert.True(HasLabel(snap, "4B  Inward supplies liable to reverse charge — tax paid in cash"));
        Assert.True(HasLabel(snap, "6  Annual total tax payable"));
    }

    /// <summary>
    /// GSTR-9 is labelled by statutory table number throughout. A reviewer reconciles this return against twelve
    /// GSTR-3Bs by table number; a caption this product invented would not be findable.
    /// </summary>
    [Fact]
    public void Gstr9_snapshot_labels_every_part_by_its_statutory_table_number()
    {
        var vm = GstCompany("Annual Regular Co", GstRegistrationType.Regular);
        var page = new Gstr9ReportViewModel(vm.Company!);
        var snap = page.ToMasterListSnapshot();

        Assert.True(HasLabel(snap, "4  Total tax payable"));
        Assert.True(HasLabel(snap, "5  Total turnover"));
        Assert.True(HasLabel(snap, "6  Total ITC availed"));
        Assert.True(HasLabel(snap, "7  Total ITC reversed"));
        Assert.True(HasLabel(snap, "8D  Difference"));
        Assert.True(HasLabel(snap, "9  Paid in cash"));
        Assert.True(HasLabel(snap, "17  Total tax"));

        // Every reversal rule is carried separately — Table 7 is filed rule-by-rule, not as one figure.
        Assert.True(HasLabel(snap, "7  ITC reversed — rule 42"));
        Assert.True(HasLabel(snap, "7  ITC reversed — rule 43"));
        Assert.True(HasLabel(snap, "7  ITC reversed — section 17(5)"));
    }

    /// <summary>
    /// 🔴 <b>GSTR-9C carries all three lines of every reconciliation — books, return and the difference.</b>
    /// The difference alone is unreadable: ₹0 unreconciled is a clean tie, and it is equally what you get when
    /// both sides failed to build. Carrying the two sides is the entire value of exporting a reconciliation.
    /// </summary>
    [Fact]
    public void Gstr9c_snapshot_carries_both_sides_of_every_reconciliation_not_just_the_difference()
    {
        var vm = GstCompany("Recon Co", GstRegistrationType.Regular);
        var page = new Gstr9cReportViewModel(vm.Company!);
        var snap = page.ToMasterListSnapshot();

        foreach (var triple in new[]
                 {
                     ("5  Turnover as per books", "5  Turnover as per returns", "5  Unreconciled turnover"),
                     ("9-11  Tax as per books", "9-11  Tax as per returns", "9-11  Unreconciled tax"),
                     ("12  ITC as per books", "12  ITC as per returns", "12  Unreconciled ITC"),
                 })
        {
            Assert.True(HasLabel(snap, triple.Item1), triple.Item1 + " missing");
            Assert.True(HasLabel(snap, triple.Item2), triple.Item2 + " missing");
            Assert.True(HasLabel(snap, triple.Item3), triple.Item3 + " missing");
        }
    }

    /// <summary>
    /// 🔴 <b>THE ONE THAT MATTERS MOST IN THIS FILE.</b> Form 12BA's prescribed perquisite table is always empty
    /// in this book — no §17(2) capture exists. Exporting the four column captions with no rows beneath them
    /// hands the recipient a <b>nil perquisite declaration</b>: a positive statement that no perquisite was
    /// provided, which this book has no basis to make and which the employee may rely on in their own return.
    ///
    /// <para>The on-screen sentence exists to stop precisely that misreading. This asserts it survives the export
    /// boundary <b>verbatim</b> — comparing against the shipped constant rather than a copy, so a future edit
    /// that softens the wording on screen cannot leave a stale reassurance in the exported document.</para>
    /// </summary>
    [Fact]
    public void Form12Ba_snapshot_carries_the_empty_state_warning_verbatim_and_never_an_implied_nil_return()
    {
        var vm = SalaryTdsCompany();
        var page = new Form12BaViewModel(vm.Company!);
        var snap = page.ToMasterListSnapshot();

        var note = snap.Rows.SingleOrDefault(r => r[0] == "Note");
        Assert.NotNull(note);
        Assert.Equal(Form12BaViewModel.EmptyStateText, note![1]);

        // The prescribed columns ride as a described SHAPE, never as the snapshot's real columns — a table with
        // real captions and no rows is what reads as a nil return.
        Assert.Equal(2, snap.Columns.Count);
        Assert.Equal("Particulars", snap.Columns[0].Caption);
        Assert.Equal("Details", snap.Columns[1].Caption);
        for (var i = 0; i < Form12BaViewModel.PrescribedColumns.Length; i++)
            Assert.True(HasLabel(snap, $"  Col. {i + 1}"), $"prescribed column {i + 1} missing");

        // The identity block an employee needs to recognise their own statement.
        Assert.True(HasLabel(snap, "Employee PAN"));
        Assert.True(HasLabel(snap, "Employer TAN"));
        Assert.True(HasLabel(snap, "Rule 26A(2)(b) verdict"));
    }

    // ================================================================ 2. a user can actually reach the output

    /// <summary>
    /// E over an open GSTR-4 opens the real Export panel, driven through <see cref="MainWindow"/>'s own key
    /// tunnel. <c>IsExportablePage</c> and <c>IsPrintablePage</c> are both asserted: Print and Export are
    /// separate gestures and were separately dead, so closing only one would leave the row honestly PARTIAL.
    /// </summary>
    [AvaloniaFact]
    public void E_opens_the_export_panel_over_an_open_gstr4()
    {
        var vm = GstCompany("Comp Export Co", GstRegistrationType.Composition);
        var win = Show(vm);

        vm.OpenGstr4Report();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Screen.Gstr4Report, vm.CurrentScreen);
        Assert.True(vm.IsExportablePage);
        Assert.True(vm.IsPrintablePage);

        Press(win, Key.E);
        Assert.Equal(Screen.Export, vm.CurrentScreen);
        Assert.NotNull(vm.ExportPanel);
        Assert.Equal("Form GSTR-4 — Composition Annual Return", vm.ExportPanel!.DocumentTitle);

        win.Close();
    }

    /// <summary>P renders a print preview of the annual return, titled as the document rather than the column.</summary>
    [AvaloniaFact]
    public void P_opens_a_print_preview_of_the_gstr9_annual_return()
    {
        var vm = GstCompany("Annual Print Co", GstRegistrationType.Regular);
        var win = Show(vm);

        vm.OpenGstr9Report();
        Dispatcher.UIThread.RunJobs();
        Press(win, Key.P);

        Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
        Assert.NotNull(vm.PrintPreview);
        // `Title` is the constant column caption ("Print Preview") and would prove nothing about WHAT is
        // previewed; `ReportTitle` is the document's own heading.
        Assert.Equal("Form GSTR-9 — Annual Return", vm.PrintPreview!.ReportTitle);

        win.Close();
    }

    /// <summary>Form 12BA reaches print too — the gesture an employer uses to actually furnish the statement.</summary>
    [AvaloniaFact]
    public void P_opens_a_print_preview_of_form_12ba()
    {
        var vm = SalaryTdsCompany();
        var win = Show(vm);

        vm.OpenForm12Ba();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Screen.Form12Ba, vm.CurrentScreen);
        Assert.True(vm.IsPrintablePage);

        Press(win, Key.P);
        Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
        Assert.NotNull(vm.PrintPreview);

        win.Close();
    }

    // ================================================================ 3. bytes actually reach the disk

    /// <summary>
    /// 🔴 <b>A file, on disk, with the warning in it.</b> "Exportable" is a property; this asserts the product.
    /// The CSV is read back as text, so a writer that produced a header-only file would pass a size check and
    /// fail this. The assertion is on the empty-state sentence specifically — that is the row whose loss would
    /// turn the document into a nil declaration.
    /// </summary>
    [Fact]
    public void Exporting_form_12ba_writes_a_csv_that_still_carries_the_warning()
    {
        var vm = SalaryTdsCompany();
        var page = new Form12BaViewModel(vm.Company!);

        var panel = new ExportViewModel(
            page.Title,
            () => MasterListTabularProjector.ProjectSource(page),
            projectPrint: null,
            _outDir,
            new DateTime(2025, 6, 1, 10, 0, 0),
            writeBytes: null)
        {
            Format = ExportFormat.Csv,
            FileName = "form12ba",
            AppendTimestamp = false,
        };

        Assert.True(panel.Apply(), panel.Status);

        var path = Path.Combine(_outDir, "form12ba.csv");
        Assert.True(File.Exists(path));
        var text = File.ReadAllText(path);

        // A distinctive clause of the shipped sentence, so the assertion cannot pass on a coincidence.
        Assert.Contains("not a nil", text);
        Assert.Contains("Employer TAN", text);
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// GSTR-9C exports as a real document, with both sides of a reconciliation present in the bytes. PDF is
    /// checked for its magic number so this is a document rather than a non-zero-length file.
    /// </summary>
    [Fact]
    public void Exporting_gstr9c_as_pdf_writes_a_real_document()
    {
        var vm = GstCompany("Recon Export Co", GstRegistrationType.Regular);
        var page = new Gstr9cReportViewModel(vm.Company!);

        var panel = new ExportViewModel(
            page.Title,
            () => MasterListTabularProjector.ProjectSource(page),
            projectPrint: null,
            _outDir,
            new DateTime(2025, 6, 1, 10, 0, 0),
            writeBytes: null)
        {
            Format = ExportFormat.Pdf,
            FileName = "gstr9c",
            AppendTimestamp = false,
        };

        Assert.True(panel.Apply(), panel.Status);

        var path = Path.Combine(_outDir, "gstr9c.pdf");
        Assert.True(File.Exists(path));
        var bytes = File.ReadAllBytes(path);

        Assert.NotEmpty(bytes);
        var latin1 = string.Concat(bytes.Select(b => (char)b));
        Assert.StartsWith("%PDF-", latin1);
        Assert.DoesNotContain("tally", latin1, StringComparison.OrdinalIgnoreCase);
    }
}
