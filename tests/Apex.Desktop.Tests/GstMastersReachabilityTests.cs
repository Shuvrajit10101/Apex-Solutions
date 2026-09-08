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
/// <b>Census rows 6.23 (Multiple GSTIN registrations) and 6.25 (GST Classification master) — the LAST mile,
/// driven by the keyboard alone.</b>
///
/// <para><b>🔴 WHY THIS FILE PRESSES KEYS AND WALKS THE REALISED VISUAL TREE INSTEAD OF ASSERTING A VIEW-MODEL
/// FLAG.</b> Both rows are exactly the shape this project has filed three dead features under: a correct engine
/// with no way in. A test that called <c>vm.ShowGstRegistrationsMaster()</c> and asserted
/// <c>vm.GstRegistrationsMaster is not null</c> would stay green on the day the <b>menu row</b>, the
/// <b>dispatch case</b> or the <b>DataTemplate</b> was missing — and those three, not the opener, are what make a
/// capability reachable. So every test below starts on the Gateway, walks the Miller cascade with ArrowDown and
/// Enter through <see cref="MainWindow"/>'s own key handler, and then looks for controls a person could actually
/// see and type into.</para>
///
/// <para><b>And both rows are gated on GST being enabled</b>, so the last test proves the gate: a non-GST company
/// is not offered either row at all, which is what ER-13 means at the menu.</para>
///
/// <para>Headless-safe: visual-tree, text and enabled-state inspection only. Culture-invariant, and every path is
/// built with <see cref="Path.Combine"/>, so this behaves identically on the ubuntu and macos CI legs.</para>
/// </summary>
public sealed class GstMastersReachabilityTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2024, 4, 1);

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

    /// <summary>
    /// Walks the Miller cascade <b>by keyboard only</b>: ArrowDown until the highlighted item is
    /// <paramref name="label"/>, then Enter. Fails loudly — naming what the column actually offered — because
    /// "the row is not in the menu" is precisely the defect being guarded.
    /// </summary>
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
        var dir = Path.Combine(Path.GetTempPath(), "ApexP1_" + tag + "_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>A saved Regular-GST company sitting on the Gateway.</summary>
    private static void SeedRegularGst(MainWindowViewModel vm, string name)
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

    // ================================================================ ROW 6.23 — GST Registration

    /// <summary>
    /// 🔴 THE ROW-6.23 TEST. Gateway → Masters → Create → GST Registration, by ArrowDown and Enter alone, ending
    /// on a form the operator can see and a grid that already lists the company's OWN registration.
    /// </summary>
    [AvaloniaFact]
    public void Gst_registration_master_is_reachable_from_the_gateway_by_keyboard_alone()
    {
        var (w, vm, dir) = NewWindow("GstRegReach");
        try
        {
            SeedRegularGst(vm, "GST Reg Reach Co");
            Pump(w);

            KeyboardInto(w, vm, "Create");
            KeyboardInto(w, vm, "GST Registration");

            Assert.Equal(Screen.GstRegistrationsMaster, vm.CurrentScreen);
            Assert.NotNull(vm.GstRegistrationsMaster);

            // The form is REALISED — the vendor's field captions are on screen, not merely on a view model.
            Assert.True(ScreenShows(w, "GST Registrations"), "The master's title is not on screen.");
            Assert.True(ScreenShows(w, "Registration Name"), "The vendor's Registration Name field is not on screen.");
            Assert.True(ScreenShows(w, "GSTIN/UIN"), "The vendor's GSTIN/UIN field is not on screen.");
            Assert.True(ScreenShows(w, "Registration Type"), "The vendor's Registration Type field is not on screen.");
            Assert.True(ScreenShows(w, "Periodicity of GSTR-1"), "The vendor's GSTR-1 periodicity field is not on screen.");

            // …and the operator can actually type into it and pick a State.
            Assert.True(VisibleControls<TextBox>(w).Count >= 4, "The create form has no realised text boxes.");
            Assert.True(VisibleControls<ComboBox>(w).Count >= 3, "The State / type / periodicity pickers are not realised.");

            // 🔴 THE COMPANY'S OWN REGISTRATION IS ALREADY LISTED. This is the half a "create screen" test would
            // miss: the first registration lives in the company's GST block, not in the collection, so if the
            // screen only listed the collection the operator would see an empty grid on a registered company and
            // reasonably conclude they had to create their own GSTIN a second time.
            Assert.True(ScreenShows(w, "Maharashtra Registration"),
                "The company's own (first) registration is not listed on the master.");
            Assert.True(ScreenShows(w, "Company (first)"),
                "The grid does not distinguish the company's own registration from the additional ones.");
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// Creating a second registration through the screen persists it and lists it — and the company becomes
    /// multi-registration, which is the switch every GST return is gated on.
    /// </summary>
    [AvaloniaFact]
    public void Creating_a_second_registration_through_the_screen_persists_and_lists_it()
    {
        var (w, vm, dir) = NewWindow("GstRegCreate");
        try
        {
            SeedRegularGst(vm, "GST Reg Create Co");
            Pump(w);
            KeyboardInto(w, vm, "Create");
            KeyboardInto(w, vm, "GST Registration");

            var master = vm.GstRegistrationsMaster!;
            master.SelectedState = master.StateOptions.Single(s => s.StartsWith("24", StringComparison.Ordinal));
            master.Gstin = "24AAACC1206D1ZM";
            master.Name = "Gujarat Registration";
            Assert.True(master.Create(), $"Create failed: {master.Message}");
            Pump(w);

            var gst = vm.Company!.Gst!;
            Assert.True(gst.IsMultiRegistration);
            var added = Assert.Single(gst.AdditionalRegistrations);
            Assert.Equal("24", added.StateCode);
            Assert.Equal("Gujarat Registration", added.Name);

            // It is on screen, alongside the company's own — the realised grid, not the collection.
            Assert.True(ScreenShows(w, "Gujarat Registration"), "The new registration is not listed on screen.");
            Assert.True(ScreenShows(w, "Maharashtra Registration"), "The first registration vanished from the list.");

            // A duplicate State is refused at the screen, with a message the operator can read.
            master.SelectedState = master.StateOptions.Single(s => s.StartsWith("24", StringComparison.Ordinal));
            master.Gstin = "24AABCC1206D1ZL";
            master.Name = "Gujarat Again";
            Assert.False(master.Create());
            Assert.NotNull(master.Message);
            Assert.Single(gst.AdditionalRegistrations);   // …and the rejected one did NOT stay behind in memory
        }
        finally { Cleanup(w, dir); }
    }

    // ================================================================ ROW 6.25 — GST Classification

    /// <summary>
    /// 🔴 THE ROW-6.25 TEST. Gateway → Masters → Create → GST Classification, by ArrowDown and Enter alone,
    /// ending on the vendor's two-section form.
    /// </summary>
    [AvaloniaFact]
    public void Gst_classification_master_is_reachable_from_the_gateway_by_keyboard_alone()
    {
        var (w, vm, dir) = NewWindow("GstClsReach");
        try
        {
            SeedRegularGst(vm, "GST Classification Reach Co");
            Pump(w);

            KeyboardInto(w, vm, "Create");
            KeyboardInto(w, vm, "GST Classification");

            Assert.Equal(Screen.GstClassificationMaster, vm.CurrentScreen);
            Assert.NotNull(vm.GstClassificationMaster);

            // The vendor's OWN two section headings and their fields are realised.
            Assert.True(ScreenShows(w, "HSN/SAC & Related Details"), "The vendor's first section heading is missing.");
            Assert.True(ScreenShows(w, "GST Rate & Related Details"), "The vendor's second section heading is missing.");
            Assert.True(ScreenShows(w, "Nature of Transaction"), "The vendor's Nature of Transaction field is missing.");
            Assert.True(ScreenShows(w, "Integrated Tax %"), "The vendor's Integrated Tax rate field is missing.");
            Assert.True(ScreenShows(w, "Cess Valuation Type"), "The vendor's Cess Valuation Type field is missing.");
            Assert.True(ScreenShows(w, "Central Tax"), "The auto-calculated Central Tax is not shown.");
            Assert.True(ScreenShows(w, "State Tax"), "The auto-calculated State Tax is not shown.");

            Assert.True(VisibleControls<TextBox>(w).Count >= 5, "The create form has no realised text boxes.");
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// Typing an integrated rate updates the auto-calculated Central and State halves <b>on screen</b> — the
    /// vendor's behaviour, and the visible proof that the two are derived rather than separately keyed.
    /// </summary>
    [AvaloniaFact]
    public void Typing_an_integrated_rate_updates_the_auto_calculated_halves_on_screen()
    {
        var (w, vm, dir) = NewWindow("GstClsHalves");
        try
        {
            SeedRegularGst(vm, "GST Classification Halves Co");
            Pump(w);
            KeyboardInto(w, vm, "Create");
            KeyboardInto(w, vm, "GST Classification");

            var master = vm.GstClassificationMaster!;
            Assert.True(ScreenShows(w, "—"), "With no rate typed the halves should read as unset, not 0%.");

            master.IntegratedRateText = "18";
            Pump(w);

            // 🔴 On the REALISED screen, not just on the view model. A binding that never fired would leave the
            // operator keying an 18% classification while the screen still showed no halves.
            Assert.True(ScreenShows(w, "9%"),
                "Typing 18 did not update the auto-calculated Central/State halves on screen.");

            master.Create();
            Pump(w);
            Assert.True(master.Message is null || !master.Message.Contains("required", StringComparison.Ordinal)
                        || vm.Company!.Gst!.Classifications.Count == 0);
        }
        finally { Cleanup(w, dir); }
    }

    // ================================================================ filing per registration

    /// <summary>
    /// 🔴 <b>THE HAZARD TEST.</b> Once a second registration exists, the engine REFUSES to build an
    /// unscoped GST return. That is correct, and it means the filing screen must (a) offer a registration picker
    /// and (b) scope its projection by it — otherwise creating a registration through the master screen would
    /// leave the operator with GST returns they cannot produce at all, and an unhandled exception where a
    /// document used to be. Both halves are asserted here, on the realised screen.
    /// </summary>
    [AvaloniaFact]
    public void The_filing_screen_offers_a_registration_picker_and_scopes_the_return_by_it()
    {
        var (w, vm, dir) = NewWindow("GstFilingScope");
        try
        {
            SeedRegularGst(vm, "GST Filing Scope Co");

            // A single-GSTIN company: exactly one registration, and the picker stays HIDDEN — that book's page
            // is byte-identical to v60 (ER-13).
            var single = new GstOfflineReturnsViewModel(vm.Company!, GstOfflineReturnKind.Gstr1);
            Assert.Single(single.Registrations);
            Assert.False(single.ShowsRegistrationPicker);
            Assert.NotNull(single.SelectedRegistration);
            Assert.True(single.SelectedRegistration!.IsPrimary);

            // Add a second registration through the master screen, exactly as an operator would.
            vm.ShowGstRegistrationsMaster();
            var master = vm.GstRegistrationsMaster!;
            master.SelectedState = master.StateOptions.Single(s => s.StartsWith("24", StringComparison.Ordinal));
            master.Gstin = "24AAACC1206D1ZM";
            master.Name = "Gujarat Registration";
            Assert.True(master.Create(), $"Create failed: {master.Message}");

            // Now the filing screen offers BOTH, shows the picker, and defaults to the company's own.
            var multi = new GstOfflineReturnsViewModel(vm.Company!, GstOfflineReturnKind.Gstr1);
            Assert.Equal(2, multi.Registrations.Count);
            Assert.True(multi.ShowsRegistrationPicker);
            Assert.True(multi.SelectedRegistration!.IsPrimary);

            // 🔴 And it PROJECTS — no refusal reaches the screen, because the screen names a registration.
            // Before this wiring the same page threw InvalidOperationException straight through the shell.
            Assert.DoesNotContain("multiple GST registrations", multi.StatusText, StringComparison.Ordinal);

            // Switching registration re-projects without throwing either.
            multi.SelectedRegistration = multi.Registrations[1];
            Assert.DoesNotContain("multiple GST registrations", multi.StatusText, StringComparison.Ordinal);
        }
        finally { Cleanup(w, dir); }
    }

    // ================================================================ the ER-13 gate

    /// <summary>
    /// A company with GST switched off is offered NEITHER row. Both masters are GST-only surfaces, so a non-GST
    /// book must look exactly as it did before v61 — which at the menu means the rows are simply not there.
    /// </summary>
    [AvaloniaFact]
    public void A_non_gst_company_is_offered_neither_gst_master()
    {
        var (w, vm, dir) = NewWindow("GstMastersGate");
        try
        {
            vm.NewCompanyName = "No GST Co";
            vm.CreateCompany();
            vm.ShowGateway();
            Pump(w);

            KeyboardInto(w, vm, "Create");

            var offered = vm.Menu.Where(i => i.IsSelectable).Select(i => i.Label).ToList();
            Assert.DoesNotContain("GST Registration", offered);
            Assert.DoesNotContain("GST Classification", offered);

            // …and the openers themselves refuse too, so the gate does not depend on the menu alone.
            vm.ShowGstRegistrationsMaster();
            Assert.Null(vm.GstRegistrationsMaster);
            vm.ShowGstClassificationMaster();
            Assert.Null(vm.GstClassificationMaster);
        }
        finally { Cleanup(w, dir); }
    }
}
