using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>W-BJ2 — THE REST OF THE FINANCIAL-YEAR-COMBOBOX FAMILY, RE-HOMED (census 11.16 / 12.1, cluster C1).</b>
///
/// <para><b>What the cluster is, measured rather than estimated.</b> Grepping <c>GstAdvFyOption</c> — the
/// financial-year option type these pages put in place of the vendor's period control — over
/// <c>src/Apex.Desktop/ViewModels/</c> returns <b>nine</b> files: the option type's own definition, <b>seven
/// report pages</b>, and <c>RunSetOffViewModel</c>, which is an ACTION page that posts a set-off and is not a
/// report at all. Of the seven report pages:
/// <list type="bullet">
///   <item><b>2 were re-homed earlier in this branch</b> — GSTR-9 and GSTR-9C (W-BJ).</item>
///   <item><b>3 are re-homed here</b> — ITC Set-Off, QRMP / IFF, Electronic Ledgers.</item>
///   <item><b>2 are deliberately NOT, and the reason is recorded in the enum beside the three that are</b> —
///   GST Amendments carries a multi-registration picker this surface has no equivalent for (re-homing it would
///   trade a period control for the loss of registration scoping, which on a two-GSTIN book is a wrong-money
///   document), and GST Offline Returns WRITES files and is not a read-only report.</item>
/// </list>
/// <b>So: 5 of the 7, with both exclusions named.</b> That is the count this file proves, and it is a count of
/// the family it measured — <b>not</b> of the whole cluster C1, which is larger and still open (the TDS/TCS
/// certificate screens, BRS, GSTR-4, CMP-08, GSTR-6, the snapshot-scoped ITC screens and Forex all remain on
/// dedicated page Screens).</para>
///
/// <para>🔴 <b>GROUNDING, AND IT IS THE GENERAL CONTRACT — SAID THAT WAY ON PURPOSE.</b> Opened by content on
/// 2026-10-20, <c>help.tallysolutions.com/tally-prime/keyboard-shortcuts/keyboard-shortcuts-tally-prime/</c>
/// gives <c>F2</c> as <i>"To change the date of voucher entry or date/period for reports"</i>. <b>No vendor page
/// we have opened names the period gesture on these three reports specifically</b> — unlike GSTR-9 and GSTR-9C,
/// which do — so the claim is that a REPORT carries the period control generally, and a dedicated page Screen,
/// which nulls the shell's report context, cannot. The same vendor page gives <c>Alt+F2</c> as <i>"To change the
/// period of the company"</i>, so our report-scoped Alt+F2 is a pre-existing divergence; this wave neither
/// relies on it being attested nor changes it.</para>
///
/// <para>🔴 <b>THE PERIOD IS NOT DECORATION, AND THAT IS THE ASSERTION THAT MATTERS MOST HERE.</b> A re-home
/// that hands a report a period control which does nothing is the inert-control defect this project has shipped
/// before (an Alt+P that was inert; two fields with 29 occurrences in <c>src/</c> and zero assignments). So
/// <see cref="Alt_F2_really_reprojects_each_rehomed_page_and_does_not_decorate_it"/> changes the period and
/// requires the FIGURES to change, and
/// <see cref="A_period_outside_the_pages_own_year_list_is_refused_out_loud_not_silently_substituted"/> requires
/// that a period the page cannot project is REFUSED BY NAME rather than silently answered with another year's
/// figures under the chosen year's heading.</para>
/// </summary>
public sealed class RehomedReportSurfaceBj2Tests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly SaleDate = new(2024, 4, 5);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public RehomedReportSurfaceBj2Tests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexRehomedBj2_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>The three kinds this wave re-homed, each with the opener an operator's menu row calls.</summary>
    public static IEnumerable<object[]> RehomedKinds() => new[]
    {
        new object[] { ReportKind.ItcSetOffProjection },
        new object[] { ReportKind.QrmpIffCadence },
        new object[] { ReportKind.ElectronicLedgers },
    };

    // ================================================================= the route

    /// <summary>
    /// 🔴 <b>THE CASE THAT REDDENS ON MAIN.</b> It names only <see cref="Screen.Report"/> and the three shipped
    /// openers, so it compiles against <c>main</c> unchanged — and FAILS there, because each opener lands on its
    /// own dedicated page <c>Screen</c> and leaves <see cref="MainWindowViewModel.Reports"/> null. That null is
    /// the whole defect: it is what switches off F2, Alt+F2, F12, Ctrl+H saved views and the Enter-drill at once.
    /// </summary>
    [Fact]
    public void The_three_openers_land_on_the_shared_report_surface()
    {
        var vm = RegularGstFixture("Bj2 Route");

        foreach (var open in new Action[]
                 { vm.OpenItcSetOffReport, vm.OpenQrmpReport, vm.OpenElectronicLedgersReport })
        {
            open();
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.NotNull(vm.Reports);
            vm.Back();
        }
    }

    /// <summary>Each opener lands on its OWN kind — a re-home that routed two menu rows to one report would pass
    /// the test above and be a different report under the operator's chosen heading.</summary>
    [Fact]
    public void Each_opener_lands_on_its_own_kind()
    {
        var vm = RegularGstFixture("Bj2 Kinds");

        foreach (var (open, kind) in new (Action Open, ReportKind Kind)[]
                 {
                     (vm.OpenItcSetOffReport, ReportKind.ItcSetOffProjection),
                     (vm.OpenQrmpReport, ReportKind.QrmpIffCadence),
                     (vm.OpenElectronicLedgersReport, ReportKind.ElectronicLedgers),
                 })
        {
            open();
            Assert.Equal(kind, vm.Reports!.Kind);
            vm.Back();
        }
    }

    // ================================================================= cell-for-cell fidelity

    /// <summary>
    /// 🔴 <b>THE RE-HOMED REPORT IS THE SUPERSEDED PAGE, CELL FOR CELL.</b> A re-home that re-derives its rows
    /// can lose a figure between the engine and the grid, which is exactly the class of defect this wave's own
    /// blocking finding was. The builder reads the page's <c>ToMasterListSnapshot</c> — the same projection
    /// E / Alt+E already exported — so the two cannot disagree, and this asserts that they do not.
    /// </summary>
    [Theory]
    [MemberData(nameof(RehomedKinds))]
    public void Every_rehomed_bj2_report_is_the_superseded_pages_grid_cell_for_cell(ReportKind kind)
    {
        var vm = RegularGstFixture($"Bj2 Fidelity {kind}");
        var reports = new ReportsViewModel(vm.Company!, kind);
        var page = SupersededPage(kind, vm.Company!);

        Assert.Equal(page.Title, reports.Title);
        Assert.Equal(
            page.Columns.Select(c => c.Caption).ToList(),
            reports.PayrollColumns.Select(c => c.Header).ToList());
        Assert.Equal(
            page.Columns.Select(c => c.IsNumeric).ToList(),
            reports.PayrollColumns.Select(c => c.IsNumeric).ToList());

        Assert.Equal(page.Rows.Count, reports.PayrollRows.Count);
        for (var r = 0; r < page.Rows.Count; r++)
            Assert.Equal(
                page.Rows[r].ToList(),
                reports.PayrollRows[r].Cells.Select(c => c.Text).ToList());

        // …and it is not an empty shell that happens to match another empty shell.
        Assert.NotEmpty(reports.PayrollRows);
    }

    // ================================================================= the period is real

    /// <summary>
    /// 🔴 <b>Alt+F2 RE-PROJECTS, IT DOES NOT DECORATE.</b> The fixture posts its only sale in FY 2024-25, so the
    /// report must differ between that year and the year before it. Asserted on the CELLS, because a subtitle
    /// that names a year is exactly what an inert control looks like.
    /// </summary>
    [Theory]
    [MemberData(nameof(RehomedKinds))]
    public void Alt_F2_really_reprojects_each_rehomed_page_and_does_not_decorate_it(ReportKind kind)
    {
        var vm = RegularGstFixture($"Bj2 Period {kind}");
        var reports = new ReportsViewModel(vm.Company!, kind);

        var inYear = Cells(reports);
        Assert.Contains("01-Apr-2024", reports.Subtitle);
        Assert.NotEmpty(inYear);

        // The financial year BEFORE the one the voucher falls in — offered by every one of these pages (each
        // lists the company FY and the two prior), so the projection must actually move.
        reports.SetPeriod(FyStart.AddYears(-1), FyStart.AddDays(-1));

        Assert.Contains("01-Apr-2023", reports.Subtitle);
        Assert.Contains("31-Mar-2024", reports.Subtitle);
        Assert.NotEqual(inYear, Cells(reports));

        // …and back again, so the change is a re-projection and not a one-way clear.
        reports.SetPeriod(FyStart, FyStart.AddYears(1).AddDays(-1));
        Assert.Equal(inYear, Cells(reports));
    }

    /// <summary>
    /// 🔴 <b>A PERIOD THE PAGE CANNOT PROJECT IS REFUSED BY NAME.</b> Each of these reports is projected a
    /// financial year at a time and offers the company FY plus the two prior. The one thing the surface must
    /// never do with a period outside that list is fall back to the page's default year, because that puts one
    /// year's figures under another year's heading on a tax document. It says so instead, and names the years.
    /// </summary>
    [Theory]
    [MemberData(nameof(RehomedKinds))]
    public void A_period_outside_the_pages_own_year_list_is_refused_out_loud_not_silently_substituted(ReportKind kind)
    {
        var vm = RegularGstFixture($"Bj2 Outside {kind}");
        var reports = new ReportsViewModel(vm.Company!, kind);
        var inYear = Cells(reports);

        // Six years before the book's own financial year — well outside the three each page offers.
        var far = FyStart.AddYears(-6);
        reports.SetPeriod(far, far.AddYears(1).AddDays(-1));

        Assert.True(reports.IsPayrollEmpty,
            $"{kind} answered a period it cannot project with rows: {string.Join(" | ", Cells(reports).Take(4))}");
        Assert.NotEqual(inYear, Cells(reports));
        Assert.Contains("2018-19", reports.PayrollEmptyNote);     // the year that was asked for, named
        Assert.Contains("2024-25", reports.PayrollEmptyNote);     // and the years that are available
        Assert.Contains("Alt+F2", reports.PayrollEmptyNote);      // and the gesture that fixes it

        // The band still stands, so the operator sees a captioned, empty report rather than a blank pane.
        Assert.NotEmpty(reports.PayrollColumns);
        Assert.All(reports.PayrollColumns, c => Assert.False(string.IsNullOrWhiteSpace(c.Header)));
    }

    /// <summary>
    /// 🔴 <b>T2-151(1) — ON AN AGED BOOK EACH RE-HOMED REPORT OPENS ON ITS DEFAULT YEAR WITH FIGURES, NOT EMPTY.</b>
    /// The default window anchors on the LAST voucher's date; each page's own year list was bounded to the
    /// company FY and the two prior. With the FY starting 01-04-2024 and the only voucher on 09-09-2025 the
    /// default year is 2025-26, which no page offered — so all three opened with
    /// <i>"FY 2025-26 is not one it can project"</i> where the page they superseded opened with figures. Driven
    /// through each REAL opener, and compared cell for cell with the superseded page projected over 2025-26, so
    /// rows from the wrong year cannot pass.
    ///
    /// <para><b>Mutation that reddens it:</b> delete the <c>OfferTheDefaultAsOfYear(page)</c> call in
    /// <c>ReportsViewModel.BuildRehomedFyScopedGstPage</c> — the report is marked empty with the refusal.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(RehomedKinds))]
    public void An_aged_book_opens_each_rehomed_report_on_its_default_year_with_figures(ReportKind kind)
    {
        var vm = RegularGstFixture($"Bj2 Aged {kind}", saleDate: new DateOnly(2025, 9, 9));

        OpenerFor(vm, kind)();
        Assert.Equal(Screen.Report, vm.CurrentScreen);
        var reports = vm.Reports!;
        Assert.Equal(kind, reports.Kind);

        Assert.False(reports.IsPayrollEmpty, $"{kind} opened empty on an aged book: {reports.PayrollEmptyNote}");
        Assert.Contains("01-Apr-2025", reports.Subtitle);
        Assert.Contains("31-Mar-2026", reports.Subtitle);

        var page = SupersededPageFor(kind, vm.Company!, startYear: 2025);
        Assert.NotEmpty(page.Rows);
        Assert.Equal(page.Rows.SelectMany(r => r).ToList(), Cells(reports));
    }

    /// <summary>
    /// 🔴 <b>T2-151(2) — A PARTIAL WINDOW IS SNAPPED TO ITS WHOLE FINANCIAL YEAR ON EVERY RE-HOMED KIND.</b> Every
    /// <c>SetPeriod</c> elsewhere in this file is already a whole year, so mutation M3 (making
    /// <c>GstAnnualReturnWindow</c> honour the requested window verbatim — the shape that printed a year's figures
    /// under a four-day heading) reddened nothing here. A four-day window inside FY 2024-25 must state the whole
    /// year it projected and carry exactly the whole year's cells.
    /// </summary>
    [Theory]
    [MemberData(nameof(RehomedKinds))]
    public void A_partial_window_is_snapped_to_its_whole_financial_year_on_every_rehomed_kind(ReportKind kind)
    {
        var vm = RegularGstFixture($"Bj2 Partial {kind}");
        var reports = new ReportsViewModel(vm.Company!, kind);
        var wholeYear = Cells(reports);

        reports.SetPeriod(new DateOnly(2024, 4, 11), new DateOnly(2024, 4, 14));

        Assert.Contains("01-Apr-2024", reports.Subtitle);
        Assert.Contains("31-Mar-2025", reports.Subtitle);
        Assert.DoesNotContain("11-Apr-2024", reports.Subtitle);
        Assert.DoesNotContain("14-Apr-2024", reports.Subtitle);
        Assert.Equal(wholeYear, Cells(reports));
    }

    // ================================================================= the egress contract

    /// <summary>
    /// Adding a <see cref="ReportKind"/> member is not re-homing. A report that lands on a grid neither egress
    /// projector maps gains a BLANK exported header row and a BLANK printed header band — strictly worse than
    /// the dedicated Screen it replaced, and invisible to any test that only checks the enum member exists.
    /// </summary>
    [Theory]
    [MemberData(nameof(RehomedKinds))]
    public void Every_rehomed_bj2_kind_exports_and_prints_with_populated_column_captions(ReportKind kind)
    {
        var vm = RegularGstFixture($"Bj2 Egress {kind}");
        var reports = new ReportsViewModel(vm.Company!, kind);

        Assert.True(reports.IsWideMatrixReport,
            $"{kind} is not on the wide-matrix surface, so neither egress projector can see its columns.");

        var exported = ReportTabularProjector.Project(reports);
        var printed = ReportPrintProjector.Project(reports);

        Assert.All(exported.Columns, c => Assert.False(string.IsNullOrWhiteSpace(c.Header),
            $"{kind} exported a BLANK column caption."));
        Assert.All(printed.Columns, c => Assert.False(string.IsNullOrWhiteSpace(c.Header),
            $"{kind} printed a BLANK column caption band."));

        var egress = ReportMatrixEgressBand.For(reports).Select(c => c.Header).ToList();
        Assert.Equal(egress, exported.Columns.Select(c => c.Header).ToList());
        Assert.Equal(egress, printed.Columns.Select(c => c.Header).ToList());

        Assert.NotEmpty(exported.Rows);
        Assert.NotEmpty(printed.Rows);
    }

    /// <summary>The two grid-visibility traps, pinned. A matrix report leaves <c>Rows</c> empty, which is how the
    /// accounting grid and the shared empty-state overlay both decide what to draw; get either wrong and the
    /// report is CORRECT and UNREADABLE — two tables stacked, or an overlay over real figures.</summary>
    [Theory]
    [MemberData(nameof(RehomedKinds))]
    public void A_rehomed_bj2_report_shows_neither_the_accounting_grid_nor_the_empty_state_overlay(ReportKind kind)
    {
        var vm = RegularGstFixture($"Bj2 Grid {kind}");
        var reports = new ReportsViewModel(vm.Company!, kind);

        Assert.False(reports.IsAccountingReport);
        Assert.False(reports.IsEmpty);
        Assert.Empty(reports.Rows);
    }

    // ================================================================= the ER-13 gate, both doors

    /// <summary>
    /// 🔴 <b>THE SECOND DOOR — THE ONE THE PAY SHEET WAS MEASURED ESCAPING THROUGH.</b> A saved view stores a
    /// kind TOKEN and walks straight past the conditional menu row that would never have offered it. So each new
    /// kind needs its own <c>ReportFeatureGate</c> row, or a Composition dealer could recall a Regular
    /// taxpayer's GST Returns (Advanced) report from a view captured on another company.
    /// </summary>
    [Theory]
    [MemberData(nameof(RehomedKinds))]
    public void A_saved_view_cannot_open_a_rehomed_bj2_report_on_a_company_that_is_not_a_regular_dealer(ReportKind kind)
    {
        var vm = RegularGstFixture($"Bj2 Gate {kind}");

        OpenerFor(vm, kind)();
        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.NotEmpty(vm.Reports!.PayrollRows);        // captured off a POPULATED report
        var savedView = vm.Reports!.ToSavedView();
        vm.Back();

        // The same company re-registers as a Composition dealer: GST Returns (Advanced) is not its group at all.
        vm.Company!.Gst!.RegistrationType = GstRegistrationType.Composition;

        OpenerFor(vm, kind)();                           // the gated door: refuses, as it always did
        Assert.NotEqual(Screen.Report, vm.CurrentScreen);

        vm.ApplySavedView(savedView);                    // the second door: must refuse too
        Assert.NotEqual(Screen.Report, vm.CurrentScreen);
        Assert.Contains("switched off", vm.Message);

        // ---- and the gate is not a wall.
        vm.Company!.Gst!.RegistrationType = GstRegistrationType.Regular;
        vm.ApplySavedView(savedView);

        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.Equal(kind, vm.Reports!.Kind);
    }

    /// <summary>The persisted token round-trips. A saved view persists the STRING, so a kind missing from the
    /// token map throws <c>KeyNotFoundException</c> on the very gesture the re-home delivers.</summary>
    [Theory]
    [MemberData(nameof(RehomedKinds))]
    public void The_rehomed_bj2_kinds_round_trip_their_saved_view_token(ReportKind kind)
    {
        var token = ReportsViewModel.TokenFor(kind);
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal(kind, ReportsViewModel.KindFor(token));
    }

    // ================================================================= scaffolding

    private static Action OpenerFor(MainWindowViewModel vm, ReportKind kind) => kind switch
    {
        ReportKind.ItcSetOffProjection => vm.OpenItcSetOffReport,
        ReportKind.QrmpIffCadence => vm.OpenQrmpReport,
        ReportKind.ElectronicLedgers => vm.OpenElectronicLedgersReport,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a W-BJ2 re-homed kind"),
    };

    /// <summary>The superseded page's own grid snapshot — the page view model production no longer builds.</summary>
    private static MasterListSnapshot SupersededPage(ReportKind kind, Company company) => kind switch
    {
        ReportKind.ItcSetOffProjection => new ItcSetOffReportViewModel(company).ToMasterListSnapshot(),
        ReportKind.QrmpIffCadence => new QrmpReportViewModel(company).ToMasterListSnapshot(),
        ReportKind.ElectronicLedgers => new ElectronicLedgersReportViewModel(company).ToMasterListSnapshot(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a W-BJ2 re-homed kind"),
    };

    /// <summary>The superseded page projected over a chosen financial year (one its list may not have offered).</summary>
    private static MasterListSnapshot SupersededPageFor(ReportKind kind, Company company, int startYear)
    {
        IFyScopedGstReportPage page = kind switch
        {
            ReportKind.ItcSetOffProjection => new ItcSetOffReportViewModel(company),
            ReportKind.QrmpIffCadence => new QrmpReportViewModel(company),
            ReportKind.ElectronicLedgers => new ElectronicLedgersReportViewModel(company),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a W-BJ2 re-homed kind"),
        };
        page.SelectedYear = new GstAdvFyOption { StartYear = startYear };
        return page.ToMasterListSnapshot();
    }

    /// <summary>Every cell of the matrix, flattened — the comparison a figure change cannot hide from.</summary>
    private static List<string> Cells(ReportsViewModel reports) => reports.PayrollRows
        .SelectMany(r => r.Cells.Select(c => c.Text))
        .ToList();

    /// <summary>A Regular GST company that elected QRMP (Quarterly return / Monthly payment), with one
    /// intra-state B2B sale of ₹1,000 @ 18% posted inside FY 2024-25 — so every one of these three reports has
    /// real, non-zero figures in that year and different ones in the year before.</summary>
    private MainWindowViewModel RegularGstFixture(string name, DateOnly? saleDate = null)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name + " " + Guid.NewGuid().ToString("N")[..6];
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);

        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            // 🔴 QUARTERLY, AND THAT IS LOAD-BEARING: QRMP applies only to a Regular filer who elected
            // Quarterly (Return) / Monthly (Payment). On a Monthly filer GstQrmp.Applicable is false and the
            // QRMP report is a "Not applicable" line that reads the SAME in every financial year — so the
            // period test above would have had nothing to detect and would have passed for the wrong reason
            // on an inert control. It was measured failing on a Monthly fixture before this was changed.
            Periodicity = GstReturnPeriodicity.Quarterly,
        });

        var gst = new GstService(c);
        var ledgers = new LedgerService(c);

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtor = Add(c, "Local Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) }, false, GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(1180m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, saleDate ?? SaleDate, lines, number: 1, partyId: debtor.Id));

        _storage.Save(c);
        vm.ShowGateway();
        return vm;
    }

    private static DomainLedger Add(Company c, string name, string groupName, bool debit)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: debit);
        c.AddLedger(ledger);
        return ledger;
    }
}
