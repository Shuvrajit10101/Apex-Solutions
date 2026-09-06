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
/// GSTR-9A has been waived by notification for years after FY 2018-19. So the page must say so <b>on its face</b>,
/// and these tests assert it does — on the realised visual tree, because a view-model string nobody renders would
/// pass a flag assertion and still leave the operator filing the wrong return.</para>
///
/// <para><b>🔴 THE SOURCING GUARD IS THE POINT OF <see cref="The_waiver_clause_names_no_notification_we_did_not_read"/>.</b>
/// The waiver notification for FY 2019-20 onward could <b>not</b> be retrieved (the notification-series slug 404s),
/// so the statement says "waived by notification" and names none. A later editor "improving" that sentence by
/// dropping a plausible number into it would be repeating the <c>SeedTdsTcsRates</c> mistake this project has
/// already had to strip out of shipped code. That test is the lock against it.</para>
///
/// <para>Headless-safe: visual-tree and text inspection only — no Skia, no rendered frame, no printer, no disk write.
/// Every path is built with <see cref="Path.Combine"/> and every comparison is <see cref="StringComparison.Ordinal"/>
/// or explicitly culture-invariant, so the ubuntu and macos legs see exactly what Windows sees.</para>
/// </summary>
public sealed class Gstr9aApplicabilityTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    /// <summary>The clause whose whole purpose is to name NO notification. Asserted verbatim.</summary>
    private const string WaiverClause = "waived by notification for years after FY 2018-19";

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
    /// 🔴 <b>THE SOURCING LOCK.</b> The waiver notification for FY 2019-20 onward was <b>not retrieved</b> — the
    /// notification-series PDF slug returns 404 and the CBIC consolidation we did read is the Rules, not the
    /// notifications. The statement therefore says "waived by notification" and names <b>none</b>. This test exists
    /// so that the next editor who thinks the sentence looks unfinished cannot quietly complete it with a number
    /// nobody here read. That is not a style preference: shipped code in this product has already had to have
    /// blog-sourced statutory figures stripped back out of it.
    /// </summary>
    [Fact]
    public void The_waiver_clause_names_no_notification_we_did_not_read()
    {
        var text = GstOfflineReturnsViewModel.Gstr9aApplicabilityText;

        Assert.Contains(WaiverClause, text, StringComparison.Ordinal);

        // The ONE notification the statement may name is 20/2019-CT, which substituted rule 62(1) and WAS read in
        // the CBIC consolidation cited on the constant. Any other notification number in this sentence is unsourced.
        foreach (var unsourced in new[] { "47/2019", "30/2021-CT dt", "notification 47", "Notification 47" })
            Assert.DoesNotContain(unsourced, text, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("Notification 20/2019-CT", text, StringComparison.Ordinal);
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
            Assert.False(ScreenShows(w, WaiverClause),
                "The GSTR-9A applicability statement is still on screen after the operator switched to GSTR-4. A " +
                "note that outlives the form it describes is worse than no note: it tells the dealer that the " +
                "return they DO file is not a filing artefact.");

            // And it comes back when they switch back — the note is state, not a one-shot.
            page.SelectedReturn = page.Returns.Single(r => r.Kind == GstOfflineReturnKind.Gstr9a);
            Pump(w);
            Assert.True(ScreenShows(w, WaiverClause));
        }
        finally { Cleanup(w, dir); }
    }

    private static string Gstr9aApplicabilityText() => GstOfflineReturnsViewModel.Gstr9aApplicabilityText;
}
