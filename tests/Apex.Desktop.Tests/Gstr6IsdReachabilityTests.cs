using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Census row 6.24 (Input Service Distributor) — the LAST mile, driven by the keyboard alone.</b>
///
/// <para>🔴 <b>WHY THIS FILE PRESSES KEYS AND WALKS THE REALISED VISUAL TREE.</b> This project has filed three
/// dead features — a correct engine with no way in, the largest ~625 lines whose only callers were tests. A test
/// that called <c>vm.OpenGstr6Report()</c> and asserted the property became non-null would stay green on the day
/// the menu row, the dispatch case or the DataTemplate went missing, and those three are what make a capability
/// reachable. So each test starts on the Gateway and walks Reports → Statutory Reports → GST Returns (Advanced) →
/// GSTR-6 (ISD) with ArrowDown and Enter through <see cref="MainWindow"/>'s own key handler.</para>
///
/// <para><b>The gate is tested in both directions</b>, because the GSTR-6 row is conditional: it appears only for
/// a company that actually holds an Input Service Distributor registration. GSTR-6 is filed by an ISD and by
/// nobody else (§39(4) of the CGST Act, <c>cbic-gst.gov.in</c>), so offering the row to everyone would be a false
/// affordance — and hiding it forever would make the row unreachable. Both failures are guarded here.</para>
///
/// <para>Headless-safe: visual-tree, text and enabled-state inspection only. Culture-invariant, and every path is
/// built with <see cref="Path.Combine"/>, so this behaves identically on the ubuntu and macos CI legs.</para>
/// </summary>
public sealed class Gstr6IsdReachabilityTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private const string Karnataka = "29";

    private static string GstinFor(string stateCode, string pan)
    {
        var body = stateCode + pan + "1Z";
        return body + Gstin.ComputeCheckDigit(body + "0");
    }

    // ---------------------------------------------------------------- scaffolding

    private static void Pump(Window w)
    {
        Dispatcher.UIThread.RunJobs();
        w.Measure(new Size(1280, 800));
        w.Arrange(new Rect(0, 0, 1280, 800));
        Dispatcher.UIThread.RunJobs();
    }

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static List<TextBlock> VisibleTextBlocks(Window w) =>
        Descendants(w)
            .OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.Bounds.Width > 0 && t.Bounds.Height > 0)
            .ToList();

    private static bool ScreenShows(Window w, string fragment) =>
        VisibleTextBlocks(w).Any(t => (t.Text ?? string.Empty).Contains(fragment, StringComparison.Ordinal));

    private static List<T> VisibleControls<T>(Window w) where T : Control =>
        Descendants(w).OfType<T>()
            .Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0)
            .ToList();

    private static void KeyboardInto(MainWindow w, MainWindowViewModel vm, string label)
    {
        var offered = vm.Menu.Where(i => i.IsSelectable).Select(i => i.Label).ToList();
        Assert.True(offered.Contains(label),
            $"The keyboard cascade never offered '{label}'. This column offers: {string.Join(" | ", offered)}. " +
            "A screen with no menu row is unreachable and does not move a census row.");

        for (var i = 0; i <= vm.Menu.Count; i++)
        {
            if (vm.SelectedIndex >= 0
                && vm.SelectedIndex < vm.Menu.Count
                && vm.Menu[vm.SelectedIndex].Label == label)
            {
                w.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Pump(w);
                return;
            }
            w.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(w);
        }

        Assert.Fail($"ArrowDown never landed the highlight on '{label}' within one lap of the column.");
    }

    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewWindow(string tag)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexT3_" + tag + "_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        return (window, vm, dir);
    }

    private static void Cleanup(MainWindow w, string dir)
    {
        w.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>A saved Regular-GST Karnataka company, optionally holding a head-office ISD registration beside
    /// its operating one and a Tamil Nadu branch — the CBIC ABC Ltd shape.</summary>
    private static void SeedCompany(MainWindowViewModel vm, string name, bool withIsd)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = Karnataka,
            Gstin = GstinFor(Karnataka, "AAPFU0939F"),
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        if (withIsd)
        {
            c.Gst!.AddRegistration(new GstRegistration(
                Guid.NewGuid(), "Head Office (ISD)", Karnataka, GstinFor(Karnataka, "AAACC1206D"),
                GstRegistrationType.InputServiceDistributor, FyStart));
            c.Gst!.AddRegistration(new GstRegistration(
                Guid.NewGuid(), "Tamil Nadu Registration", "33", GstinFor("33", "AABCC1206D"),
                GstRegistrationType.Regular, FyStart));
            c.Gst!.EnsureValid();
        }

        vm.ShowGateway();
    }

    // ================================================================ the row

    /// <summary>
    /// 🔴 THE ROW-6.24 TEST. Gateway → Reports → Statutory Reports → GST Returns (Advanced) → GSTR-6 (ISD), by
    /// ArrowDown and Enter alone, ending on a realised page carrying the form's own captions, its three pickers
    /// and the Rule 39(1)(b) footing line.
    /// </summary>
    [AvaloniaFact]
    public void Gstr6_is_reachable_from_the_gateway_by_keyboard_alone_once_an_isd_registration_exists()
    {
        var (w, vm, dir) = NewWindow("Gstr6Reach");
        try
        {
            SeedCompany(vm, "ISD Reach Co", withIsd: true);
            Pump(w);

            KeyboardInto(w, vm, "Statutory Reports");
            KeyboardInto(w, vm, "GST Returns (Advanced)");
            KeyboardInto(w, vm, "GSTR-6 (ISD)");

            Assert.Equal(Screen.Gstr6Report, vm.CurrentScreen);
            Assert.NotNull(vm.Gstr6Report);

            // The page is REALISED — these captions exist only in the DataTemplate, so a missing template fails here.
            Assert.True(ScreenShows(w, "Form GSTR-6"), "The GSTR-6 title is not on screen.");
            Assert.True(ScreenShows(w, "Input Service Distributor"), "The distributor block is not on screen.");
            Assert.True(ScreenShows(w, "Relevant period for the turnover ratio"),
                "The Rule 39 Explanation (i) relevant period is not shown, so the basis of the pro rata is invisible.");
            Assert.True(ScreenShows(w, "Distribution of input tax credit (Rule 39)"),
                "The distribution table heading is not on screen.");

            // 🔴 The Rule 39(1)(b) check must be VISIBLE, not derivable. A filer who cannot see received vs distributed
            // cannot see the one error that matters on this return.
            Assert.True(ScreenShows(w, "Total credit received"), "The credit received is not on screen.");
            Assert.True(ScreenShows(w, "Total distributed"), "The distributed total is not on screen.");
            Assert.True(ScreenShows(w, "Undistributed (received − distributed)"),
                "The undistributed difference is not on screen — Rule 39(1)(b) cannot be checked at a glance.");

            // The operator can pick a distributor, a year and a month.
            Assert.True(VisibleControls<ComboBox>(w).Count >= 3,
                "The distributor / financial-year / month pickers are not realised.");

            // The head office ISD is offered by name, and the page is showing it.
            var page = vm.Gstr6Report!;
            Assert.Single(page.IsdRegistrations);
            Assert.Equal("Head Office (ISD)", page.SelectedIsd!.Name);
            Assert.True(ScreenShows(w, "Head Office (ISD)"), "The selected distributor is not shown on the page.");

            // 🔴 THE LAYOUT FIX MUST NOT HAVE COST A FACT. The distribution grid originally gave the recipient's
            // State and its Rule 39(1)(g) eligibility fixed columns of their own, which starved the star Recipient
            // column to 34px (XamlLayoutInvariantTests caught it). They moved to the row's sub-line — so the
            // heading must still ANNOUNCE them, or a reader of a filed statement cannot tell an eligible row from
            // an ineligible one, nor see the State that Rule 39(1)(j) turns on.
            Assert.True(ScreenShows(w, "Recipient (State · eligibility · GSTIN below)"),
                "The distribution grid no longer tells the reader where the State and eligibility are.");

            // …and the row view-model still carries all three, joined, whatever the grid does with them.
            var row = new Apex.Desktop.ViewModels.Gstr6RowVm
            {
                Recipient = "Karnataka Registration", StateCode = "29",
                Eligibility = "Eligible", Gstin = "29AAACC1206D1ZM",
            };
            Assert.Contains("29", row.SubLine);
            Assert.Contains("Eligible", row.SubLine);
            Assert.Contains("29AAACC1206D1ZM", row.SubLine);
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// The gate, the other way round: a company with no ISD registration is not offered the row at all, and the
    /// open path is a no-op even if something calls it. ER-13 — the GST Returns (Advanced) column such a company
    /// sees is the one it saw before this slice.
    /// </summary>
    [AvaloniaFact]
    public void A_company_without_an_isd_registration_is_not_offered_the_gstr6_row()
    {
        var (w, vm, dir) = NewWindow("Gstr6Gate");
        try
        {
            SeedCompany(vm, "No ISD Co", withIsd: false);
            Pump(w);

            KeyboardInto(w, vm, "Statutory Reports");
            KeyboardInto(w, vm, "GST Returns (Advanced)");

            var offered = vm.Menu.Where(i => i.IsSelectable).Select(i => i.Label).ToList();
            Assert.DoesNotContain("GSTR-6 (ISD)", offered);

            // The sibling rows are all still there — the column was not otherwise disturbed.
            Assert.Contains("Electronic Ledgers", offered);
            Assert.Contains("ITC Set-Off", offered);
            Assert.Contains("Offline Return Files (JSON)", offered);

            vm.OpenGstr6Report();
            Pump(w);
            Assert.Null(vm.Gstr6Report);
            Assert.NotEqual(Screen.Gstr6Report, vm.CurrentScreen);
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// The registration type has to be creatable, or the gate above can never open. The master's picker must offer
    /// "Input Service Distributor", and choosing it must produce a registration of that type — after which the
    /// GSTR-6 row appears in the menu that did not have it a moment earlier.
    /// </summary>
    [AvaloniaFact]
    public void Creating_an_isd_registration_through_the_master_makes_the_gstr6_row_appear()
    {
        var (w, vm, dir) = NewWindow("Gstr6Create");
        try
        {
            SeedCompany(vm, "ISD Create Co", withIsd: false);
            Pump(w);

            KeyboardInto(w, vm, "Create");
            KeyboardInto(w, vm, "GST Registration");

            var master = vm.GstRegistrationsMaster!;
            Assert.Contains("Input Service Distributor", master.RegistrationTypeOptions);

            // An ISD in the company's OWN State — CBIC's ABC Ltd example is exactly that, a Bangalore corporate
            // office acting as ISD beside a Bangalore business location. Census row 6.23's set rule used to refuse
            // this shape outright; the relaxation is narrow, and this is the screen that depends on it.
            master.SelectedState = master.StateOptions.Single(s => s.StartsWith(Karnataka, StringComparison.Ordinal));
            master.Gstin = GstinFor(Karnataka, "AAACC1206D");
            master.Name = "Head Office (ISD)";
            master.SelectedRegistrationType = "Input Service Distributor";
            Assert.True(master.Create(), $"Create failed: {master.Message}");
            Pump(w);

            var added = Assert.Single(vm.Company!.Gst!.AdditionalRegistrations);
            Assert.Equal(GstRegistrationType.InputServiceDistributor, added.RegistrationType);
            Assert.True(vm.Company!.Gst!.HasIsdRegistration);

            // …and the row is now in the menu it was absent from.
            vm.ShowGateway();
            Pump(w);
            KeyboardInto(w, vm, "Statutory Reports");
            KeyboardInto(w, vm, "GST Returns (Advanced)");
            Assert.Contains("GSTR-6 (ISD)", vm.Menu.Where(i => i.IsSelectable).Select(i => i.Label));
        }
        finally { Cleanup(w, dir); }
    }

    // ================================================================ the filed document

    /// <summary>
    /// 🔴 <b>THE ROW IS NOT DONE WHEN THE SCREEN RENDERS — GSTR-6 IS A RETURN THAT GETS FILED.</b> Reaching a page
    /// that shows the distribution is not the same as being able to submit it, and this project has repeatedly
    /// shipped a capability whose only caller was a test. This drives the whole route from the Gateway by keyboard
    /// and then presses <b>Ctrl+A</b> — the app's own primary-action chord — and asserts that a real GSTR-6 file
    /// lands on disk with the right name and the right contents.
    ///
    /// <para>The file name is asserted to carry the <b>ISD's</b> GSTIN, not the company's operating GSTIN. GSTR-6
    /// is filed by the distributor registration, so a file named for the company would be the right figures
    /// submitted under the wrong registration — and every other writer on the offline-returns page legitimately
    /// reaches for <c>company.Gst.Gstin</c>, which is exactly the habit that would produce that bug here.</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_A_on_the_gstr6_screen_writes_the_isd_return_file()
    {
        var (w, vm, dir) = NewWindow("Gstr6Export");
        try
        {
            SeedCompany(vm, "ISD Export Co", withIsd: true);
            Pump(w);

            KeyboardInto(w, vm, "Statutory Reports");
            KeyboardInto(w, vm, "GST Returns (Advanced)");
            KeyboardInto(w, vm, "GSTR-6 (ISD)");

            Assert.Equal(Screen.Gstr6Report, vm.CurrentScreen);
            var page = Assert.IsType<Gstr6ReportViewModel>(vm.Gstr6Report);

            var outDir = Path.Combine(dir, "export");
            Directory.CreateDirectory(outDir);
            page.ExportFolder = outDir;

            // The app's own primary-action chord, through MainWindow's key handler — not a direct method call, so a
            // regression that drops the dispatch case fails here rather than silently.
            w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(w);

            var written = Directory.GetFiles(outDir, "*.json");
            var path = Assert.Single(written);

            var isd = vm.Company!.Gst!.IsdRegistrations.Single();
            var name = Path.GetFileName(path);
            Assert.Equal(page.ExportFileName, name);
            Assert.StartsWith($"GSTR-6_{isd.Gstin}_", name, StringComparison.Ordinal);
            Assert.EndsWith(".json", name, StringComparison.Ordinal);
            // MMYYYY, so a filer can tell two months apart at a glance.
            Assert.Equal(6, name[$"GSTR-6_{isd.Gstin}_".Length..^".json".Length].Length);
            Assert.NotEqual(vm.Company!.Gst!.Gstin, isd.Gstin); // the two really are different registrations

            var text = File.ReadAllText(path);
            Assert.Contains("\"gstin\": \"" + isd.Gstin + "\"", text, StringComparison.Ordinal);
            // Tables 5 and 8 — the GSTN offline utility captures the distribution across both, so the key names
            // both. (This was "tbl8_distribution"; the receipts key alongside it was "tbl4_received_*", which was
            // the WRONG table — credit received is Table 3. See Gstr6IsdJsonTests for the sourcing.)
            Assert.Contains("tbl5_8_distribution", text, StringComparison.Ordinal);
            Assert.Contains("tbl3_received_camt_paisa", text, StringComparison.Ordinal);
            // Rule 39(1)(b) travels with the file, not only with the screen.
            Assert.Contains("undistributed_credit_paisa", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Tally", text, StringComparison.OrdinalIgnoreCase);

            Assert.Contains("Exported", page.ExportStatus, StringComparison.Ordinal);
        }
        finally { Cleanup(w, dir); }
    }

    // ================================================================ the statutory text ON THE SCREEN

    /// <summary>
    /// 🔴 <b>THE CAPTIONS, NOT THE NUMBERS.</b> Nothing in the suite asserted any heading or label on this page,
    /// which is how a WRONG TABLE NUMBER and a WRONG STATUTORY CLAUSE LETTER — twice — survived a 3,500-test Desktop
    /// leg. Both reach a filer's eyes on a return that is submitted to the government, and the table number
    /// contradicted the branch's own emitted file (<c>tbl5_8_distribution</c>).
    ///
    /// <para><b>Verified by content, by me, at the sources:</b> GSTR-6's distribution of ITC is <b>Tables 5 and 8</b>
    /// — "<i>Table 5, 8: To enter details of distribution of input tax credit for ISD invoices and ISD Credit
    /// notes</i>" — while Table 6 is the note/amendment family, "<i>Table 6B: To enter details of debit or credit
    /// notes received</i>" (<c>tutorial.gst.gov.in/userguide/returns/GSTR-6_faq.htm</c>). The eligible/ineligible
    /// separation is <b>Rule 39(1)(g)</b> — "<i>the Input Service Distributor shall, in accordance with the
    /// provisions of clause (d) and (e), separately distribute the amount of ineligible input tax credit and the
    /// amount of eligible input tax credit</i>" — whereas Rule 39(1)(b) is the footing cap, "<i>the amount of the
    /// credit distributed shall not exceed the amount of credit available for distribution</i>"
    /// (<c>taxinformation.cbic.gov.in/content/html/tax_repository/gst/rules/cgst_rules/active/chapter5/rule39_v1.00.html</c>).</para>
    /// </summary>
    [AvaloniaFact]
    public void The_gstr6_screen_states_the_right_table_numbers_and_the_right_rule_39_clause_letters()
    {
        var (w, vm, dir) = NewWindow("Gstr6Captions");
        try
        {
            SeedCompany(vm, "ISD Caption Co", withIsd: true);
            Pump(w);

            KeyboardInto(w, vm, "Statutory Reports");
            KeyboardInto(w, vm, "GST Returns (Advanced)");
            KeyboardInto(w, vm, "GSTR-6 (ISD)");
            Assert.Equal(Screen.Gstr6Report, vm.CurrentScreen);

            // The distribution block is Tables 5 AND 8.
            Assert.True(ScreenShows(w, "Tables 5 & 8"),
                "The distribution block must be captioned Tables 5 & 8. Visible headings: "
                + string.Join(" | ", VisibleTextBlocks(w).Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t))));
            Assert.False(ScreenShows(w, "Tables 5 & 6"),
                "'Tables 5 & 6' is the WRONG table number on a filed return — Table 6 is the note/amendment family "
                + "(6A/6B/6C) — and it contradicts this branch's own emitted tbl5_8_distribution key.");

            // The eligible/ineligible split is Rule 39(1)(g), and (1)(b) must not be used for it.
            //
            // 🔴 SCOPED TO THE TWO LABELS, DELIBERATELY — NOT A WHOLE-SCREEN "Rule 39(1)(b)" ABSENCE. Rule 39(1)(b)
            // IS the right citation for the footing cap, and it legitimately appears in this page's own
            // diagnostics (Gstr6.cs emits it for the purchase-return netting and for an unabsorbed excess). A
            // whole-screen negative would therefore pass today only because this fixture posts no return, and
            // would fail for entirely the wrong reason the moment any fixture did. The defect was two specific
            // LABELS, so the assertion is on those labels.
            var splitLabels = VisibleTextBlocks(w)
                .Select(t => t.Text ?? string.Empty)
                .Where(t => t.StartsWith("of which eligible", StringComparison.Ordinal)
                         || t.StartsWith("of which ineligible", StringComparison.Ordinal))
                .ToList();

            Assert.Equal(2, splitLabels.Count);
            Assert.All(splitLabels, label =>
            {
                Assert.Contains("Rule 39(1)(g)", label, StringComparison.Ordinal);
                Assert.DoesNotContain("Rule 39(1)(b)", label, StringComparison.Ordinal);
            });

            // The relevant-period basis is the Explanation's clause (i), not the pre-substitution (a).
            var page = Assert.IsType<Gstr6ReportViewModel>(vm.Gstr6Report);
            Assert.Contains("Explanation (i)", page.RelevantPeriodText, StringComparison.Ordinal);
            Assert.DoesNotContain("Explanation (a)", page.RelevantPeriodText, StringComparison.Ordinal);
        }
        finally { Cleanup(w, dir); }
    }

    // ================================================================ the export must SPEAK

    /// <summary>
    /// 🔴 <b>Ctrl+A FILED A STATUTORY RETURN SILENTLY AND SWALLOWED EVERY FAILURE.</b> The view model set
    /// <c>ExportStatus</c> on both the success and the catch, but it was bound NOWHERE in the GSTR-6 template, and
    /// <c>ExportFolder</c> had no control at all — so the export took the empty-folder arm and wrote into the process
    /// working directory while telling the operator nothing.
    ///
    /// <para>This test asserts the <b>RENDERED WINDOW</b>, not the view-model member. The pre-existing export test
    /// asserted <c>page.ExportStatus</c> — an engine member — for the one thing that has to be on screen, which is
    /// precisely why a dead binding was invisible to it.</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_A_tells_the_operator_on_the_rendered_window_both_when_it_writes_and_when_it_cannot()
    {
        var (w, vm, dir) = NewWindow("Gstr6Feedback");
        try
        {
            SeedCompany(vm, "ISD Feedback Co", withIsd: true);
            Pump(w);

            KeyboardInto(w, vm, "Statutory Reports");
            KeyboardInto(w, vm, "GST Returns (Advanced)");
            KeyboardInto(w, vm, "GSTR-6 (ISD)");
            var page = Assert.IsType<Gstr6ReportViewModel>(vm.Gstr6Report);

            // The destination must be settable FROM THE SCREEN — a path no UI control offers is not a feature.
            var folderBoxes = VisibleControls<TextBox>(w);
            Assert.Contains(folderBoxes, b => b.GetValue(TextBox.TextProperty) is null or "");
            Assert.True(ScreenShows(w, "Export folder"),
                "The export destination has no control on this page, so Ctrl+A writes into the process working "
                + "directory and the filer cannot say where the return went.");

            // ---- The SUCCESS path says so, on screen.
            var outDir = Path.Combine(dir, "export");
            Directory.CreateDirectory(outDir);
            page.ExportFolder = outDir;
            w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(w);

            Assert.Single(Directory.GetFiles(outDir, "*.json"));
            Assert.True(ScreenShows(w, "Exported"),
                "Ctrl+A wrote the return and the window said nothing. Visible text: "
                + string.Join(" | ", VisibleTextBlocks(w).Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t))));
            Assert.True(ScreenShows(w, outDir) || ScreenShows(w, page.ExportFileName),
                "The operator must be told WHERE the filed return was written.");

            // ---- The FAILURE path says so too, instead of being swallowed by the catch.
            page.ExportFolder = Path.Combine(dir, "no-such-dir-" + Guid.NewGuid().ToString("N"));
            w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(w);

            Assert.True(ScreenShows(w, "Could not write the return file"),
                "A return that silently FAILED to write is worse than one that refused. The catch set ExportStatus "
                + "and nothing rendered it.");
        }
        finally { Cleanup(w, dir); }
    }

    // ================================================================ the ISD registration gate

    /// <summary>
    /// A company that is not a Regular dealer must not be offered an Input Service Distributor registration: the
    /// GSTR-6 menu row lives under <c>IsRegularGstDealer</c>, so creating one produced a persisted registration with
    /// NO route to the return it exists to file. An ISD distributes input tax credit (CGST Act §20(1)–(2)) and a
    /// composition taxpayer avails none, so refusing is the right answer rather than ungating the menu.
    /// </summary>
    [AvaloniaFact]
    public void A_composition_company_is_not_offered_an_input_service_distributor_registration()
    {
        var (w, vm, dir) = NewWindow("IsdGate");
        try
        {
            vm.NewCompanyName = "Composition Co";
            vm.CreateCompany();
            var c = vm.Company!;
            c.FinancialYearStart = FyStart;
            c.BooksBeginFrom = FyStart;
            new GstService(c).EnableGst(new GstConfig
            {
                HomeStateCode = Karnataka,
                Gstin = GstinFor(Karnataka, "AAPFU0939F"),
                RegistrationType = GstRegistrationType.Composition,
                CompositionSubType = CompositionSubType.Trader,
                ApplicableFrom = FyStart,
                Periodicity = GstReturnPeriodicity.Monthly,
            });
            vm.ShowGateway();
            Pump(w);

            var master = new GstRegistrationsMasterViewModel(
                c, new CompanyStorage(Path.Combine(dir, "store")), () => { });

            Assert.False(master.IsdAllowed);
            Assert.DoesNotContain(GstRegistrationsMasterViewModel.IsdOptionText, master.RegistrationTypeOptions);

            // And the refusal holds even when the type is set past the picker.
            master.Name = "Head Office (ISD)";
            master.SelectedState = Karnataka + " — Karnataka";
            master.Gstin = GstinFor(Karnataka, "AAACC1206D");
            master.SelectedRegistrationType = GstRegistrationsMasterViewModel.IsdOptionText;
            Assert.False(master.Create());
            Assert.Contains("Regular dealer", master.Message ?? string.Empty, StringComparison.Ordinal);
            Assert.Empty(c.Gst!.IsdRegistrations);

            // The control: a Regular company IS offered it.
            var regular = NewWindow("IsdGateRegular");
            try
            {
                SeedCompany(regular.Vm, "Regular Co", withIsd: false);
                var ok = new GstRegistrationsMasterViewModel(
                    regular.Vm.Company!, new CompanyStorage(Path.Combine(regular.Dir, "store")), () => { });
                Assert.True(ok.IsdAllowed);
                Assert.Contains(GstRegistrationsMasterViewModel.IsdOptionText, ok.RegistrationTypeOptions);
            }
            finally { Cleanup(regular.Window, regular.Dir); }
        }
        finally { Cleanup(w, dir); }
    }
}
