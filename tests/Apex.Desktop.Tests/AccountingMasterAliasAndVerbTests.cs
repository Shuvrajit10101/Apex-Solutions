using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Converters;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Census 2.3 (Ledger alias), 2.8 (Cost Centre alias), and the Create/Alter verb mismatch that touches 2.1,
/// 2.3, 2.7 and 2.8.</b> Every test here drives the REAL keyboard route through a realised
/// <see cref="MainWindow"/> and asserts either the RENDERED window or the RELOADED database — never a view-model
/// flag on its own.
///
/// <para><b>WHAT WAS ACTUALLY MISSING, measured before anything was written.</b> Neither alias needed storage and
/// neither needed a domain change. <see cref="Ledger.Alias"/> (<c>Ledger.cs:27</c>) and
/// <see cref="CostCentre.Alias"/> (<c>CostCentre.cs:29</c>) have existed all along, <c>ledgers.alias</c> and
/// <c>cost_centres.alias</c> are in the DDL (<c>Schema.cs:1076</c>, <c>:1474</c>), and
/// <c>SqliteCompanyStore</c> both reads and writes both columns. <b>The only missing link was the capture</b> —
/// and the enumeration that scheduled these rows asserted the opposite for the cost centre: the doc comment on
/// <c>CostMasterService.AlterCostCentre</c> said in shipped code that "alias is again absent from the domain
/// type", which is false and would have bought a migration this row never needed.</para>
///
/// <para><b>AND THE CAPTURE WAS WORTH SHIPPING BECAUSE TWO PRODUCTION READERS WERE ALREADY WAITING ON IT</b> —
/// this is not a field added so a census row could move. <see cref="Company.FindLedgerByName"/> and
/// <see cref="Company.FindCostCentreByName"/> both resolve on the alias as well as the name, and
/// <see cref="PickerDisplayTextConverter"/> returns <c>Name (Alias)</c> for an aliased ledger — a converter that
/// <c>PickerTextSearch.Register</c> binds to <c>TextSearch</c> on every <c>ComboBox</c> in the app, so the alias
/// becomes TYPE-AHEAD text and a ledger can be jumped to by typing it. Those paths are asserted below, not
/// assumed.</para>
///
/// <para>🔴 <b>AND THE LIMIT OF THAT CLAIM, measured rather than assumed:</b> the alias is <b>searchable, not
/// painted</b>, inside a dropdown. <c>PickerDisplayTextConverter</c>'s own remarks record that a picker's
/// <c>ItemTemplate</c> "does NOT participate — it only paints the row", and the ledger pickers' templates bind
/// <c>Name</c>. The alias IS painted on the Cost Centre master's list column, which is asserted; there is no
/// equivalent ledger list column, and none is claimed.</para>
///
/// <para><b>FIDELITY (R7; RULING 14), and the two halves are NOT the same grade.</b>
/// <list type="bullet">
///   <item><b>Cost Centre — VENDOR-ATTESTED, verbatim.</b> "Name &amp; alias: Provide a name. As in other
///     masters, you can specify multiple aliases."
///     (<c>help.tallysolutions.com/cost-centre-or-profit-centre-tally/</c>, read 2026-10-04). 🔴 The vendor
///     allows MULTIPLE aliases and this build stores ONE, because the column is a single <c>TEXT</c>. That limit
///     is OURS.</item>
///   <item><b>Ledger — OURS.</b> TallyPrime's own ledger page
///     (<c>help.tallysolutions.com/ledgers-in-tallyprime/</c>, read 2026-10-04) documents the ledger alias ONLY
///     as a <i>language</i> alias behind F12 "Provide language aliases for Name". The unconditional "enter the
///     alias of the ledger account if required" that the gap enumeration relied on is from the <b>Tally.ERP 9</b>
///     documentation — a different, older product that R7 does not make ground truth. A plain always-visible
///     ledger alias is therefore OUR divergence and is labelled so here and on the view model. It must not be
///     reported as vendor fidelity.</item>
///   <item><b>Captions and button labels — OURS.</b> No page states them.</item>
/// </list></para>
/// </summary>
public sealed class AccountingMasterAliasAndVerbTests
{
    // ======================================================================================= harness

    private static (MainWindow Window, MainWindowViewModel Vm, CompanyStorage Storage, string TempDir) NewWindow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexAcctAlias_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        return (window, vm, storage, tempDir);
    }

    private static void Pump(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Close(Window window, string dir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }

    private static IEnumerable<Avalonia.Visual> Descendants(Avalonia.Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    /// <summary>Every non-empty string the window is actually PAINTING, from both TextBlocks and the content of
    /// realised Buttons — a button label is the half an operator reads before committing.</summary>
    private static List<string> RenderedText(MainWindow window)
    {
        var all = Descendants(window).ToList();
        var fromBlocks = all.OfType<TextBlock>().Select(t => t.Text ?? string.Empty);
        var fromButtons = all.OfType<Button>().Select(b => b.Content as string ?? string.Empty);
        return fromBlocks.Concat(fromButtons).Where(s => s.Length > 0).ToList();
    }

    /// <summary>The live text of every realised TextBox, so a PRE-FILLED form field can be asserted as the
    /// operator sees it rather than through the property behind it.</summary>
    private static List<string> RenderedBoxText(MainWindow window) =>
        Descendants(window).OfType<TextBox>()
            .Select(t => t.Text ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList();

    private static void NewCompany(MainWindowViewModel vm, string name)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
    }

    /// <summary>Reloads the company from its <c>.db</c>, so an assertion is about STORED bytes and not about the
    /// in-memory graph the screen just mutated.</summary>
    private static Company Reload(CompanyStorage storage, string name)
    {
        var entry = storage.ListCompanies().Single(e =>
            string.Equals(e.Name, name, StringComparison.Ordinal));
        return storage.Load(entry);
    }

    // ============================================================= (a) census 2.8 — Cost Centre alias

    /// <summary>
    /// 🔴 <b>THE DRIVING TEST FOR ROW 2.8, and it reds on today's main at its first assertion.</b>
    ///
    /// <para><b>Keyboard route, walked end to end:</b> Gateway → Masters → Create → Cost Centre → type a name and
    /// an <b>Alias</b> → <b>Ctrl+A</b> creates. On main the Alias box does not exist, so
    /// <c>RenderedBoxText</c> can never contain the typed alias and the stored centre's
    /// <see cref="CostCentre.Alias"/> is null.</para>
    ///
    /// <para>The alias is asserted THREE ways because each one is a separately breakable link: on the RELOADED
    /// company (the column really round-trips), through
    /// <see cref="Company.FindCostCentreByName"/> (the production reader that was waiting on it), and in the
    /// RENDERED list column (the operator can see it without opening the master).</para>
    /// </summary>
    [AvaloniaFact]
    public void Cost_centre_alias_is_captured_rendered_persisted_and_resolvable()
    {
        var (window, vm, storage, dir) = NewWindow();
        try
        {
            NewCompany(vm, "CC Alias Co");

            vm.ShowCostCentreMaster();
            Pump(window);
            var master = vm.CostCentreMaster!;
            master.Name = "Southern Region";
            master.Alias = "SR";
            Pump(window);

            // The REALISED form is carrying the alias the operator typed — proving a bound box exists at all.
            Assert.Contains("SR", RenderedBoxText(window));

            Assert.True(master.Create(), master.Message);
            Pump(window);

            // (1) STORED. Reloaded from the .db, not read back off the live graph.
            var reloaded = Reload(storage, "CC Alias Co");
            var stored = reloaded.CostCentres.Single(c => c.Name == "Southern Region");
            Assert.Equal("SR", stored.Alias);

            // (2) The production READER that was waiting on the capture: resolution BY ALIAS.
            var byAlias = reloaded.FindCostCentreByName("SR");
            Assert.NotNull(byAlias);
            Assert.Equal(stored.Id, byAlias!.Id);

            // (3) RENDERED in the existing-centres list, so it is visible without altering the record.
            Assert.Contains(RenderedText(window), s => s == "SR");
            Assert.Contains(RenderedText(window), s => s == "Alias");
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>The ALTER half, driven by the real Ctrl+Enter route — and it covers the failure mode that matters
    /// more than the capture.</b> A screen that WRITES the alias but does not LOAD it blanks the alias of every
    /// centre altered for an unrelated reason. So this test alters only the PARENT and asserts the alias
    /// survived, then alters the alias itself and asserts the new value persisted.
    ///
    /// <para>Route: Cost Centre master → arrow onto the existing row → <b>Ctrl+Enter</b> (the shared
    /// <c>AlterHighlightedMasterListRow</c> arm) → <b>Ctrl+A</b> saves.</para>
    /// </summary>
    [AvaloniaFact]
    public void Altering_a_cost_centre_preserves_its_alias_and_can_change_it()
    {
        var (window, vm, storage, dir) = NewWindow();
        try
        {
            NewCompany(vm, "CC Alter Alias Co");

            var service = new CostMasterService(vm.Company!);
            var category = vm.Company!.CostCategories.First();
            vm.Company!.AddCostCentre(new CostCentre(
                Guid.NewGuid(), "Northern Region", category.Id, parentId: null, alias: "NR"));
            storage.Save(vm.Company!);

            vm.ShowCostCentreMaster();
            Pump(window);
            vm.CostCentreMaster!.MoveHighlight(1);
            Pump(window);
            Assert.NotNull(vm.CostCentreMaster!.HighlightedRow);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
            Pump(window);

            var altering = vm.CostCentreMaster!;
            Assert.True(altering.IsAltering, "Ctrl+Enter did not open the Cost Centre Alteration screen.");

            // PRE-FILLED, asserted on the realised box: on a build that writes but does not load the alias this
            // is the assertion that reds, and it is the one that catches the silent wipe.
            Assert.Equal("NR", altering.Alias);
            Assert.Contains("NR", RenderedBoxText(window));

            // Alter something ELSE entirely and save with the real chord.
            altering.Name = "Northern Region (Renamed)";
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);

            var afterRename = Reload(storage, "CC Alter Alias Co")
                .CostCentres.Single(c => c.Name == "Northern Region (Renamed)");
            Assert.Equal("NR", afterRename.Alias);

            // Now change the alias itself, through the same screen and chord.
            vm.CostCentreMaster!.Alias = "NORTH";
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);

            var afterAlias = Reload(storage, "CC Alter Alias Co")
                .CostCentres.Single(c => c.Name == "Northern Region (Renamed)");
            Assert.Equal("NORTH", afterAlias.Alias);

            // And the stale alias no longer resolves, which is what makes it a real lookup key rather than a
            // label that happens to be stored twice.
            Assert.Null(Reload(storage, "CC Alter Alias Co").FindCostCentreByName("NR"));
            Assert.NotNull(service);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// A blank alias box stores <c>null</c>, never <c>""</c>. Not pedantry: both
    /// <see cref="Company.FindCostCentreByName"/> and <see cref="Company.FindLedgerByName"/> guard their alias
    /// leg with <c>Alias is not null</c>, so an empty string would make a lookup for <c>""</c> match the first
    /// master ever saved through the screen.
    /// </summary>
    [AvaloniaFact]
    public void A_blank_alias_is_stored_as_null_and_cannot_be_matched_by_an_empty_search()
    {
        var (window, vm, storage, dir) = NewWindow();
        try
        {
            NewCompany(vm, "Blank Alias Co");

            vm.ShowCostCentreMaster();
            Pump(window);
            var master = vm.CostCentreMaster!;
            master.Name = "No Alias Centre";
            master.Alias = "   ";                 // whitespace only — the trap case
            Assert.True(master.Create(), master.Message);
            Pump(window);

            var reloaded = Reload(storage, "Blank Alias Co");
            var stored = reloaded.CostCentres.Single(c => c.Name == "No Alias Centre");
            Assert.Null(stored.Alias);
            Assert.Null(reloaded.FindCostCentreByName(string.Empty));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The alias clears with the name after a CREATE. Were it to persist on screen, the next centre entered in
    /// the same run would silently take the previous centre's alias — and because the alias resolves,
    /// <see cref="Company.FindCostCentreByName"/> would then return whichever of the two came first in company
    /// order.
    /// </summary>
    [AvaloniaFact]
    public void The_alias_box_clears_with_the_name_after_a_create()
    {
        var (window, vm, _, dir) = NewWindow();
        try
        {
            NewCompany(vm, "Alias Reset Co");

            vm.ShowCostCentreMaster();
            Pump(window);
            var master = vm.CostCentreMaster!;
            master.Name = "First Centre";
            master.Alias = "FC";
            Assert.True(master.Create(), master.Message);
            Pump(window);

            Assert.Equal(string.Empty, master.Name);
            Assert.Equal(string.Empty, master.Alias);
        }
        finally { Close(window, dir); }
    }

    // ================================================================ (b) census 2.3 — Ledger alias

    /// <summary>
    /// 🔴 <b>THE DRIVING TEST FOR ROW 2.3.</b> Route: Gateway → Masters → Create → Ledger → name, Under, and an
    /// <b>Alias</b> → Ctrl+A. Asserts the stored column, the resolution-by-alias reader, and the picker
    /// TYPE-AHEAD text — the last of which is the reason this capture is not a dead field: that converter is
    /// bound to <c>TextSearch</c> on every ComboBox in the app and no operator could produce an alias for it to
    /// match. (Search text, not the painted row — see the class remarks.)
    ///
    /// <para>🔴 <b>OURS, not vendor fidelity.</b> See the class remarks: TallyPrime documents the ledger alias
    /// only as an F12-gated LANGUAGE alias.</para>
    /// </summary>
    [AvaloniaFact]
    public void Ledger_alias_is_captured_persisted_resolvable_and_shown_in_every_picker()
    {
        var (window, vm, storage, dir) = NewWindow();
        try
        {
            NewCompany(vm, "Ledger Alias Co");

            vm.ShowLedgerMaster();
            Pump(window);
            var master = vm.LedgerMaster!;
            master.Name = "State Bank of India";
            master.SelectedGroup = master.Groups.First(g => g.Name == "Bank Accounts");
            master.Alias = "SBI";
            Pump(window);

            Assert.Contains("SBI", RenderedBoxText(window));

            Assert.True(master.Create(), master.Message);
            Pump(window);

            var reloaded = Reload(storage, "Ledger Alias Co");
            var stored = reloaded.FindLedgerByName("State Bank of India")!;
            Assert.Equal("SBI", stored.Alias);

            // The two production readers that were waiting on the capture.
            var byAlias = reloaded.FindLedgerByName("SBI");
            Assert.NotNull(byAlias);
            Assert.Equal(stored.Id, byAlias!.Id);
            Assert.Equal("State Bank of India (SBI)", PickerDisplayTextConverter.Resolve(stored));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE NON-WIPE TEST, and it is the sharper half of row 2.3.</b> <c>TryBuildInto</c> is the single
    /// mapping both Create and Alter run, and its own header warns that "a field omitted from it would silently
    /// WIPE that field the first time a user altered an unrelated one". The alias was on that method's
    /// explicit NOT-written list until this slice, so this test pins the new behaviour: alter the ledger's
    /// GROUP through the real route and assert the alias survived.
    ///
    /// <para>Route: Gateway → Chart of Accounts → arrow onto the ledger row → <b>Ctrl+A</b>
    /// (<c>AlterHighlightedChartRow</c>) opens Ledger Alteration → edit → Ctrl+A saves.</para>
    /// </summary>
    [AvaloniaFact]
    public void Altering_a_ledgers_group_preserves_its_alias()
    {
        var (window, vm, storage, dir) = NewWindow();
        try
        {
            NewCompany(vm, "Ledger Alias Alter Co");

            vm.ShowLedgerMaster();
            Pump(window);
            var create = vm.LedgerMaster!;
            create.Name = "Acme Traders";
            create.SelectedGroup = create.Groups.First(g => g.Name == "Sundry Debtors");
            create.Alias = "ACME";
            Assert.True(create.Create(), create.Message);
            Pump(window);

            var ledgerId = vm.Company!.FindLedgerByName("Acme Traders")!.Id;

            vm.ShowChartOfAccounts();
            Pump(window);
            vm.ShowLedgerAlter(ledgerId);
            Pump(window);

            var altering = vm.LedgerMaster!;
            Assert.True(altering.IsAltering, "The ledger did not open for alteration.");

            // PRE-FILLED — the load leg. Reds on a build that writes the alias but never loads it.
            Assert.Equal("ACME", altering.Alias);
            Assert.Contains("ACME", RenderedBoxText(window));

            // Change ONLY the group, and save with the real chord.
            altering.SelectedGroup = altering.Groups.First(g => g.Name == "Sundry Creditors");
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);

            var stored = Reload(storage, "Ledger Alias Alter Co").FindLedgerByName("Acme Traders")!;
            Assert.Equal("ACME", stored.Alias);
            Assert.Equal("Sundry Creditors",
                Reload(storage, "Ledger Alias Alter Co").FindGroup(stored.GroupId)!.Name);
        }
        finally { Close(window, dir); }
    }

    // ============================= (c) the Create/Alter verb mismatch — census 2.1, 2.3, 2.7, 2.8

    /// <summary>
    /// 🔴 <b>A CORRECTNESS DEFECT, NOT A LABEL: the pointer path on three accounting ALTERATION screens tried to
    /// CREATE.</b> <c>OnCreateLedgerClick</c>, <c>OnCreateCostCategoryClick</c> and
    /// <c>OnCreateCostCentreClick</c> each called <c>Create()</c> unconditionally while their Ctrl+A arms
    /// branched on <c>IsAltering</c> — so the button and the accelerator did two different things, which the
    /// account-group handler's own comment already forbids after the identical bug was fixed there. Clicking the
    /// button on an alteration screen failed on the master's own name ("a ledger named 'X' already exists")
    /// over a perfectly valid edit.
    ///
    /// <para>This test invokes the REAL realised Button, by name, exactly as a mouse would, and asserts the edit
    /// LANDED IN THE DATABASE. On today's main the click runs Create, the name clash is refused, and the
    /// reloaded ledger still carries the old alias.</para>
    /// </summary>
    [AvaloniaFact]
    public void Clicking_commit_on_the_ledger_alteration_screen_saves_instead_of_creating_a_duplicate()
    {
        var (window, vm, storage, dir) = NewWindow();
        try
        {
            NewCompany(vm, "Ledger Button Verb Co");

            vm.ShowLedgerMaster();
            Pump(window);
            var create = vm.LedgerMaster!;
            create.Name = "Globex Ltd";
            create.SelectedGroup = create.Groups.First(g => g.Name == "Sundry Debtors");
            create.Alias = "OLD";
            Assert.True(create.Create(), create.Message);
            Pump(window);

            var ledgerId = vm.Company!.FindLedgerByName("Globex Ltd")!.Id;
            vm.ShowLedgerAlter(ledgerId);
            Pump(window);
            Assert.True(vm.LedgerMaster!.IsAltering);

            vm.LedgerMaster!.Alias = "NEW";
            Pump(window);

            // The REAL button, found in the realised tree and fired through the SAME routed event its Click
            // handler is attached to — i.e. the pointer path, not a direct call to the view model.
            var button = Descendants(window).OfType<Button>()
                .Single(b => b.Name == "LedgerCommitButton");
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Pump(window);

            // 🔴 THE MUTATION-PROOF ASSERTION: the alteration reached the .db. On main the click ran Create(),
            // which refuses on the duplicate name, and this value is still "OLD".
            var stored = Reload(storage, "Ledger Button Verb Co").FindLedgerByName("Globex Ltd")!;
            Assert.Equal("NEW", stored.Alias);

            // And no second ledger was created by the click.
            Assert.Single(Reload(storage, "Ledger Button Verb Co")
                .Ledgers.Where(l => l.Name == "Globex Ltd"));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The same defect on the COST CENTRE, asserted the same way. Kept as its own test rather than folded into
    /// the ledger's, because the three handlers were three separate one-line methods and "the class of bug is
    /// fixed" has been claimed on this project from a single sample before.
    /// </summary>
    [AvaloniaFact]
    public void Clicking_commit_on_the_cost_centre_alteration_screen_saves_instead_of_creating_a_duplicate()
    {
        var (window, vm, storage, dir) = NewWindow();
        try
        {
            NewCompany(vm, "CC Button Verb Co");

            var category = vm.Company!.CostCategories.First();
            vm.Company!.AddCostCentre(new CostCentre(
                Guid.NewGuid(), "Western Region", category.Id, parentId: null, alias: "OLD"));
            storage.Save(vm.Company!);

            vm.ShowCostCentreMaster();
            Pump(window);
            vm.CostCentreMaster!.MoveHighlight(1);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
            Pump(window);
            Assert.True(vm.CostCentreMaster!.IsAltering);

            vm.CostCentreMaster!.Alias = "WR";
            Pump(window);

            var button = Descendants(window).OfType<Button>()
                .Single(b => b.Name == "CostCentreCommitButton");
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Pump(window);

            var stored = Reload(storage, "CC Button Verb Co")
                .CostCentres.Single(c => c.Name == "Western Region");
            Assert.Equal("WR", stored.Alias);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE RENDERED-WINDOW TEST FOR THE VERB LABELS.</b> Four accounting master pages printed the literal
    /// "<i>… Creation</i>" heading and a literal "<i>Create (Ctrl+A)</i>" button in BOTH modes, so an operator
    /// who pressed Ctrl+Enter to ALTER an existing master was told, by the two things they look at, that they
    /// were creating a new one. The Ledger and Group view models had no <c>Caption</c> property at all — and
    /// <c>LedgerMasterViewModel.IsAltering</c>'s own remark claimed the flag "drives the screen title/caption",
    /// which was false when it was written.
    ///
    /// <para>Asserted on the painted text of the realised window, both halves, and asserted NEGATIVELY too: the
    /// creation wording must be GONE, or a page that rendered both strings would pass on the positive alone.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_cost_centre_alteration_screen_renders_the_alteration_verb_in_heading_and_button()
    {
        var (window, vm, storage, dir) = NewWindow();
        try
        {
            NewCompany(vm, "CC Caption Co");

            var category = vm.Company!.CostCategories.First();
            vm.Company!.AddCostCentre(new CostCentre(Guid.NewGuid(), "Eastern Region", category.Id));
            storage.Save(vm.Company!);

            vm.ShowCostCentreMaster();
            Pump(window);

            // Create mode first — the baseline the alteration must DIFFER from.
            Assert.Contains(RenderedText(window), s => s == "Cost Centre Creation");
            Assert.Contains(RenderedText(window), s => s == "Create (Ctrl+A)");

            vm.CostCentreMaster!.MoveHighlight(1);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
            Pump(window);
            Assert.True(vm.CostCentreMaster!.IsAltering);

            var painted = RenderedText(window);
            Assert.Contains(painted, s => s == "Cost Centre Alteration");
            Assert.Contains(painted, s => s == "Save (Ctrl+A)");

            // NEGATIVE: the creation wording is gone from the page, not merely joined by the alteration wording.
            Assert.DoesNotContain(painted, s => s == "Cost Centre Creation");
            Assert.DoesNotContain(painted, s => s == "Create (Ctrl+A)");
        }
        finally { Close(window, dir); }
    }

    /// <summary>The Ledger page's half of the same label defect, driven through the Chart of Accounts route the
    /// operator actually uses to alter a ledger.</summary>
    [AvaloniaFact]
    public void The_ledger_alteration_screen_renders_the_alteration_verb_in_heading_and_button()
    {
        var (window, vm, _, dir) = NewWindow();
        try
        {
            NewCompany(vm, "Ledger Caption Co");

            vm.ShowLedgerMaster();
            Pump(window);
            Assert.Contains(RenderedText(window), s => s == "Ledger Creation");
            Assert.Contains(RenderedText(window), s => s == "Create (Ctrl+A)");

            var create = vm.LedgerMaster!;
            create.Name = "Initech";
            create.SelectedGroup = create.Groups.First(g => g.Name == "Sundry Debtors");
            Assert.True(create.Create(), create.Message);
            Pump(window);

            vm.ShowLedgerAlter(vm.Company!.FindLedgerByName("Initech")!.Id);
            Pump(window);

            var painted = RenderedText(window);
            Assert.Contains(painted, s => s == "Ledger Alteration");
            Assert.Contains(painted, s => s == "Save (Ctrl+A)");
            Assert.DoesNotContain(painted, s => s == "Ledger Creation");
            Assert.DoesNotContain(painted, s => s == "Create (Ctrl+A)");
        }
        finally { Close(window, dir); }
    }

    /// <summary>The accounting GROUP page's half (census 2.1). Its BUTTON BEHAVIOUR was fixed in an earlier
    /// slice; its heading and its button LABEL were not, which is how the omission survived — the alteration
    /// worked correctly while still announcing a creation.</summary>
    [AvaloniaFact]
    public void The_group_alteration_screen_renders_the_alteration_verb_in_heading_and_button()
    {
        var (window, vm, _, dir) = NewWindow();
        try
        {
            NewCompany(vm, "Group Caption Co");

            var group = new GroupService(vm.Company!).CreateGroup(
                "Field Expenses", vm.Company!.FindGroupByName("Indirect Expenses")!.Id);

            vm.ShowAccountGroupMaster();
            Pump(window);
            Assert.Contains(RenderedText(window), s => s == "Group Creation");

            vm.ShowAccountGroupAlter(group.Id);
            Pump(window);
            Assert.True(vm.AccountGroupMaster!.IsAltering);

            var painted = RenderedText(window);
            Assert.Contains(painted, s => s == "Group Alteration");
            Assert.Contains(painted, s => s == "Save (Ctrl+A)");
            Assert.DoesNotContain(painted, s => s == "Group Creation");
            Assert.DoesNotContain(painted, s => s == "Create (Ctrl+A)");
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The exported master list carries the alias too. A list screen whose EXPORT omits a column the screen
    /// displays has quietly become a different report from the one on screen — and this project ships the
    /// snapshot as the single source for every export format.
    /// </summary>
    [AvaloniaFact]
    public void The_cost_centre_master_list_export_carries_the_alias_column()
    {
        var (window, vm, storage, dir) = NewWindow();
        try
        {
            NewCompany(vm, "CC Export Co");

            var category = vm.Company!.CostCategories.First();
            vm.Company!.AddCostCentre(new CostCentre(
                Guid.NewGuid(), "Central Region", category.Id, parentId: null, alias: "CR"));
            storage.Save(vm.Company!);

            vm.ShowCostCentreMaster();
            Pump(window);

            var snapshot = vm.CostCentreMaster!.ToMasterListSnapshot();
            Assert.Contains(snapshot.Columns, c => c.Caption == "Alias");

            var aliasIndex = snapshot.Columns.ToList().FindIndex(c => c.Caption == "Alias");
            var row = snapshot.Rows.Single(r => r[0] == "Central Region");
            Assert.Equal("CR", row[aliasIndex]);
        }
        finally { Close(window, dir); }
    }
}
