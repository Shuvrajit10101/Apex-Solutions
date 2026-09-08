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
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Seed;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS ROW 6.35 — REACHABILITY. THE THREE NEW TDS SECTIONS HAVE TO BE SOMETHING A USER CAN GET TO FROM
/// THE KEYBOARD, OR THEY ARE NOT SHIPPED.</b>
///
/// <para>This project has filed three capabilities that were fully built, fully tested and reachable by nobody.
/// A seeded master is unusually exposed to a quieter version of the same failure: the seed only runs on
/// <c>EnableTds</c> <b>when the config has no natures at all</b>, so a section added to
/// <c>SeedTdsTcsRates</c> today reaches a <b>brand-new company</b> and no other. Every book that already
/// enabled TDS keeps the old eight for ever, and re-running F11 → Enable TDS does nothing, because the count is
/// not zero. "It works in a new company" would have been the wrong answer.</para>
///
/// <para>So there are two routes to prove, and this file proves both against the realised tree:
/// <list type="number">
///   <item><b>The menu route</b> — Gateway → Create → Statutory Masters → Nature of Payment, walked with the
///     arrow keys, with the three sections present in the list the screen actually renders.</item>
///   <item><b>The top-up route</b> — <b>Ctrl+U</b> on that screen, which is the ONLY way an existing book can
///     acquire a section seeded after the book was created.</item>
/// </list></para>
///
/// <para><b>And the inverse property.</b> The top-up must never overwrite a nature the operator has already
/// created or edited under the same section code, and must never fire on a company that has not enabled TDS.</para>
/// </summary>
public sealed class TdsLongTailReachabilityTests : IDisposable
{
    private const string ValidTan = "MUMA12345B";

    /// <summary>The three sections census row 6.35's first instalment added.</summary>
    private static readonly string[] NewSections = { "194T", "194R", "194S" };

    /// <summary>The eight the Phase-7 seed shipped — i.e. exactly what a book created before this slice holds.
    /// Kept as literal text on purpose: deriving it from the seed would make the "legacy book" fixture silently
    /// gain every future section and the test would stop reproducing the situation it exists for.</summary>
    private static readonly string[] LegacyEightSections =
        { "194A", "194C", "194H", "194I(a)", "194I(b)", "194J(a)", "194J(b)", "194Q" };

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public TdsLongTailReachabilityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexTdsTail_" + Guid.NewGuid().ToString("N"));
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

    // =====================================================================================================
    //  1. The menu route
    // =====================================================================================================

    /// <summary>
    /// 🔴 <b>THE ROUTE, WALKED WITH THE ARROW KEYS, AND NESTED — never a flat dump.</b> Gateway → Create →
    /// Statutory Masters → Nature of Payment. The three new sections must be in the list the screen builds, with
    /// their rate and their Form-26Q code, and marked "Predefined" rather than "Custom".
    /// </summary>
    [Fact]
    public void The_new_sections_are_arrow_reachable_on_the_Nature_of_Payment_master()
    {
        var vm = TdsCompany();

        vm.ShowGateway();
        ArrowToAndDrill(vm, "Create");
        ArrowToAndDrill(vm, "Nature of Payment");

        Assert.Equal(Screen.NatureOfPaymentMaster, vm.CurrentScreen);
        var page = Assert.IsAssignableFrom<NatureOfPaymentMasterViewModel>(vm.NatureOfPaymentMaster);

        foreach (var section in NewSections)
        {
            var row = Assert.Single(page.Natures, r => r.SectionCode == section);
            Assert.Equal("Predefined", row.Kind);
            Assert.False(string.IsNullOrWhiteSpace(row.FvuCode));
        }

        Assert.Equal("10%", page.Natures.Single(r => r.SectionCode == "194T").RateWithPan);
        Assert.Equal("94T", page.Natures.Single(r => r.SectionCode == "194T").FvuCode);
        Assert.Equal("1%", page.Natures.Single(r => r.SectionCode == "194S").RateWithPan);
        Assert.Equal("94S", page.Natures.Single(r => r.SectionCode == "194S").FvuCode);

        // The threshold column states the figure AND the window, so an operator can see it is a yearly
        // aggregate and not a per-payment test.
        Assert.Equal("₹20,000/FY", page.Natures.Single(r => r.SectionCode == "194T").Threshold);
        Assert.Equal("₹10,000/FY", page.Natures.Single(r => r.SectionCode == "194S").Threshold);
    }

    /// <summary>The row lives under its own <b>Statutory Masters</b> heading rather than being dumped flat into
    /// the Create column, and it is absent entirely when TDS is off — an operator with no TAN must not meet a
    /// TDS master.</summary>
    [Fact]
    public void The_master_is_nested_under_Statutory_Masters_and_hidden_when_TDS_is_off()
    {
        var withTds = TdsCompany();
        withTds.ShowGateway();
        ArrowToAndDrill(withTds, "Create");
        Assert.Contains(withTds.Menu, m => m.Label == "Nature of Payment");
        Assert.Contains(withTds.Menu, m => m.Label == "Statutory Masters" && !m.IsSelectable);

        var withoutTds = NewCompany("No Tds Co");
        withoutTds.ShowGateway();
        ArrowToAndDrill(withoutTds, "Create");
        Assert.DoesNotContain(withoutTds.Menu, m => m.Label == "Nature of Payment");
    }

    // =====================================================================================================
    //  2. The top-up route — the one that matters for every book that already exists
    // =====================================================================================================

    /// <summary>
    /// 🔴 <b>THE CENTRAL TEST OF THIS SLICE.</b> A book that enabled TDS before row 6.35 holds the Phase-7
    /// eight, and — measured, not assumed — re-running <c>EnableTds</c> does <b>not</b> give it the new three,
    /// because the seed is skipped whenever the config already has natures. Ctrl+U does, and adds exactly the
    /// three that were missing.
    /// </summary>
    [Fact]
    public void An_existing_book_gains_the_new_sections_only_through_the_top_up()
    {
        var vm = LegacyBook("Legacy Tds Co");
        var company = vm.Company!;

        // Precondition, asserted rather than assumed: the fixture really is a pre-6.35 book.
        Assert.Equal(8, company.NaturesOfPayment.Count);
        foreach (var s in NewSections) Assert.Null(company.FindNatureOfPaymentByCode(s));

        // 🔴 Re-enabling does NOTHING for it. This is the measurement that makes the top-up necessary.
        new TdsTcsService(company).EnableTds(company.Tds!);
        Assert.Equal(8, company.NaturesOfPayment.Count);
        Assert.Null(company.FindNatureOfPaymentByCode("194T"));

        vm.ShowNatureOfPaymentMaster();
        var page = Assert.IsAssignableFrom<NatureOfPaymentMasterViewModel>(vm.NatureOfPaymentMaster);

        Assert.Equal(3, page.AddMissingPredefined());

        Assert.Equal(11, company.NaturesOfPayment.Count);
        foreach (var s in NewSections) Assert.NotNull(company.FindNatureOfPaymentByCode(s));
        Assert.All(NewSections, s => Assert.Contains(page.Natures, r => r.SectionCode == s));

        // …and it survives a real save/load round trip, not just the in-memory object.
        var reloaded = Reload("Legacy Tds Co");
        Assert.Equal(11, reloaded.NaturesOfPayment.Count);
        var t = reloaded.FindNatureOfPaymentByCode("194T")!;
        Assert.Equal(1000, t.RateWithPanBp);
        Assert.Equal("94T", t.FvuSectionCode);
        Assert.Equal(Money.FromRupees(20_000m), t.CumulativeThreshold);
    }

    /// <summary>Idempotent: a second Ctrl+U adds nothing, changes nothing and says so rather than duplicating
    /// every section. (The section code is unique per company, so a duplicate would be a corrupt master set.)</summary>
    [Fact]
    public void A_second_top_up_adds_nothing_and_creates_no_duplicates()
    {
        var vm = LegacyBook("Idempotent Tds Co");
        vm.ShowNatureOfPaymentMaster();
        var page = (NatureOfPaymentMasterViewModel)vm.NatureOfPaymentMaster!;

        Assert.Equal(3, page.AddMissingPredefined());
        Assert.Equal(0, page.AddMissingPredefined());

        Assert.Equal(11, vm.Company!.NaturesOfPayment.Count);
        Assert.Equal(
            vm.Company.NaturesOfPayment.Count,
            vm.Company.NaturesOfPayment.Select(n => n.SectionCode).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("already present", page.Message);
    }

    /// <summary>
    /// 🔴 <b>THE INVERSE PROPERTY, AND THE ONE THAT WOULD HURT A REAL USER.</b> An operator who already hand-created
    /// a §194T nature — perhaps with a rate they were told to use — must keep <b>their</b> figures. The top-up is
    /// additive only; it matches on section code and never overwrites.
    /// </summary>
    [Fact]
    public void The_top_up_never_overwrites_a_section_the_operator_already_created()
    {
        var vm = LegacyBook("Custom Tds Co");
        vm.ShowNatureOfPaymentMaster();
        var page = (NatureOfPaymentMasterViewModel)vm.NatureOfPaymentMaster!;

        page.SectionCode = "194T";
        page.Name = "Partner remuneration (as advised)";
        page.RateWithPanText = "7.5";
        page.RateWithoutPanText = "20";
        page.FvuSectionCode = "94T";
        page.CumulativeThresholdText = "20000";
        Assert.True(page.Create(), page.Message);

        // Only the other two are missing now.
        Assert.Equal(2, page.AddMissingPredefined());

        var kept = vm.Company!.FindNatureOfPaymentByCode("194T")!;
        Assert.Equal(750, kept.RateWithPanBp);                     // the operator's 7.5%, NOT the seeded 10%
        Assert.Equal("Partner remuneration (as advised)", kept.Name);
        Assert.False(kept.IsPredefined);
        Assert.Single(vm.Company.NaturesOfPayment, n => n.SectionCode == "194T");
    }

    /// <summary>A company with TDS switched off is refused by name and nothing is written — the same guard the
    /// Create action applies.</summary>
    [Fact]
    public void The_top_up_is_refused_when_TDS_is_not_enabled()
    {
        var vm = NewCompany("Top Up No Tds Co");
        var page = new NatureOfPaymentMasterViewModel(vm.Company!, _storage, onChanged: () => { });

        Assert.Equal(0, page.AddMissingPredefined());
        Assert.Contains("Enable TDS", page.Message);
        Assert.Empty(vm.Company!.NaturesOfPayment);
    }

    // =====================================================================================================
    //  3. The keystroke, and the realised visual tree
    // =====================================================================================================

    /// <summary>
    /// 🔴 <b>Ctrl+U ACTUALLY REACHES THE ACTION, THROUGH THE REAL KEY HANDLER ON A REAL WINDOW</b> — not through
    /// a direct call to the viewmodel, which is what a test can accidentally prove instead. A button nobody can
    /// reach by keyboard is the defect this project keeps filing.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_U_runs_the_top_up_on_the_Nature_of_Payment_master()
    {
        var vm = LegacyBook("Keystroke Tds Co");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
        window.Show();
        Pump(window);

        vm.ShowNatureOfPaymentMaster();
        Pump(window);
        Assert.Equal(Screen.NatureOfPaymentMaster, vm.CurrentScreen);
        Assert.Equal(8, vm.Company!.NaturesOfPayment.Count);

        window.KeyPressQwerty(PhysicalKey.U, RawInputModifiers.Control);
        Pump(window);

        Assert.Equal(11, vm.Company.NaturesOfPayment.Count);
        Assert.NotNull(vm.Company.FindNatureOfPaymentByCode("194T"));
    }

    /// <summary>
    /// 🔴 <b>Ctrl+U IS SCOPED TO THIS SCREEN AND MUST DO NOTHING ANYWHERE ELSE.</b> It is bound on a chord that
    /// was previously free across ~157 screens; a global binding would put a persisting mutation behind a stray
    /// keystroke on any of them. Pressed on the Gateway, nothing may change.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_U_does_nothing_on_any_other_screen()
    {
        var vm = LegacyBook("Scoped Chord Co");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
        window.Show();
        Pump(window);

        vm.ShowGateway();
        Pump(window);

        window.KeyPressQwerty(PhysicalKey.U, RawInputModifiers.Control);
        Pump(window);

        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        Assert.Equal(8, vm.Company!.NaturesOfPayment.Count);
    }

    /// <summary>
    /// 🔴 <b>THE LEGIBILITY INVARIANT FOR THE ACTION ROW, copied from the payroll-reports track because it
    /// caught two real truncation defects on its first run.</b> The row is
    /// <c>ColumnDefinitions="*,Auto,Auto"</c>: the two buttons take their natural width and the message takes
    /// the rest, so as soon as a second button was added the message column became the one that can be starved.
    /// Both button captions must render at their full desired width — a "Add Missing Predefine…" caption is a
    /// user who cannot tell what the button does — and the message must keep a usable share of the row.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1920, 1080)]
    [InlineData(1600, 900)]
    [InlineData(1366, 768)]
    [InlineData(1280, 720)]
    public void Both_action_buttons_render_uncropped_and_leave_the_message_room(double width, double height)
    {
        var vm = LegacyBook("Layout Tds Co " + Guid.NewGuid().ToString("N")[..8]);
        var window = new MainWindow { DataContext = vm, Width = width, Height = height };
        window.Show();
        Pump(window);

        vm.ShowNatureOfPaymentMaster();
        Pump(window);

        // Put a message on screen so the * column has something to compete with.
        var page = (NatureOfPaymentMasterViewModel)vm.NatureOfPaymentMaster!;
        page.AddMissingPredefined();
        Pump(window);

        var buttons = Descendants(window).OfType<Button>()
            .Where(b => b.IsEffectivelyVisible && b.Content is string)
            .ToDictionary(b => (string)b.Content!, b => b, StringComparer.Ordinal);

        foreach (var caption in new[] { "Create (Ctrl+A)", "Add Missing Predefined (Ctrl+U)" })
        {
            Assert.True(buttons.ContainsKey(caption),
                $"The '{caption}' button did not reach the visual tree at {width}x{height}. Realised captions: "
                + string.Join(" · ", buttons.Keys));

            var b = buttons[caption];
            Assert.True(b.Bounds.Width > 0 && b.Bounds.Height > 0,
                $"'{caption}' rendered at a degenerate size ({b.Bounds.Width:0.#}x{b.Bounds.Height:0.#}) at "
                + $"{width}x{height}.");
            // 🔴 DesiredSize INCLUDES the control's Margin; Bounds does NOT. Comparing them raw reported the
            // secondary button as 8px short at every viewport — which is exactly its Margin="0,0,8,0" and not a
            // crop at all. Measured, not assumed: without this term the assertion is off by the margin on any
            // control that has one, and it would have been "fixed" by widening a button that was already fine.
            var marginX = b.Margin.Left + b.Margin.Right;
            Assert.True(b.Bounds.Width + marginX + 0.5 >= b.DesiredSize.Width,
                $"'{caption}' was CROPPED at {width}x{height}: it wants {b.DesiredSize.Width:0.#}px (including "
                + $"{marginX:0.#}px of margin) and was given {b.Bounds.Width:0.#}px. A clipped caption on an "
                + "action button is a user who cannot tell what pressing it will do.");
        }

        var messageBlock = Descendants(window).OfType<TextBlock>()
            .FirstOrDefault(t => t.IsEffectivelyVisible && t.Text is { } s && s.Contains("predefined section"));

        Assert.True(messageBlock is not null,
            $"The result message did not render at {width}x{height}, so the operator gets no confirmation that "
            + "the sections were added.");
        Assert.True(messageBlock!.Bounds.Width > 40,
            $"The message column was starved to {messageBlock.Bounds.Width:0.#}px at {width}x{height} by the "
            + "two Auto button columns — the exact failure mode adding a second button introduces.");
    }

    // =====================================================================================================

    private MainWindowViewModel NewCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    /// <summary>A company with TDS on and the CURRENT seed — i.e. a book created today.</summary>
    private MainWindowViewModel TdsCompany()
    {
        var vm = NewCompany("Tds Reach Co " + Guid.NewGuid().ToString("N")[..8]);
        new TdsTcsService(vm.Company!).EnableTds(new TdsConfig { Tan = ValidTan });
        _storage.Save(vm.Company!);
        return vm;
    }

    /// <summary>
    /// 🔴 A book as it stood BEFORE census row 6.35 — TDS enabled and carrying only the Phase-7 eight.
    /// <para>Built by handing <c>EnableTds</c> a config that <b>already has</b> natures, which is exactly the
    /// branch a previously-enabled company takes on load: the seed is skipped, so the eight survive untouched
    /// and the three new ones never arrive. That is the situation this slice's top-up exists for, reproduced
    /// rather than described.</para>
    /// </summary>
    private MainWindowViewModel LegacyBook(string name)
    {
        var vm = NewCompany(name);

        var config = new TdsConfig { Tan = ValidTan };
        foreach (var seeded in SeedTdsTcsRates.BuildTdsDefaults()
                     .Where(n => LegacyEightSections.Contains(n.SectionCode, StringComparer.OrdinalIgnoreCase)))
            config.AddNatureOfPayment(seeded);

        Assert.Equal(LegacyEightSections.Length, config.NaturesOfPayment.Count);

        new TdsTcsService(vm.Company!).EnableTds(config);
        _storage.Save(vm.Company!);
        return vm;
    }

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    private static void ArrowToAndDrill(MainWindowViewModel vm, string label)
    {
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            if (vm.Menu[vm.SelectedIndex].Label == label) { vm.DrillIn(); return; }
            vm.MoveDown();
        }
        Assert.Fail($"'{label}' was not reachable by arrow navigation in the active column.");
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
}
