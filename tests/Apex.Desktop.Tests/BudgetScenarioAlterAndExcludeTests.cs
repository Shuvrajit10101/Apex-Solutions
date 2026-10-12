using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>WAVE 46 / TRACK AF — THE <i>ALTER</i> VERB ON THE BUDGET (census 2.9) AND THE SCENARIO (census 2.10),
/// PLUS THE SCENARIO'S <i>EXCLUDE</i> TICK LIST.</b> Every test drives a realised <see cref="MainWindow"/> and
/// asserts either the RELOADED database or the RENDERED window — never a view-model flag on its own.
///
/// <para><b>THE SHARED GAP THESE TWO ROWS WERE HOLDING, AND IT WAS NOT A CODE GAP.</b> Both screens had Create
/// and Delete and no Alter, and the reason was written down in shipped source: the W33 C3 block in
/// <c>MainWindowViewModel.AlterHighlightedMasterListRow</c> says an alter verb is "only honest where a VENDOR
/// PAGE says what altering that master means", and on that ground "batch, BOM, budget, scenario and reorder keep
/// create+delete only and their census rows stay PARTIAL". The withholding was correct discipline. What was
/// missing was the <b>citation</b>, and two pages supply it.</para>
///
/// <para><b>FIDELITY (R7; USER RULING 14) — BOTH URLs OPENED AND READ BY CONTENT ON 2026-10-11.</b>
/// <list type="bullet">
///   <item><b>Budget — VENDOR-ATTESTED.</b> <c>help.tallysolutions.com/tally-prime/budgets-and-scenario/budgets-tally/</c>
///     carries the heading <i>"Alter a Budget"</i> and, verbatim: <i>"You can enable the facility to alter a
///     budget in TallyPrime and then make the necessary changes, as per your requirement"</i>; the modifiable
///     fields are <i>Name</i>, <i>Period of Budget</i> and <i>Set/Alter Budgets of</i>; and
///     <i>"As always press Ctrl+A to save the Budget Alteration screen"</i>. The same page's
///     <i>"Delete a Budget"</i> section gives <i>"Press Alt+D and press Y to delete"</i> and the rule
///     <i>"As a budget is only an estimation you can delete a budget without any dependency"</i> — which
///     independently vindicates this product's existing unguarded budget delete.</item>
///   <item><b>Scenario — VENDOR-ATTESTED.</b> <c>help.tallysolutions.com/tally-prime/financial-controlling/scenarios-tally/</c>:
///     <i>"Press Alt+G (Go To) &gt; Alter Master &gt; Scenario &gt; and press Enter"</i>, <i>"Select the Scenario
///     you want to alter from the List of Scenarios"</i>, <i>"From the Include and Exclude sections, select the
///     scenarios to alter from the List of Voucher Types"</i>, <i>"Modify the details as required and, as always,
///     accept the Scenario Alteration screen"</i>. The same page attests the Exclude field — <i>"Select the
///     voucher types to Exclude"</i> — and states its purpose: <i>"This is a unique option ensured by
///     TallyPrime which allows you to exclude some of the voucher types selected under the Include field, in case
///     you don't want those transactions to be reflected in your financial reports."</i></item>
/// </list></para>
///
/// <para><b>WHAT IS OURS AND IS LABELLED SO, not passed off as the vendor's:</b> the <b>route</b> (arrows +
/// Ctrl+Enter on the existing-list, this product's established shape rather than the vendor's Go-To menu); the
/// <b>refusal of a voucher type ticked both Include and Exclude</b>; the rule that <b>re-stating a budget target
/// overwrites its figure</b> rather than appending a second line; the <b>Excludes list column</b>; and the
/// <b>scenario DELETE verb</b>, which no vendor page documents.</para>
///
/// <para>🔴 <b>WHAT IS STILL OPEN, SO NEITHER ROW MAY BE READ AS COMPLETE.</b> 2.9 still has no <b>cost-centre</b>
/// budget target — the vendor's own alter sentence names <i>"groups, ledgers, or cost centres"</i> and
/// <c>budget_lines</c> has no <c>cost_centre_id</c>, so it needs a MIGRATION this track did not take (v66
/// ownership is an open user decision, T2-125) — and no way to REMOVE a line from the pending grid. 2.10 still
/// has no <b>"Exclude forex gain/loss calculations"</b> and no <b>"Exclude inventory tracking calculations"</b>,
/// both attested on the page above and both needing new <c>scenarios</c> columns, i.e. the same migration.
/// 🔴 And the census cell's <i>"no period"</i> gap on 2.10 is <b>MIS-SPECIFIED and nothing was built for it</b>:
/// the TallyPrime Scenario Creation screen has no period or date field at all (field list checked twice, by
/// content: Name, Include actuals, the two Exclude switches, Include, Exclude). A scenario period is a
/// Tally.ERP 9 artefact, which R7 does not make ground truth.</para>
///
/// <para>🔴 <b>EVERY TEST HERE FAILS ON <c>main</c> AT 0854ef5</b>, where <c>BudgetMasterViewModel.IsAltering</c>
/// and <c>ScenarioMasterViewModel.IsAltering</c> are the constant <c>false</c>, neither type has a
/// <c>ForAlter</c>, <c>ScenarioTypeRow</c> has no <c>Exclude</c>, and both Ctrl+A arms are a bare unconditional
/// <c>Create()</c>.</para>
/// </summary>
public sealed class BudgetScenarioAlterAndExcludeTests
{
    // ======================================================================================= harness

    private static (MainWindow Window, MainWindowViewModel Vm, CompanyStorage Storage, string Dir) NewWindow(
        string company)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexW46AF_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(dir);
        var vm = new MainWindowViewModel(storage);
        vm.NewCompanyName = company;
        vm.CreateCompany();
        vm.ShowGateway();

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Pump(window);
        return (window, vm, storage, dir);
    }

    private static void Pump(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
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
        catch (UnauthorizedAccessException) { /* best effort */ }
    }

    private static void Key(MainWindow window, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, mods);
        Pump(window);
    }

    private static void Accept(MainWindow window) => Key(window, PhysicalKey.A, RawInputModifiers.Control);

    private static void AlterHighlighted(MainWindow window) =>
        Key(window, PhysicalKey.Enter, RawInputModifiers.Control);

    private static IEnumerable<Avalonia.Visual> Descendants(Avalonia.Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    /// <summary>Every non-empty string the window is actually PAINTING — TextBlocks plus realised Button
    /// content, because the button label is the half an operator reads before committing.</summary>
    private static List<string> RenderedText(MainWindow window) =>
        Descendants(window)
            .Select(v => v switch
            {
                TextBlock t => t.Text ?? string.Empty,
                Button b => b.Content as string ?? string.Empty,
                _ => string.Empty,
            })
            .Where(s => s.Length > 0)
            .ToList();

    private static Button CommitButton(MainWindow window, string name) =>
        Descendants(window).OfType<Button>().Single(b => b.Name == name);

    /// <summary>Reloads the company from its own <c>.db</c>, through the same listing the company picker uses, so
    /// an assertion is about STORED bytes and not about the graph the screen just mutated.</summary>
    private static Company Reload(CompanyStorage storage, string name) =>
        storage.Load(storage.ListCompanies().Single(e => string.Equals(e.Name, name, StringComparison.Ordinal)));

    private static string? ActiveLabel(MainWindowViewModel vm) =>
        vm.Columns[vm.ActiveColumnIndex].Selected?.Label;

    private static void ArrowToAndEnter(MainWindow window, MainWindowViewModel vm, string label)
    {
        var rows = vm.Columns[vm.ActiveColumnIndex].Items.Count + 2;
        for (var i = 0; i < rows; i++)
        {
            if (ActiveLabel(vm) == label) { Key(window, PhysicalKey.Enter); return; }
            Key(window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"'{label}' was not reachable by arrow navigation from the active column.");
    }

    /// <summary>The real keyboard route: Gateway → Create → the named master.</summary>
    private static void OpenMaster(MainWindow window, MainWindowViewModel vm, string label, Screen expected)
    {
        ArrowToAndEnter(window, vm, "Create");
        ArrowToAndEnter(window, vm, label);
        Assert.Equal(expected, vm.CurrentScreen);
        Assert.NotNull(vm.MasterListScreen);
    }

    private static void ArrowToRow(MainWindow window, MainWindowViewModel vm, string name)
    {
        for (var i = 0; i < 60; i++)
        {
            if (vm.MasterListScreen?.HighlightedMasterRow?.MasterName == name) return;
            Key(window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"'{name}' was not reachable by arrow navigation on the existing-masters list.");
    }

    /// <summary>Seeds one budget through the product's own Create path, so the row the arrows later land on is a
    /// real row and not a hand-built aggregate the screen has never seen.</summary>
    private static Guid SeedBudget(
        MainWindow window, MainWindowViewModel vm, string name, string targetPrefix, string amount)
    {
        vm.ShowBudgetMaster();
        Pump(window);
        var create = vm.BudgetMaster!;
        create.Name = name;
        create.SelectedTarget = create.Targets.First(t => t.Display.StartsWith(targetPrefix, StringComparison.Ordinal));
        create.LineAmountText = amount;
        Assert.True(create.AddLine(), create.Message);
        Assert.True(create.Create(), create.Message);
        Pump(window);
        return vm.Company!.FindBudgetByName(name)!.Id;
    }

    // ==================================================================== (a) census 2.9 — BUDGET ALTERATION

    /// <summary>
    /// 🔴 <b>THE DRIVING TEST FOR THE BUDGET ALTER VERB, by keyboard only, asserted on the reloaded database.</b>
    /// Arrows to the budget on the existing-list → <b>Ctrl+Enter</b> opens <i>Budget Alteration</i> pre-filled →
    /// the name, both period dates and the budgeted figure are corrected → <b>Ctrl+A</b> saves.
    ///
    /// <para>The pre-fill assertions are not decoration. An alteration screen that opened BLANK would write a
    /// one-line budget over a many-line one and lose the rest without a word, which is the failure the pending
    /// grid's pre-load exists to prevent — so <c>PendingLines</c> carrying the existing line is asserted before
    /// anything is typed.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_budget_is_altered_with_CtrlEnter_then_CtrlA_and_the_name_period_and_figure_all_persist()
    {
        const string co = "W46 Budget Alter Co";
        var (window, vm, storage, dir) = NewWindow(co);
        try
        {
            var id = SeedBudget(window, vm, "Q1 Budjet", "Indirect Expenses", "50000");
            vm.ShowGateway();
            Pump(window);

            OpenMaster(window, vm, "Budget", Screen.BudgetMaster);
            ArrowToRow(window, vm, "Q1 Budjet");
            AlterHighlighted(window);

            var alter = vm.BudgetMaster!;
            Assert.True(alter.IsAltering, "Ctrl+Enter must open the budget for ALTERATION.");
            Assert.Equal("Budget Alteration", alter.Caption);
            Assert.Equal("Save (Ctrl+A)", alter.CommitLabel);

            // Pre-filled: the name, BOTH dates, and the existing line already in the pending grid.
            Assert.Equal("Q1 Budjet", alter.Name);
            Assert.False(string.IsNullOrWhiteSpace(alter.PeriodFromText));
            var line = Assert.Single(alter.PendingLines);
            Assert.StartsWith("Indirect Expenses", line.Target, StringComparison.Ordinal);

            // Correct all three things the vendor page says an alteration changes.
            alter.Name = "Q1 Budget";
            alter.PeriodFromText = "01-Apr-2024";
            alter.PeriodToText = "30-Jun-2024";
            alter.SelectedTarget = alter.Targets.First(t =>
                t.Display.StartsWith("Indirect Expenses", StringComparison.Ordinal));
            alter.LineAmountText = "65000";
            Assert.True(alter.AddLine(), alter.Message);

            Accept(window);

            // 🔴 OFF THE BOOK, not off the screen.
            var stored = Reload(storage, co);
            Assert.DoesNotContain(stored.Budgets, b => b.Name == "Q1 Budjet");
            var budget = stored.FindBudget(id);
            Assert.NotNull(budget);
            Assert.Equal("Q1 Budget", budget!.Name);
            Assert.Equal(new DateOnly(2024, 4, 1), budget.PeriodFrom);
            Assert.Equal(new DateOnly(2024, 6, 30), budget.PeriodTo);

            // ONE line at the NEW figure — re-stating the target corrected it rather than adding a second.
            var storedLine = Assert.Single(budget.Lines);
            Assert.Equal(65000m, storedLine.Amount.Amount);

            // And no second budget was conjured by the accept.
            Assert.Single(stored.Budgets.Where(b => b.Id == id));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE 3.2 / 3.5 DEMOTION SHAPE, PINNED.</b> Those two rows were demoted because an alteration
    /// screen's button ran a bare unconditional <c>Create()</c>, so a correction was lost or appended as a second
    /// version. The tell is the UNCHANGED name: <c>Create</c>'s uniqueness check is
    /// <c>FindBudgetByName(name) is not null</c>, which refuses the budget's own name — so an alteration that
    /// changes only the FIGURE cannot save at all. Accepting with the name untouched is therefore its own test.
    /// </summary>
    [AvaloniaFact]
    public void Altering_only_the_figure_and_accepting_the_unchanged_name_succeeds()
    {
        const string co = "W46 Budget Same Name Co";
        var (window, vm, storage, dir) = NewWindow(co);
        try
        {
            var id = SeedBudget(window, vm, "Annual", "Indirect Expenses", "10000");

            var alter = BudgetMasterViewModel.ForAlter(vm.Company!, storage, id, onChanged: () => { });
            Assert.NotNull(alter);
            Assert.True(alter!.IsAltering);

            alter.SelectedTarget = alter.Targets.First(t =>
                t.Display.StartsWith("Indirect Expenses", StringComparison.Ordinal));
            alter.LineAmountText = "12345";
            Assert.True(alter.AddLine(), alter.Message);

            Assert.True(alter.Alter(), alter.Message);          // name deliberately untouched
            // 🔴 The exact message Create() would have produced must NOT appear.
            Assert.DoesNotContain("already exists", alter.Message ?? string.Empty, StringComparison.Ordinal);

            var stored = Reload(storage, co).FindBudget(id)!;
            Assert.Equal("Annual", stored.Name);
            Assert.Equal(12345m, Assert.Single(stored.Lines).Amount.Amount);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>PROVE BY MUTATION THAT THE BUTTON RUNS THE VERB IT NAMES.</b> The real button is found in the
    /// realised visual tree and fired through the SAME routed event its Click handler is attached to — the
    /// pointer path, not a direct call. On main the handler was <c>=&gt; Vm?.BudgetMaster?.Create()</c>, which
    /// refuses on the duplicate name, so the stored figure would still be the original.
    /// </summary>
    [AvaloniaFact]
    public void Clicking_commit_on_the_budget_alteration_screen_saves_instead_of_creating_a_duplicate()
    {
        const string co = "W46 Budget Button Co";
        var (window, vm, storage, dir) = NewWindow(co);
        try
        {
            var id = SeedBudget(window, vm, "Capex", "Indirect Expenses", "1000");

            vm.ShowBudgetMaster();
            Pump(window);
            vm.BudgetMaster!.MoveHighlight(1);
            Pump(window);
            AlterHighlighted(window);
            Assert.True(vm.BudgetMaster!.IsAltering);

            // The painted label names the verb — a button still reading "Create" on an Alteration screen is the
            // defect's own signature.
            var button = CommitButton(window, "BudgetMasterCommit");
            Assert.Equal("Save (Ctrl+A)", button.Content as string);

            var alter = vm.BudgetMaster!;
            alter.SelectedTarget = alter.Targets.First(t =>
                t.Display.StartsWith("Indirect Expenses", StringComparison.Ordinal));
            alter.LineAmountText = "2000";
            Assert.True(alter.AddLine(), alter.Message);

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(window);

            var reloaded = Reload(storage, co);
            Assert.Equal(2000m, Assert.Single(reloaded.FindBudget(id)!.Lines).Amount.Amount);
            Assert.Single(reloaded.Budgets.Where(b => b.Name == "Capex"));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The overwrite rule on its own, because the arithmetic behind it is the reason it exists: two lines for the
    /// same target and measure would be summed by the variance projection and budget that target twice.
    /// </summary>
    [AvaloniaFact]
    public void Re_stating_a_target_overwrites_its_figure_instead_of_appending_a_second_line()
    {
        const string co = "W46 Budget Overwrite Co";
        var (window, vm, _, dir) = NewWindow(co);
        try
        {
            vm.ShowBudgetMaster();
            Pump(window);
            var m = vm.BudgetMaster!;
            var target = m.Targets.First(t => t.Display.StartsWith("Indirect Expenses", StringComparison.Ordinal));

            m.SelectedTarget = target;
            m.LineAmountText = "100";
            Assert.True(m.AddLine(), m.Message);
            m.SelectedTarget = target;
            m.LineAmountText = "250";
            Assert.True(m.AddLine(), m.Message);

            var only = Assert.Single(m.PendingLines);
            Assert.Equal(250m, only.Line.Amount.Amount);

            // A DIFFERENT measure on the same target is a different line and must still append.
            m.SelectedTarget = target;
            m.SelectedType = BudgetType.OnNettTransactions;
            m.LineAmountText = "900";
            Assert.True(m.AddLine(), m.Message);
            Assert.Equal(2, m.PendingLines.Count);
        }
        finally { Close(window, dir); }
    }

    // ================================================================== (b) census 2.10 — SCENARIO ALTERATION

    private static Guid SeedScenario(MainWindow window, MainWindowViewModel vm, string name)
    {
        vm.ShowScenarioMaster();
        Pump(window);
        var create = vm.ScenarioMaster!;
        create.Name = name;
        Assert.True(create.Types.Count(t => t.Include) >= 2,
            "The create defaults must pre-tick at least two types for the untick half of this test to mean anything.");
        Assert.True(create.Create(), create.Message);
        Pump(window);
        return vm.Company!.FindScenarioByName(name)!.Id;
    }

    /// <summary>
    /// 🔴 <b>THE DRIVING TEST FOR THE SCENARIO ALTER VERB.</b> Arrows → Ctrl+Enter → <i>Scenario Alteration</i>
    /// pre-filled with each type's state → one included type is UNTICKED and the name corrected → Ctrl+A.
    ///
    /// <para>The untick is the load-bearing half. Before this slice <c>Scenario</c>'s only mutators were
    /// <c>IncludeType</c> / <c>ExcludeType</c> and BOTH are add-only, so a voucher type could never be taken
    /// back out of a scenario — which is exactly the act the vendor's alteration page describes. A test that only
    /// renamed would have passed over that.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_scenario_is_altered_with_CtrlEnter_then_CtrlA_and_an_unticked_type_leaves_the_scenario()
    {
        const string co = "W46 Scenario Alter Co";
        var (window, vm, storage, dir) = NewWindow(co);
        try
        {
            var id = SeedScenario(window, vm, "Provisonal");
            var droppedId = Reload(storage, co).FindScenario(id)!.IncludedTypeIds.First();

            vm.ShowGateway();
            Pump(window);
            OpenMaster(window, vm, "Scenario", Screen.ScenarioMaster);
            ArrowToRow(window, vm, "Provisonal");
            AlterHighlighted(window);

            var alter = vm.ScenarioMaster!;
            Assert.True(alter.IsAltering, "Ctrl+Enter must open the scenario for ALTERATION.");
            Assert.Equal("Scenario Alteration", alter.Caption);
            Assert.Equal("Provisonal", alter.Name);

            // Pre-filled from the SAVED scenario, not from the create-mode pre-ticks.
            var dropped = alter.Types.Single(t => t.TypeId == droppedId);
            Assert.True(dropped.Include, "The alteration form must arrive carrying the saved Include state.");

            dropped.Include = false;
            alter.Name = "Provisional";
            Accept(window);

            var stored = Reload(storage, co).FindScenario(id);
            Assert.NotNull(stored);
            Assert.Equal("Provisional", stored!.Name);
            Assert.False(stored.Includes(droppedId),
                "An unticked voucher type must LEAVE the scenario — Scenario's old mutators were add-only.");
            Assert.NotEmpty(stored.IncludedTypeIds);
        }
        finally { Close(window, dir); }
    }

    /// <summary>The scenario half of the button-verb mutation proof. See the budget version for the shape.</summary>
    [AvaloniaFact]
    public void Clicking_commit_on_the_scenario_alteration_screen_saves_instead_of_creating_a_duplicate()
    {
        const string co = "W46 Scenario Button Co";
        var (window, vm, storage, dir) = NewWindow(co);
        try
        {
            var id = SeedScenario(window, vm, "WhatIf");

            vm.ShowScenarioMaster();
            Pump(window);
            vm.ScenarioMaster!.MoveHighlight(1);
            Pump(window);
            AlterHighlighted(window);
            Assert.True(vm.ScenarioMaster!.IsAltering);

            var button = CommitButton(window, "ScenarioMasterCommit");
            Assert.Equal("Save (Ctrl+A)", button.Content as string);

            vm.ScenarioMaster!.IncludeActuals = false;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(window);

            var reloaded = Reload(storage, co);
            Assert.False(reloaded.FindScenario(id)!.IncludeActuals);
            Assert.Single(reloaded.Scenarios.Where(s => s.Name == "WhatIf"));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE EXCLUDE TICK LIST — the vendor's own second field, which had NO Desktop caller at all.</b>
    /// <c>Scenario.ExcludeType</c> and the <c>is_included</c> column on <c>scenario_voucher_types</c> both
    /// existed; an exclusion could previously arrive only through import. Asserted on three observable artefacts:
    /// the reloaded aggregate, the master-list snapshot the CSV export is built from, and the painted window.
    /// </summary>
    [AvaloniaFact]
    public void A_scenario_exclusion_is_captured_on_create_and_reaches_the_db_the_export_and_the_window()
    {
        const string co = "W46 Scenario Exclude Co";
        var (window, vm, storage, dir) = NewWindow(co);
        try
        {
            vm.ShowScenarioMaster();
            Pump(window);
            var m = vm.ScenarioMaster!;
            m.Name = "Excl";

            var excluded = m.Types.First(t => !t.Include);
            excluded.Exclude = true;
            Assert.True(m.Create(), m.Message);
            Pump(window);

            // (1) the book
            var stored = Reload(storage, co).FindScenarioByName("Excl");
            Assert.NotNull(stored);
            Assert.True(stored!.Excludes(excluded.TypeId),
                "The ticked Exclude must reach scenario_voucher_types with is_included = 0.");
            Assert.False(stored.Includes(excluded.TypeId));

            // (2) the export source — the snapshot the CSV is written from carries an Excludes column.
            var snapshot = m.ToMasterListSnapshot();
            var excludesColumn = snapshot.Columns
                .Select((c, i) => (c, i))
                .Single(x => x.c.Caption == "Excludes").i;
            Assert.Contains(snapshot.Rows, r => r[excludesColumn].Contains(excluded.Name, StringComparison.Ordinal));

            // (3) the window — the header is painted, so the column is not an invisible data shape.
            Assert.Contains("Excludes", RenderedText(window));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>OURS, AND A REFUSAL RATHER THAN A SILENT PRECEDENCE RULE.</b> <see cref="Scenario"/>'s documented
    /// semantics give Exclude precedence, so a type ticked both ways WOULD resolve — to "excluded". Resolving it
    /// quietly is the defect: the operator asked for two contradictory things and the screen would honour one
    /// without saying which. The message must NAME the type, or it is not actionable.
    /// </summary>
    [AvaloniaFact]
    public void A_voucher_type_ticked_both_include_and_exclude_is_refused_by_name_on_both_verbs()
    {
        const string co = "W46 Scenario Conflict Co";
        var (window, vm, storage, dir) = NewWindow(co);
        try
        {
            vm.ShowScenarioMaster();
            Pump(window);
            var m = vm.ScenarioMaster!;
            m.Name = "Clash";
            var row = m.Types.First(t => t.Include);
            row.Exclude = true;

            Assert.False(m.Create(), "Create must refuse a type ticked Include AND Exclude.");
            Assert.Contains(row.Name, m.Message ?? string.Empty, StringComparison.Ordinal);
            Assert.Null(vm.Company!.FindScenarioByName("Clash"));

            // And the same refusal on the ALTER verb, so the two paths cannot drift.
            row.Exclude = false;
            Assert.True(m.Create(), m.Message);
            var id = vm.Company!.FindScenarioByName("Clash")!.Id;

            var alter = ScenarioMasterViewModel.ForAlter(vm.Company!, storage, id, onChanged: () => { })!;
            var clash = alter.Types.First(t => t.Include);
            clash.Exclude = true;
            Assert.False(alter.Alter(), "Alter must refuse the same contradiction.");
            Assert.Contains(clash.Name, alter.Message ?? string.Empty, StringComparison.Ordinal);
            Assert.False(Reload(storage, co).FindScenario(id)!.Excludes(clash.TypeId));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// Both headings are BOUND, not literals. The captions used to be hard-coded "Budget Creation" /
    /// "Scenario Creation" in <c>MainWindow.axaml</c>, so an alteration screen would have gone on announcing
    /// itself as a creation — the operator's only on-screen statement of which verb Ctrl+A will run.
    /// </summary>
    [AvaloniaFact]
    public void The_alteration_captions_are_painted_by_the_window_not_the_creation_literals()
    {
        const string co = "W46 Caption Co";
        var (window, vm, _, dir) = NewWindow(co);
        try
        {
            var budgetId = SeedBudget(window, vm, "Cap", "Indirect Expenses", "10");
            vm.ShowBudgetMaster();
            Pump(window);
            vm.BudgetMaster!.MoveHighlight(1);
            Pump(window);
            AlterHighlighted(window);
            Pump(window);

            Assert.Equal(budgetId, vm.Company!.FindBudgetByName("Cap")!.Id);
            Assert.Contains("Budget Alteration", RenderedText(window));
            Assert.DoesNotContain("Budget Creation", RenderedText(window));

            vm.ShowGateway();
            Pump(window);
            SeedScenario(window, vm, "CapS");
            vm.ShowScenarioMaster();
            Pump(window);
            vm.ScenarioMaster!.MoveHighlight(1);
            Pump(window);
            AlterHighlighted(window);
            Pump(window);

            Assert.Contains("Scenario Alteration", RenderedText(window));
            Assert.DoesNotContain("Scenario Creation", RenderedText(window));
        }
        finally { Close(window, dir); }
    }
}
