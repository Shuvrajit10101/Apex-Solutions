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
using Domain = Apex.Ledger.Domain;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Census row 6.26 — the Kerala Flood Cess must be REACHABLE, and the figures must actually REACH THE SCREEN.</b>
///
/// <para>🔴 <b>Why this class exists.</b> Before it, the Kerala Flood Cess engine and its return projection were
/// complete, cited and (partly) tested — and <c>grep -rn "KeralaFloodCess" src/</c> returned <b>zero callers</b>
/// outside the two engine files. No menu item, no screen, no keystroke. That is the exact shape this project has
/// filed twice already (<c>CostReports.BuildLedgerBreakup</c>, fully tested with zero <c>src/</c> callers, and
/// <c>MultiAccountPrintViewModel</c>, ~432 lines with zero references, filed as T2-40): a capability no user can
/// reach, which does not move a census row however good the engine behind it is.</para>
///
/// <para><b>Two kinds of test here, and the second is the load-bearing one.</b> The route tests drive the real
/// cascading menu and pin the gate. The <b>visual-tree</b> test opens the real <see cref="MainWindow"/> headlessly
/// and requires the computed cess to be present as realised, visible text — because a view model that computes the
/// right number while nothing in the XAML renders it is precisely the failure a view-model-flag assertion cannot
/// see, and the page template added for this row is new and unproven.</para>
/// </summary>
public sealed class KeralaFloodCessRouteTests
{
    private static readonly DateOnly FyStart = new(2019, 4, 1);
    private static readonly DateOnly SaleDate = new(2019, 8, 10);

    private const string PanCompany = "AAPFU0939F";

    private static string GstinFor(string stateCode, string pan)
    {
        var body = stateCode + pan + "1Z0";
        return body[..14] + Gstin.ComputeCheckDigit(body);
    }

    // ─────────────────────────────────────────────────────────────────── fixture

    /// <summary>
    /// Creates a company through the real shell and puts it in the state the gate reads: a GST registration in
    /// <paramref name="stateCode"/>, the given registration type, and books beginning on <paramref name="booksBegin"/>.
    /// </summary>
    private static MainWindowViewModel NewShell(
        string tempDir,
        string stateCode = "32",
        GstRegistrationType registrationType = GstRegistrationType.Regular,
        DateOnly? booksBegin = null)
    {
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        vm.NewCompanyName = "Kerala Flood Cess Co";
        vm.CreateCompany();

        var c = vm.Company!;
        c.FinancialYearStart = booksBegin ?? FyStart;
        c.BooksBeginFrom = booksBegin ?? FyStart;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = stateCode,
            Gstin = GstinFor(stateCode, PanCompany),
            RegistrationType = registrationType,
            CompositionSubType = registrationType == GstRegistrationType.Composition
                ? Domain.CompositionSubType.Trader
                : null,
            ApplicableFrom = booksBegin ?? FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        return vm;
    }

    /// <summary>Posts one intra-Kerala B2C sale of ₹1,00,000 at 18% inside the levy window — 1% cess ⇒ ₹1,000.00.</summary>
    private static void PostB2CSale(Company c)
    {
        var gst = new GstService(c);
        var sales = AddLedger(c, "Sales", "Sales Accounts", openingIsDebit: false);
        var consumer = AddLedger(c, "Walk-in Consumer", "Sundry Debtors", openingIsDebit: true);
        consumer.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Consumer,
            StateCode = "32",
        };

        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(1_00_000m), 1800) },
            interState: false, GstTaxDirection.Output);

        var lines = new List<EntryLine>
        {
            new(consumer.Id, Money.FromRupees(1_00_000m) + tax.TotalTax, DrCr.Debit),
            new(sales.Id, Money.FromRupees(1_00_000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);

        new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id,
            SaleDate, lines, partyId: consumer.Id));
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "ApexKfc_" + Guid.NewGuid().ToString("N"));

    private static void Cleanup(string dir)
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>The labels of the Reports → Statutory Reports column, reached through the real cascade.</summary>
    private static List<string> StatutoryReportLabels(MainWindowViewModel vm)
    {
        vm.ShowStatutoryReportsMenu();
        return vm.Menu.Select(m => m.Label).ToList();
    }

    /// <summary>Walks the ACTIVE cascade column with Down until the highlighted row is <paramref name="label"/> —
    /// the real keyboard gesture, so "reachable" means reachable by arrows, not by calling an internal method.</summary>
    private static void SelectActiveItem(MainWindowViewModel vm, string label)
    {
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            if (vm.Menu[vm.SelectedIndex].Label == label) return;
            vm.MoveDown();
        }
        Assert.Fail($"menu item '{label}' was not reachable by arrow navigation from the active column "
                    + $"(it holds: {string.Join(" · ", vm.Menu.Select(m => m.Label))})");
    }

    /// <summary>
    /// 🔴 Opens the return the way an operator does: from the <b>Gateway root</b>, arrow down to <b>Statutory
    /// Reports</b> (which sits under the root column's <b>Reports</b> section header — "Reports" is a header, not a
    /// drillable row, so the cascade is two drills deep, not three), Enter, then arrow to <b>Kerala Flood Cess</b>
    /// and Enter. Nothing here calls <c>OpenKeralaFloodCessReturn</c>, so a route that exists only as a public
    /// method with no menu path fails at the arrow walk.
    /// </summary>
    private static void OpenViaCascade(MainWindowViewModel vm)
    {
        vm.ShowGateway();

        // The row lives under the Reports SECTION, never in a flat dump — pin that here, since the arrow walk
        // below would be just as happy with a top-level item.
        AssertNestedUnderReportsSection(vm, "Statutory Reports");

        SelectActiveItem(vm, "Statutory Reports"); vm.DrillIn();
        SelectActiveItem(vm, "Kerala Flood Cess"); vm.DrillIn();
    }

    /// <summary>
    /// Asserts that <paramref name="label"/> appears in the root column <b>after</b> the "Reports" section header
    /// and before the next header — i.e. it is nested under Reports, which is the professional-hierarchy rule this
    /// project holds every screen to.
    /// </summary>
    private static void AssertNestedUnderReportsSection(MainWindowViewModel vm, string label)
    {
        var reportsHeader = vm.Menu.ToList().FindIndex(m => m.IsHeader && m.Label == "Reports");
        Assert.True(reportsHeader >= 0, "the Gateway root has no 'Reports' section header.");

        var target = vm.Menu.ToList().FindIndex(m => m.Label == label);
        Assert.True(target > reportsHeader, $"'{label}' is not under the Reports section.");

        var nextHeader = vm.Menu.ToList().FindIndex(reportsHeader + 1, m => m.IsHeader);
        if (nextHeader >= 0)
            Assert.True(target < nextHeader,
                $"'{label}' falls after the Reports section ended — it is not nested under it.");
    }

    // ─────────────────────────────────────────────────────────── the route, and its gate

    /// <summary>
    /// 🔴 <b>THE REACHABILITY TEST — this one fails on today's main by construction</b> (there is no such menu item
    /// anywhere in the tree). A Kerala GST registrant finds <b>Kerala Flood Cess</b> nested under
    /// Reports → Statutory Reports — never a flat dump, never a hidden hotkey.
    /// </summary>
    [Fact]
    public void A_Kerala_company_reaches_the_Kerala_Flood_Cess_return_from_Reports_Statutory_Reports()
    {
        var dir = TempDir();
        try
        {
            var vm = NewShell(dir);
            Assert.Contains("Kerala Flood Cess", StatutoryReportLabels(vm));
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// A company registered outside Kerala never sees the item ([KFC-FAQ] Q11/Q21 — a Kerala State levy is collected
    /// by Kerala registrants). Without this clause the row would be a flat dump of an irrelevant State's levy into
    /// every book in the product.
    /// </summary>
    [Fact]
    public void A_company_registered_outside_Kerala_never_sees_the_item()
    {
        var dir = TempDir();
        try
        {
            var vm = NewShell(dir, stateCode: "27");   // Maharashtra
            Assert.DoesNotContain("Kerala Flood Cess", StatutoryReportLabels(vm));
        }
        finally { Cleanup(dir); }
    }

    /// <summary>[KFC-FAQ] Q14 — "Composition tax payers are exempted from the levy of Kerala Flood Cess." A Kerala
    /// composition dealer has no return to file, so it has no menu item either.</summary>
    [Fact]
    public void A_Kerala_composition_dealer_never_sees_the_item()
    {
        var dir = TempDir();
        try
        {
            var vm = NewShell(dir, registrationType: GstRegistrationType.Composition);
            Assert.DoesNotContain("Kerala Flood Cess", StatutoryReportLabels(vm));
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// 🔴 <b>THE DEAD-MENU-ITEM LOCK.</b> A Kerala company whose books begin AFTER the levy lapsed cannot hold a
    /// single leviable supply, so the report would open empty for ever. A menu item that always opens an
    /// always-empty report is a dead capability wearing a feature's clothes, so it must be absent — and this is the
    /// clause a "just gate it on Kerala" simplification would drop.
    /// </summary>
    [Fact]
    public void A_Kerala_company_whose_books_begin_after_the_lapse_never_sees_the_item()
    {
        var dir = TempDir();
        try
        {
            var vm = NewShell(dir, booksBegin: new DateOnly(2021, 8, 1));   // one day after cessation
            Assert.DoesNotContain("Kerala Flood Cess", StatutoryReportLabels(vm));
        }
        finally { Cleanup(dir); }
    }

    /// <summary>The gate is on the OPEN PATH too, not only on the menu — so no hotkey, test or future caller can
    /// reach a screen the menu correctly hides (ER-13).</summary>
    [Fact]
    public void The_open_path_is_a_no_op_for_a_company_that_could_never_file()
    {
        var dir = TempDir();
        try
        {
            var vm = NewShell(dir, stateCode: "27");
            vm.OpenKeralaFloodCessReturn();

            Assert.Null(vm.KeralaFloodCessPage);
            Assert.NotEqual(Screen.KeralaFloodCessReturn, vm.CurrentScreen);
            Assert.False(vm.IsKeralaFloodCessScreen);
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// 🔴 <b>THE FULL KEYBOARD ROUTE, FROM THE GATEWAY ROOT.</b> Arrowing Reports → Statutory Reports → Kerala Flood
    /// Cess and pressing Enter at each step opens the page as the rightmost cascading column. This is what "the row
    /// is reachable" has to mean: no test here calls <c>OpenKeralaFloodCessReturn</c> to get there.
    /// </summary>
    [Fact]
    public void The_return_opens_as_a_page_column_by_arrow_navigation_from_the_Gateway_root()
    {
        var dir = TempDir();
        try
        {
            var vm = NewShell(dir);
            OpenViaCascade(vm);

            Assert.Equal(Screen.KeralaFloodCessReturn, vm.CurrentScreen);
            Assert.NotNull(vm.KeralaFloodCessPage);
            Assert.True(vm.IsKeralaFloodCessScreen);
            // The page really is the rightmost column of the cascade, with its own title.
            Assert.Equal("Kerala Flood Cess", vm.Columns[^1].Title);
        }
        finally { Cleanup(dir); }
    }

    // ─────────────────────────────────────────────────────────── the screen's own honesty

    /// <summary>
    /// 🔴 <b>THE CLOSED-WINDOW LOCK ON THE PICKER.</b> The screen offers <b>only</b> the return periods the levy
    /// actually had — twenty-four months, Aug 2019 through Jul 2021. A free-form period box would let an operator
    /// ask for a month outside the levy and read a zero back, which on screen is indistinguishable from "we
    /// computed it and it was nil".
    /// </summary>
    [Fact]
    public void The_screen_offers_only_return_periods_inside_the_levy_window()
    {
        var dir = TempDir();
        try
        {
            var vm = NewShell(dir);
            vm.OpenKeralaFloodCessReturn();
            var page = vm.KeralaFloodCessPage!;

            Assert.Equal(24, page.Periods.Count);
            Assert.Equal(new DateOnly(2019, 8, 1), page.Periods[0].FirstDay);
            Assert.Equal(new DateOnly(2021, 7, 1), page.Periods[^1].FirstDay);
            Assert.Equal(new DateOnly(2021, 7, 31), page.Periods[^1].LastDay);

            // Every offered period lies wholly inside the levy — no month may straddle out of it.
            Assert.All(page.Periods, p =>
            {
                Assert.True(KeralaFloodCess.IsInForceOn(p.FirstDay), $"{p.Label} starts outside the levy.");
                Assert.True(KeralaFloodCess.IsInForceOn(p.LastDay), $"{p.Label} ends outside the levy.");
            });
        }
        finally { Cleanup(dir); }
    }

    /// <summary>[KFC-FAQ] Q7 — the cess follows GSTR-3B's due date, the 20th of the succeeding month.</summary>
    [Fact]
    public void The_remittance_due_date_is_the_twentieth_of_the_succeeding_month()
    {
        var dir = TempDir();
        try
        {
            var vm = NewShell(dir);
            vm.OpenKeralaFloodCessReturn();
            var page = vm.KeralaFloodCessPage!;

            Assert.Equal(new DateOnly(2019, 9, 20), page.Periods[0].DueDate);      // Aug 2019 ⇒ 20-Sep-2019
            Assert.Equal(new DateOnly(2021, 8, 20), page.Periods[^1].DueDate);     // Jul 2021 ⇒ 20-Aug-2021
            Assert.Equal("20-Sep-2019", page.DueDateText);
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// 🔴 <b>THE HONESTY LOCK.</b> Two things this screen must state <b>in words on its own face</b>, because both
    /// are judgements a filer has to be able to check: that the levy has lapsed, and that supplies to a
    /// Kerala-registered buyer were treated as made in furtherance of business ([KFC-FAQ] Q12) even though the book
    /// records no such flag. Deleting either caption to "clean up the screen" fails here.
    /// </summary>
    [Fact]
    public void The_screen_states_the_lapse_and_the_furtherance_of_business_reading()
    {
        var dir = TempDir();
        try
        {
            var vm = NewShell(dir);
            vm.OpenKeralaFloodCessReturn();
            var page = vm.KeralaFloodCessPage!;

            Assert.Contains("lapsed on 31-Jul-2021", page.LapseText);
            Assert.Contains("S.R.O. 436/2019", page.LapseText);
            Assert.Contains("furtherance of business", page.FurtheranceNote);
            Assert.Contains("Q12", page.FurtheranceNote);
        }
        finally { Cleanup(dir); }
    }

    /// <summary>A nil return STATES its nil — the footings render "0.00", never a blank that reads as "not
    /// computed". (<c>IndianFormat.Amount</c> renders a zero as blank; the screen must not use it for a footing.)</summary>
    [Fact]
    public void A_nil_return_states_its_zero_rather_than_going_blank()
    {
        var dir = TempDir();
        try
        {
            var vm = NewShell(dir);
            vm.OpenKeralaFloodCessReturn();
            var page = vm.KeralaFloodCessPage!;

            Assert.True(page.IsEmpty);
            Assert.Equal("0.00", page.TotalTurnoverText);
            Assert.Equal("0.00", page.TotalCessText);
            Assert.Contains("nil return", page.StatusText);
        }
        finally { Cleanup(dir); }
    }

    /// <summary>Ctrl+A on the screen writes the return CSV; the export folder seam is the shared one, so it never
    /// collapses to a bare file name on a runner with no Documents folder (W2-03).</summary>
    [Fact]
    public void Ctrl_A_exports_the_return_csv_and_names_the_file_it_wrote()
    {
        var dir = TempDir();
        try
        {
            var vm = NewShell(dir);
            PostB2CSale(vm.Company!);
            vm.OpenKeralaFloodCessReturn();

            var exportDir = Path.Combine(dir, "exports");
            vm.KeralaFloodCessPage!.ExportFolder = exportDir;
            vm.ExportKeralaFloodCessReturn();

            var written = Path.Combine(exportDir, "KeralaFloodCess-2019-08.csv");
            Assert.True(File.Exists(written), $"expected the return CSV at {written}; status was '{vm.KeralaFloodCessPage.ExportStatus}'.");
            Assert.Contains(written, vm.KeralaFloodCessPage.ExportStatus);

            var csv = File.ReadAllText(written);
            Assert.Contains("1,000.00", csv);            // the cess actually reaches the file
            Assert.Contains("lapsed on 31-Jul-2021", csv);   // and so does the honesty line
        }
        finally { Cleanup(dir); }
    }

    /// <summary>Alt+B saves the return and pops back out of the page column, the same save-and-return every other
    /// statutory report screen offers.</summary>
    [Fact]
    public void Alt_B_saves_the_return_and_returns_to_the_menu()
    {
        var dir = TempDir();
        try
        {
            var vm = NewShell(dir);
            PostB2CSale(vm.Company!);
            vm.OpenKeralaFloodCessReturn();
            vm.KeralaFloodCessPage!.ExportFolder = Path.Combine(dir, "exports");

            vm.SaveReturnKeralaFloodCess();

            Assert.NotEqual(Screen.KeralaFloodCessReturn, vm.CurrentScreen);
            Assert.False(vm.IsKeralaFloodCessScreen);
            Assert.True(File.Exists(Path.Combine(dir, "exports", "KeralaFloodCess-2019-08.csv")));
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// 🔴 <b>THE CULTURE LOCK — a defect found in this screen's own first draft.</b> The return's dates and its
    /// export filename were originally written with plain interpolation (<c>$"{period.FirstDay:dd-MMM-yyyy}"</c>,
    /// <c>$"…-{period.FirstDay:yyyy-MM}.csv"</c>), which formats in the <b>current culture</b>. Under a culture whose
    /// default calendar is not Gregorian — <c>ar-SA</c> uses Umm al-Qura — that changes not just the month
    /// abbreviation but <b>the year and month numbers</b>, so the exported return would be filed under a
    /// completely different month's name. The gate runs on ubuntu and macOS as well as Windows.
    ///
    /// <para><b>This test is written so it cannot pass vacuously.</b> It first proves the hostile culture really
    /// does format the date differently from the invariant culture on this runner; if it does not, the lock proves
    /// nothing and says so loudly rather than going green — that is the exact shape of the already-filed defect
    /// where an assertion passed by comparing "" to "".</para>
    /// </summary>
    [Fact]
    public void The_return_dates_and_export_filename_do_not_move_with_the_current_culture()
    {
        var hostile = new System.Globalization.CultureInfo("ar-SA");
        var probe = new DateOnly(2019, 8, 1);

        Assert.True(
            probe.ToString("yyyy-MM", hostile) != "2019-08",
            "this runner does not distinguish the ar-SA calendar from the invariant one, so this culture lock "
            + "would pass without exercising anything. Treat it as un-run, not as green.");

        var dir = TempDir();
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            var vm = NewShell(dir);
            PostB2CSale(vm.Company!);

            System.Globalization.CultureInfo.CurrentCulture = hostile;

            vm.OpenKeralaFloodCessReturn();
            var page = vm.KeralaFloodCessPage!;
            page.ExportFolder = Path.Combine(dir, "exports");
            page.Rebuild();          // re-render every date under the hostile culture
            page.ExportReturn();

            Assert.Equal("20-Sep-2019", page.DueDateText);
            Assert.Equal("01-Aug-2019 to 31-Aug-2019", page.PeriodText);
            Assert.True(
                File.Exists(Path.Combine(dir, "exports", "KeralaFloodCess-2019-08.csv")),
                $"the return was exported under a culture-dependent file name; status was '{page.ExportStatus}'.");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
            Cleanup(dir);
        }
    }

    // ─────────────────────────────────────────────────────────── the realised visual tree

    /// <summary>
    /// 🔴 <b>THE TEST THAT WOULD HAVE CAUGHT THE WHOLE ROW BEING DEAD, AND THE ONE A VIEW-MODEL ASSERTION CANNOT
    /// SUBSTITUTE FOR.</b> Every assertion above this line is satisfied by a view model that computes the right
    /// figures while <b>nothing on screen shows them</b> — which is exactly the state this row was found in (a
    /// complete engine with zero callers), and exactly what a brand-new page template can silently reproduce if its
    /// binding scope is wrong.
    ///
    /// <para>So this opens the real <see cref="MainWindow"/> headlessly, navigates to the return, and requires the
    /// computed cess — <b>₹1,000.00</b> on a ₹1,00,000 intra-Kerala B2C sale at 18% — to be present as realised,
    /// visible, laid-out text in the window's own visual tree. Headless-safe: visual-tree and layout-bounds
    /// inspection only, no Skia and no rendered frame.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_computed_cess_is_visible_on_the_realised_screen()
    {
        var dir = TempDir();
        MainWindow? window = null;
        try
        {
            var vm = NewShell(dir);
            PostB2CSale(vm.Company!);

            window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
            window.Show();

            OpenViaCascade(vm);

            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var visibleText = Descendants(window)
                .OfType<TextBlock>()
                .Where(t => t.IsEffectivelyVisible && t.Bounds.Width > 0 && t.Bounds.Height > 0)
                .Select(t => t.Text ?? string.Empty)
                .ToList();

            Assert.True(
                visibleText.Any(t => t.Contains("1,000.00", StringComparison.Ordinal)),
                "the Kerala Flood Cess of ₹1,000.00 on a ₹1,00,000 intra-Kerala B2C sale at 18% is computed by the "
                + "view model but does not appear anywhere in the realised visual tree — the page template is not "
                + "rendering the return. This is the dead-capability shape: an engine and a view model that are both "
                + "correct behind a screen that shows nothing.");

            // …and the screen names itself, so the figure above is not floating in some other page's grid.
            Assert.True(
                visibleText.Any(t => t.Contains("Kerala Flood Cess", StringComparison.Ordinal)),
                "the Kerala Flood Cess page rendered no title — the figure asserted above may belong to another screen.");

            // The covered date range is rendered too — the last period is CLIPPED at the cessation date, so an
            // operator who cannot see the range cannot tell which days the figures cover.
            Assert.True(
                visibleText.Any(t => t.Contains("01-Aug-2019 to 31-Aug-2019", StringComparison.Ordinal)),
                "the return's covered period is computed but never rendered — PeriodText has no binding, which "
                + "makes it a view-model property whose only consumer is a test.");

            // The lapse must be on the operator's screen, not only in the view model.
            Assert.True(
                visibleText.Any(t => t.Contains("lapsed on 31-Jul-2021", StringComparison.Ordinal)),
                "the screen does not tell the operator the levy has lapsed — the honesty caption is not rendered.");
        }
        finally
        {
            window?.Close();
            Cleanup(dir);
        }
    }

    /// <summary>
    /// 🔴 <b>A COMPUTED NIL MUST BE WORDED ON SCREEN, NOT AN EMPTY BOX.</b> On a Kerala book with no leviable
    /// supply the return is nil — and an empty table is indistinguishable from a table that failed to populate.
    /// This drives the real window to a period with nothing in it and requires the worded empty state to be
    /// realised and visible, and the ₹0.00 footings with it.
    /// </summary>
    [AvaloniaFact]
    public void A_nil_return_renders_a_worded_empty_state_and_not_a_blank_table()
    {
        var dir = TempDir();
        MainWindow? window = null;
        try
        {
            var vm = NewShell(dir);          // a Kerala book with NO sales at all
            window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
            window.Show();

            OpenViaCascade(vm);

            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var visibleText = Descendants(window)
                .OfType<TextBlock>()
                .Where(t => t.IsEffectivelyVisible && t.Bounds.Width > 0 && t.Bounds.Height > 0)
                .Select(t => t.Text ?? string.Empty)
                .ToList();

            // 🔴 The asserted phrase is deliberately one the STATUS line does not also contain. The first draft of
            // this test looked for "No outward supply leviable", which is a substring of StatusText — so it passed
            // with the empty-state TextBlock's IsVisible hard-wired to False. Mutation caught it; the phrase below
            // exists only in the in-table empty state.
            Assert.True(
                visibleText.Any(t => t.Contains("No rate slabs", StringComparison.Ordinal)),
                "a nil Kerala Flood Cess return rendered an empty table with no worded empty state — an operator "
                + "cannot tell a computed nil from a report that failed to populate.");

            Assert.True(
                visibleText.Any(t => t.Contains("0.00", StringComparison.Ordinal)),
                "the nil return's footings did not render — a blank footing reads as 'not computed'.");
        }
        finally
        {
            window?.Close();
            Cleanup(dir);
        }
    }

    /// <summary>Every visual in the window's tree, depth-first.</summary>
    private static IEnumerable<Visual> Descendants(Visual root)
    {
        foreach (var child in root.GetVisualChildren())
        {
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }
}
