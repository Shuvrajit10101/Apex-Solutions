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
/// <b>Census row 6.13 — GSTR-9A. The half the dispatcher did not carry: the APPLICABILITY STATEMENT.</b>
///
/// <para><b>🔴 WHAT THIS SLICE IS, AND WHAT IT DELIBERATELY IS NOT.</b> The GSTR-9A <i>route</i> already exists on
/// <c>main</c>: <c>BuildCompositionReturnsColumn</c> carries a "GSTR-9A" row and the menu case opens the shared
/// offline-returns page preselected on that form. This file does <b>not</b> re-test that — and this slice did
/// <b>not</b> build a second GSTR-9A page, because one existed. What the dispatcher shape could not carry is the one
/// thing that makes the row honest: <b>a composition dealer who opened a page headed "GST Offline Return Files",
/// read 9A figures off it and filed them would file the WRONG FORM.</b> The operative annual return for a person
/// paying tax under section 10 is <b>GSTR-4</b> (rule 62(1)(ii)), which the very same page offers one row above, and
/// GSTR-9A is a form the common portal will no longer accept even for the two years it was ever relaxed for. So the
/// page must say so <b>on its face</b>, and these tests assert it does — on the realised visual tree, because a
/// view-model string nobody renders would pass a flag assertion and still leave the operator filing the wrong
/// return.</para>
///
/// <para><b>🔴 THIS FILE ONCE PINNED A FALSE STATEMENT, AND THAT IS WHY
/// <see cref="The_relaxation_clause_says_what_the_circular_says"/> IS SHAPED THE WAY IT IS.</b> The shipped string
/// used to read <i>"GSTR-9A filing has been waived by notification for years after FY 2018-19"</i>, and the test
/// here asserted that sentence <b>verbatim</b> — so the test was not a guard, it was a lock holding the error in.
/// CBIC's own <b>Circular 124/43/2019-GST dt. 18.11.2019</b>
/// (<c>https://cbic-gst.gov.in/pdf/circular-cgst-124.pdf</c>, retrieved 2026-09-06) refutes it on every limb: the
/// relaxation was <b>optional filing, not a waiver</b>; it covered <b>FY 2017-18 and FY 2018-19</b> — the years
/// <b>before</b> the cut-off the sentence named, not the years after it; and it applied only where aggregate
/// turnover did not exceed <b>two crore rupees</b>, a condition the sentence dropped entirely.
///
/// <para>The lock is now two-sided, which is the only shape that works. It pins the corrected clause <b>and</b>
/// asserts the old false wording is absent, so the error cannot creep back; and it still refuses any notification
/// number that is not quoted in <see cref="GstOfflineReturnsViewModel.Gstr9aApplicabilityText"/>'s own sources
/// block, because the original worry was real — dropping a plausible-looking number into a statutory sentence is
/// the <c>SeedTdsTcsRates</c> mistake this project has already had to strip out of shipped code. What changed is
/// that 47/2019-CT is no longer plausible-looking: it is quoted, by number and date, in a CBIC PDF that was
/// actually read.</para></para>
///
/// <para>Headless-safe: visual-tree and text inspection only — no Skia, no rendered frame, no printer, no disk write.
/// Every path is built with <see cref="Path.Combine"/> and every comparison is <see cref="StringComparison.Ordinal"/>
/// or explicitly culture-invariant, so the ubuntu and macos legs see exactly what Windows sees.</para>
/// </summary>
public sealed class Gstr9aApplicabilityTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    /// <summary>The corrected relaxation clause, asserted verbatim and used as the on-screen probe. Every word of it
    /// is quoted or paraphrased from Circular 124/43/2019-GST — see the type remarks.</summary>
    private const string RelaxationClause =
        "For FY 2017-18 and FY 2018-19 only, Notification 47/2019-CT dt. 09.10.2019 made that annual return optional";

    /// <summary>🔴 The wording this file used to pin. It is FALSE and must never return. Kept as a named constant so
    /// the regression assertion reads as what it is, rather than as an anonymous string nobody dares delete.</summary>
    private const string RetractedFalseClause = "waived by notification for years after FY 2018-19";

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

    /// <summary>
    /// Walks the Miller cascade <b>by keyboard only</b>: ArrowDown until the highlighted item is
    /// <paramref name="label"/>, then Enter. Fails loudly, naming what the column actually offered, rather than
    /// looping — "the row is not in the menu" is itself a defect worth a readable message.
    /// </summary>
    private static void KeyboardInto(MainWindow w, MainWindowViewModel vm, string label)
    {
        var offered = vm.Menu.Where(i => i.IsSelectable).Select(i => i.Label).ToList();
        Assert.True(offered.Contains(label),
            $"The keyboard cascade never offered '{label}'. This column offers: {string.Join(" | ", offered)}.");

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
        var dir = Path.Combine(Path.GetTempPath(), "ApexD3_" + tag + "_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>A saved Composition company on the Gateway — the only registration type that is offered GSTR-9A.</summary>
    private static void SeedComposition(MainWindowViewModel vm, string name)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Composition,
            CompositionSubType = CompositionSubType.Trader,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Quarterly,
        });

        vm.ShowGateway();
    }

    // ================================================================ the tests

    /// <summary>
    /// 🔴 <b>THE ROW-6.13 TEST, AND IT FAILS ON <c>main</c>.</b> Gateway → Statutory Reports → Composition Returns →
    /// GSTR-9A, by ArrowDown and Enter alone. The route is <b>not</b> what is new — the applicability statement the
    /// page carries once it arrives is. Asserted on the realised visual tree: the operator must be able to READ that
    /// GSTR-4 is the return they actually file and that these figures are not a filing artefact.
    /// </summary>
    [AvaloniaFact]
    public void Gstr9a_is_reachable_by_keyboard_and_states_its_applicability_on_the_face_of_the_page()
    {
        var (w, vm, dir) = NewWindow("Gstr9aApplic");
        try
        {
            SeedComposition(vm, "Gstr9a Applicability Co");
            Pump(w);

            KeyboardInto(w, vm, "Statutory Reports");
            KeyboardInto(w, vm, "Composition Returns");
            KeyboardInto(w, vm, "GSTR-9A");

            Assert.Equal(Screen.GstOfflineReturns, vm.CurrentScreen);
            var page = vm.GstOfflineReturns;
            Assert.True(page is not null, "The GSTR-9A menu row opened no page.");
            Assert.Equal(GstOfflineReturnKind.Gstr9a, page!.SelectedReturn!.Kind);

            // 1. The statement is RENDERED, not merely computed. A string on a view model nobody binds would let an
            //    operator file the wrong return while this test stayed green — which is exactly the class of defect
            //    this project has shipped three times.
            Assert.True(ScreenShows(w, Gstr9aApplicabilityText()),
                "The GSTR-9A applicability statement is not visible anywhere on the page. Without it, a composition " +
                "dealer reads a page headed \"GST Offline Return Files\", takes the 9A figures off it and files the " +
                "wrong form — the operative annual return under rule 62(1)(ii) is GSTR-4.");

            // 2. The two clauses that carry the whole meaning, asserted individually so a future rewrite that keeps
            //    the paragraph's length but loses its point cannot pass.
            Assert.Contains("GSTR-4", GstOfflineReturnsViewModel.Gstr9aApplicabilityText, StringComparison.Ordinal);
            Assert.Contains("it is not a filing artefact",
                GstOfflineReturnsViewModel.Gstr9aApplicabilityText, StringComparison.Ordinal);

            // 3. Brand rule: the shipped product never says "Tally".
            Assert.DoesNotContain(VisibleTextBlocks(w),
                t => (t.Text ?? string.Empty).Contains("Tally", StringComparison.OrdinalIgnoreCase));
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE SOURCING LOCK, NOW TWO-SIDED — AND THE REGRESSION LOCK ON A STATEMENT THAT WAS WRONG IN SHIPPED
    /// CODE.</b> Every clause asserted here is quoted in the sources block on
    /// <see cref="GstOfflineReturnsViewModel.Gstr9aApplicabilityText"/>, and every clause refused here is one that
    /// was not read. The three positive assertions are the three limbs the old sentence got wrong — <b>which years,
    /// optional-vs-waived, and the turnover condition</b> — because a correction that only fixes the wording an
    /// editor happens to look at is not a correction.
    /// </summary>
    [Fact]
    public void The_relaxation_clause_says_what_the_circular_says()
    {
        var text = GstOfflineReturnsViewModel.Gstr9aApplicabilityText;

        // 1. 🔴 THE RETRACTION. The false sentence must be gone, in any casing.
        Assert.DoesNotContain(RetractedFalseClause, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("after FY 2018-19", text, StringComparison.OrdinalIgnoreCase);

        // 2. The corrected clause, verbatim: the right years, the right instrument, and "optional" not "waived".
        Assert.Contains(RelaxationClause, text, StringComparison.Ordinal);

        // 3. The turnover condition the old sentence dropped. Circular 124/43/2019-GST para 2(a) states the
        //    relaxation only "for those registered persons whose aggregate turnover in a financial year does not
        //    exceed two crore rupees" — a statement of the relaxation without its condition is not true of everyone
        //    reading it.
        Assert.Contains("two crore rupees", text, StringComparison.Ordinal);

        // 4. The circular itself is named, because it is the source that carries the portal cut-off.
        Assert.Contains("Circular 124/43/2019-GST dt. 18.11.2019", text, StringComparison.Ordinal);

        // 5. 🔴 AND THE LIMIT OF WHAT WE KNOW IS STATED, NOT IMPLIED. No later-year waiver was retrieved; the
        //    sentence has to say so, because silence there is what let the false clause read as complete.
        Assert.Contains("No waiver of GSTR-9A for any later year was retrieved", text, StringComparison.Ordinal);

        // 6. Rule 62(1)(ii)'s own notification survives from the original lock — it was read in the CBIC Rules
        //    consolidation and is still cited.
        Assert.Contains("Notification 20/2019-CT", text, StringComparison.Ordinal);

        // 7. 🔴 THE ORIGINAL WORRY, UNCHANGED IN FORCE. Only instruments quoted in the constant's own sources block
        //    may appear. 47/2019-CT and Circular 124/43/2019 have JOINED that list because a CBIC PDF quoting both
        //    by number and date was actually retrieved and extracted; these have NOT, and each is a plausible
        //    number an editor could reach for from memory when "completing" the sentence.
        foreach (var unsourced in new[]
                 {
                     "77/2020", "31/2021", "10/2022", "14/2023", "07/2023", "30/2021", "notification 9/2020",
                 })
            Assert.DoesNotContain(unsourced, text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The applicability statement belongs to <b>one</b> form. Selecting another must clear it — a note that lingered
    /// over GSTR-4 would tell a composition dealer that the return they DO file is not a filing artefact, which is
    /// the opposite of true and worse than showing nothing.
    /// </summary>
    [AvaloniaFact]
    public void Selecting_another_return_clears_the_applicability_note()
    {
        var (w, vm, dir) = NewWindow("Gstr9aClear");
        try
        {
            SeedComposition(vm, "Gstr9a Clear Co");
            Pump(w);

            vm.OpenGstOfflineReturns(GstOfflineReturnKind.Gstr9a);
            Pump(w);
            var page = vm.GstOfflineReturns!;
            Assert.NotEmpty(page.ApplicabilityNoteText);

            // GSTR-4 is the return this dealer actually files; it carries no such caveat.
            page.SelectedReturn = page.Returns.Single(r => r.Kind == GstOfflineReturnKind.Gstr4);
            Pump(w);

            Assert.Equal(string.Empty, page.ApplicabilityNoteText);
            Assert.False(ScreenShows(w, RelaxationClause),
                "The GSTR-9A applicability statement is still on screen after the operator switched to GSTR-4. A " +
                "note that outlives the form it describes is worse than no note: it tells the dealer that the " +
                "return they DO file is not a filing artefact.");

            // And it comes back when they switch back — the note is state, not a one-shot.
            page.SelectedReturn = page.Returns.Single(r => r.Kind == GstOfflineReturnKind.Gstr9a);
            Pump(w);
            Assert.True(ScreenShows(w, RelaxationClause));
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>A saved <b>Regular</b> GST company on the Gateway — the registration type that is NOT offered
    /// GSTR-9A. Identical to <see cref="SeedComposition"/> but for the registration type, so any difference the
    /// tests find is the registration type and nothing else.</summary>
    private static void SeedRegular(MainWindowViewModel vm, string name)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        vm.ShowGateway();
    }

    /// <summary>
    /// 🔴 <b>THE COMMENT THAT DESCRIBED A STATE THE CODE CANNOT REACH.</b> <c>ProjectGstr9a</c> carried a comment
    /// claiming the applicability statement "is raised for a Composition dealer AND for a Regular one". It never
    /// was and never could be: <c>ApplicableReturns</c> offers GSTR-9A in the Composition arm only,
    /// <c>OpenGstOfflineReturns</c> bails when the arm yields nothing, and the <c>preselect</c> argument can only
    /// pick a form the arm already contains — so a Regular company has no path to <c>ProjectGstr9a</c> at all.
    ///
    /// <para>A false comment is not a cosmetic defect here. It describes a fallback as live, which is exactly the
    /// invitation to delete the "redundant" real guard elsewhere. This test makes the corrected comment checkable:
    /// it drives the two routes a Regular company actually has — the preselect argument, which is the strongest
    /// form of the claim, and the menu — and pins that neither produces a GSTR-9A projection.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_regular_dealer_has_no_path_to_the_gstr9a_projection_at_all()
    {
        var (w, vm, dir) = NewWindow("Gstr9aRegular");
        try
        {
            SeedRegular(vm, "Gstr9a Regular Co");
            Pump(w);

            // 1. The strongest form: ask for GSTR-9A BY NAME. The preselect cannot conjure a form the registration
            //    type does not file, so the page opens on the Regular arm's own first form instead.
            vm.OpenGstOfflineReturns(GstOfflineReturnKind.Gstr9a);
            Pump(w);

            var page = vm.GstOfflineReturns;
            Assert.True(page is not null, "The offline-returns page did not open for a Regular company.");
            Assert.DoesNotContain(page!.Returns, r => r.Kind == GstOfflineReturnKind.Gstr9a);
            Assert.NotEqual(GstOfflineReturnKind.Gstr9a, page.SelectedReturn!.Kind);

            // 2. Therefore ProjectGstr9a never ran, and the statement is not on the page — neither in the view model
            //    nor on the realised tree. A note about a form this company does not file would be noise at best.
            Assert.Equal(string.Empty, page.ApplicabilityNoteText);
            Assert.False(ScreenShows(w, RelaxationClause),
                "A Regular company is showing the GSTR-9A applicability statement. It does not file GSTR-9A, is " +
                "never offered it, and cannot select it — so this note is describing a form that is not on screen.");

            // 3. And the menu route does not offer it either: the Composition Returns group, which is where the
            //    GSTR-9A row lives, is hidden from a Regular company entirely (ER-13).
            vm.ShowGateway();
            Pump(w);
            vm.ShowCompositionReturnsMenu();
            Pump(w);
            Assert.DoesNotContain(vm.Menu.Where(i => i.IsSelectable).Select(i => i.Label), l => l == "GSTR-9A");
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// 🔴 <b>TWO FORMS MUST NOT DESCRIBE THEMSELVES IDENTICALLY.</b> GSTR-4 and GSTR-9A both carried
    /// <c>Description = "Composition annual return"</c> — one line apart in the same arm. That is the precise
    /// confusion <see cref="GstOfflineReturnsViewModel.Gstr9aApplicabilityText"/> exists to prevent, restated as a
    /// data defect: the form the dealer must file and the form the portal will not accept, described in the same
    /// words, in the same picker.
    ///
    /// <para>Asserted over <b>both</b> registration arms and over the pair as a whole, so this cannot be satisfied
    /// by making one description unique while another pair collides.</para>
    /// </summary>
    [AvaloniaTheory]
    [InlineData(GstRegistrationType.Regular)]
    [InlineData(GstRegistrationType.Composition)]
    public void Every_offered_return_describes_itself_distinctly(GstRegistrationType registration)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexD3_Desc_" + Guid.NewGuid().ToString("N"));
        try
        {
            var vm = new MainWindowViewModel(new CompanyStorage(dir));
            vm.NewCompanyName = "Description Co";
            vm.CreateCompany();
            var c = vm.Company!;
            c.FinancialYearStart = FyStart;
            c.BooksBeginFrom = FyStart;
            new GstService(c).EnableGst(new GstConfig
            {
                HomeStateCode = "27",
                Gstin = GstinMaharashtra,
                RegistrationType = registration,
                CompositionSubType = registration == GstRegistrationType.Composition
                    ? CompositionSubType.Trader
                    : null,
                ApplicableFrom = FyStart,
                Periodicity = registration == GstRegistrationType.Composition
                    ? GstReturnPeriodicity.Quarterly
                    : GstReturnPeriodicity.Monthly,
            });

            var page = new GstOfflineReturnsViewModel(c);
            Assert.NotEmpty(page.Returns);

            foreach (var option in page.Returns)
                Assert.False(string.IsNullOrWhiteSpace(option.Description),
                    $"{option.Label} offers no description at all.");

            var duplicated = page.Returns
                .GroupBy(r => r.Description, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => $"\"{g.Key}\" is used by {string.Join(" and ", g.Select(r => r.Label))}")
                .ToList();

            Assert.True(duplicated.Count == 0,
                "Two different return forms describe themselves identically, so the picker cannot tell them " +
                "apart: " + string.Join("; ", duplicated) + ". For GSTR-4 vs GSTR-9A this is the exact mistake " +
                "the applicability note exists to prevent — the return the dealer files and the return the portal " +
                "will not accept, in the same words.");
        }
        finally
        {
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// 🔴 <b>THE MEASUREMENT THIS PAGE NEVER HAD: PROSE IN AN <c>Auto</c> ROW DIRECTLY ABOVE A <c>*</c> ROW.</b>
    /// The two notes (the applicability statement and the schema note) share an <c>Auto</c> row immediately above
    /// the figure grid's <c>*</c> row, so every line they wrap to is taken straight out of the figures — and at a
    /// narrow viewport multi-sentence statutory prose wraps to a great many lines. Nothing measured the result at
    /// any viewport, which is the standing shape of this project's UI defect catalogue.
    ///
    /// <para>Both halves are asserted at four viewports, because either alone is satisfiable by a bad fix:
    /// <b>(a)</b> the figure area keeps a workable height — capping the notes is what buys this, and a fix that
    /// merely shrank the font would not; <b>(b)</b> the applicability note is never CLIPPED — the cap makes the
    /// notes scroll rather than truncate, so no statutory sentence is silently cut. A <c>MaxHeight</c> without (b)
    /// would trade a starved grid for a beheaded statutory warning, which is the worse of the two.</para>
    ///
    /// <para>The floors are deliberately modest — this asserts "not starved", not a pixel-perfect layout, so it
    /// stays useful when the surrounding page is restyled.</para>
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1920, 1080)]
    [InlineData(1366, 768)]
    [InlineData(1280, 720)]
    [InlineData(1024, 700)]
    public void The_applicability_note_never_starves_the_figure_grid_or_clips_itself(int width, int height)
    {
        var (w, vm, dir) = NewWindow($"Gstr9aLayout{width}x{height}");
        try
        {
            w.Width = width;
            w.Height = height;
            SeedComposition(vm, "Gstr9a Layout Co");
            Dispatcher.UIThread.RunJobs();
            w.Measure(new Size(width, height));
            w.Arrange(new Rect(0, 0, width, height));
            Dispatcher.UIThread.RunJobs();

            vm.OpenGstOfflineReturns(GstOfflineReturnKind.Gstr9a);
            Dispatcher.UIThread.RunJobs();
            w.Measure(new Size(width, height));
            w.Arrange(new Rect(0, 0, width, height));
            Dispatcher.UIThread.RunJobs();

            var notes = Descendants(w).OfType<ScrollViewer>()
                .FirstOrDefault(s => s.Name == "GstReturnNotesScroller");
            Assert.True(notes is not null,
                "The notes row is not the named ScrollViewer any more, so nothing is constraining the Auto row " +
                "above the figure grid's * row. Re-establish the cap or re-write this measurement.");

            // (a) The notes row is capped, so the * row below it cannot be starved.
            Assert.True(notes!.Bounds.Height <= 150.5,
                $"At {width}x{height} the notes row measured {notes.Bounds.Height:0.#}px, above its 150px cap. " +
                "Every pixel over the cap is taken from the figure grid's * row directly below it.");

            // (b) …and the note inside it is not clipped: the ScrollViewer's extent covers the note's full desired
            //     height, so the text is reachable by scrolling rather than truncated.
            var noteBlock = VisibleTextBlocks(w)
                .FirstOrDefault(t => (t.Text ?? string.Empty).Contains(RelaxationClause, StringComparison.Ordinal));
            Assert.True(noteBlock is not null,
                $"At {width}x{height} the applicability statement is not rendered at all.");
            Assert.True(noteBlock!.Bounds.Height >= noteBlock.DesiredSize.Height - 1.0,
                $"At {width}x{height} the applicability statement is CLIPPED: it was laid out into " +
                $"{noteBlock.Bounds.Height:0.#}px but wants {noteBlock.DesiredSize.Height:0.#}px. A statutory " +
                "warning that is cut off mid-sentence is worse than one that scrolls.");
            Assert.True(notes.Extent.Height >= noteBlock.Bounds.Height,
                $"At {width}x{height} the notes scroller's extent ({notes.Extent.Height:0.#}px) does not cover " +
                $"the note ({noteBlock.Bounds.Height:0.#}px), so the tail of it cannot be scrolled to.");

            // (c) The figures the page exists to show still have room to be read.
            var figureRows = VisibleTextBlocks(w)
                .Count(t => (t.Text ?? string.Empty) is "Total turnover" or "Taxable turnover" or "Late fee");
            Assert.True(figureRows == 3,
                $"At {width}x{height} only {figureRows} of the three probe figure labels are visible — the notes " +
                "row has squeezed the figure grid out of the page.");
        }
        finally { Cleanup(w, dir); }
    }

    private static string Gstr9aApplicabilityText() => GstOfflineReturnsViewModel.Gstr9aApplicabilityText;
}
