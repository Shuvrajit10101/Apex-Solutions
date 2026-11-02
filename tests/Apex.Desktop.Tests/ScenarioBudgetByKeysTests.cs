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
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>WAVE 10 / TRACK gb — CENSUS 2.10 (and the DELETE clause of 2.9) DRIVEN BY THE REAL KEYS ONLY.</b>
///
/// <para><b>WHY THIS FILE EXISTS.</b> The three clauses 2.10's cell still listed — exclusion capture, Alter and
/// Delete — were already built, but the proofs on main stopped short of the keyboard or of the disk:
/// <c>A_scenario_exclusion_is_captured_on_create_…</c> sets <c>ScenarioTypeRow.Exclude</c> on the view model
/// and calls <c>Create()</c> directly, <c>A_scenario_is_altered_…</c> unticks its row on the view model, and the
/// W33 C3 delete tests assert the in-memory company and then SAVE it themselves before any reload — which would
/// hide a delete arm that never persisted. Every test here moves focus with Tab, ticks with Space, commits with
/// Ctrl+A / Ctrl+Enter / Alt+D + Y, and reads the answer back from SQLite through a <b>fresh</b>
/// <see cref="CompanyStorage"/> that the test never saved through.</para>
///
/// <para><b>FIDELITY (R7; ruling 14).</b> <c>help.tallysolutions.com/tally-prime/financial-controlling/scenarios-tally/</c>
/// (now served at <c>help.tallysolutions.com/scenarios/</c>, "last updated October 15, 2026"), opened by content
/// 2026-11-02: <i>"Select the voucher types to Exclude."</i>, and <i>"You can alter the Scenarios whenever your
/// business requirements change, and delete them when they are no longer needed."</i> The budget page
/// (<c>help.tallysolutions.com/budgets-tally/</c>, same day) carries the same alter-and-delete sentence for
/// Budgets. Neither page names a key for either verb; the keys (Ctrl+Enter on the existing-list, Alt+D + Y) are
/// this product's established shape and are labelled OURS in <c>docs/invented-vs-cloned.md</c> §2Y.</para>
/// </summary>
public sealed class ScenarioBudgetByKeysTests
{
    // ======================================================================================= harness

    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewWindow(string company)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexW10GBK_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        vm.NewCompanyName = company;
        vm.CreateCompany();
        vm.ShowGateway();

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Pump(window);
        return (window, vm, dir);
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
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { /* best effort */ }
    }

    /// <summary>A whole keystroke — down AND up, as a physical key produces. A CheckBox toggles on Space's
    /// key-UP (Avalonia's ButtonBase), so a down-only press would never tick anything.</summary>
    private static void Key(MainWindow window, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, mods);
        Pump(window);
        window.KeyReleaseQwerty(key, mods);
        Pump(window);
    }

    private static void TypeText(MainWindow window, string text)
    {
        window.KeyTextInput(text);
        Pump(window);
    }

    private static IEnumerable<Avalonia.Visual> Descendants(Avalonia.Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    /// <summary>A FRESH store over the same folder — never the shell's handle, and never saved through first.</summary>
    private static Company ReloadFresh(string dir, string name)
    {
        SqliteConnection.ClearAllPools();
        var fresh = new CompanyStorage(dir);
        return fresh.Load(fresh.ListCompanies().Single(e => string.Equals(e.Name, name, StringComparison.Ordinal)));
    }

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

    /// <summary>Gateway → Create → the named master, by keys.</summary>
    private static void OpenMaster(MainWindow window, MainWindowViewModel vm, string label, Screen expected)
    {
        vm.ShowGateway();
        Pump(window);
        ArrowToAndEnter(window, vm, "Create");
        ArrowToAndEnter(window, vm, label);
        Assert.Equal(expected, vm.CurrentScreen);
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

    private static TextBox ScenarioNameBox(MainWindow window) =>
        Descendants(window).OfType<TextBox>().Single(t => t.PlaceholderText == "e.g. Provisional");

    /// <summary>Focuses the realised scenario Name box and types <paramref name="name"/> into it.</summary>
    private static void TypeScenarioName(MainWindow window, string name)
    {
        var box = ScenarioNameBox(window);
        box.Focus();
        Pump(window);
        box.SelectAll();
        TypeText(window, name);
        Assert.Equal(name, box.Text);
    }

    /// <summary>
    /// TABs forward from the current focus until focus lands on the Include (<paramref name="exclude"/> false) or
    /// Exclude (true) tick of the row for <paramref name="typeId"/>, then presses SPACE on it. The column is read
    /// off the realised CheckBox's <c>Grid.Column</c> (0 = In, 1 = Ex), the row off its DataContext.
    /// </summary>
    private static void TabToTickAndSpace(MainWindow window, Guid typeId, bool exclude)
    {
        var wantedColumn = exclude ? 1 : 0;
        for (var i = 0; i < 200; i++)
        {
            if (window.FocusManager?.GetFocusedElement() is CheckBox cb
                && cb.DataContext is ScenarioTypeRow row && row.TypeId == typeId
                && Grid.GetColumn(cb) == wantedColumn)
            {
                Key(window, PhysicalKey.Space);
                return;
            }
            Key(window, PhysicalKey.Tab);
        }
        Assert.Fail($"Tab never reached the {(exclude ? "Exclude" : "Include")} tick of voucher type {typeId}.");
    }

    // ============================================================== 2.10 — exclusion capture

    /// <summary>
    /// 🔴 <b>THE EXCLUDE TICK, BY KEYS.</b> Name → Tab to a voucher type's <i>Ex</i> tick → Space → Ctrl+A. The
    /// reloaded book must carry that type as EXCLUDED (<c>is_included = 0</c>), and the realised CheckBox must have
    /// been checked by the Space before the save — so the keyboard route, not a view-model write, did it.
    /// </summary>
    [AvaloniaFact]
    public void A_scenario_exclusion_ticked_with_Tab_and_Space_is_stored_as_excluded()
    {
        const string co = "W10 Scenario Exclude Keys Co";
        var (window, vm, dir) = NewWindow(co);
        try
        {
            OpenMaster(window, vm, "Scenario", Screen.ScenarioMaster);
            var m = vm.ScenarioMaster!;
            Assert.False(m.IsAltering);

            var target = m.Types.First(t => !t.Include && !t.Exclude);
            TypeScenarioName(window, "Excluded By Keys");
            TabToTickAndSpace(window, target.TypeId, exclude: true);

            var tick = (CheckBox)window.FocusManager!.GetFocusedElement()!;
            Assert.True(tick.IsChecked, "Space on the focused Exclude tick must check it.");

            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("created", m.Message);

            var stored = ReloadFresh(dir, co).FindScenarioByName("Excluded By Keys");
            Assert.NotNull(stored);
            Assert.True(stored!.Excludes(target.TypeId), "The Space-ticked type must be stored EXCLUDED.");
            Assert.False(stored.Includes(target.TypeId));
            Assert.NotEmpty(stored.IncludedTypeIds);
        }
        finally { Close(window, dir); }
    }

    // ============================================================================ 2.10 — alter

    /// <summary>
    /// <b>SCENARIO ALTERATION, BY KEYS.</b> Ctrl+Enter on the highlighted scenario opens <i>Scenario Alteration</i>
    /// pre-filled; Tab to an included type's <i>In</i> tick and Space UNTICKS it, Tab on to another type's
    /// <i>Ex</i> tick and Space ticks it, the name is retyped, Ctrl+A saves. All three changes must be on the
    /// reloaded book, under the SAME id (an alter, not a duplicate create).
    /// </summary>
    [AvaloniaFact]
    public void A_scenario_altered_by_keys_persists_its_rename_untick_and_new_exclusion()
    {
        const string co = "W10 Scenario Alter Keys Co";
        var (window, vm, dir) = NewWindow(co);
        try
        {
            OpenMaster(window, vm, "Scenario", Screen.ScenarioMaster);
            TypeScenarioName(window, "Draft Plan");
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            var before = ReloadFresh(dir, co).FindScenarioByName("Draft Plan");
            Assert.NotNull(before);
            Assert.True(before!.IncludedTypeIds.Count >= 2, "The create defaults must pre-tick at least two types.");

            OpenMaster(window, vm, "Scenario", Screen.ScenarioMaster);
            ArrowToRow(window, vm, "Draft Plan");
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            var alter = vm.ScenarioMaster!;
            Assert.True(alter.IsAltering, "Ctrl+Enter must open the scenario for ALTERATION.");

            // Untick the first included type that is on screen; exclude the first type that is in neither set.
            var drop = alter.Types.First(t => t.Include);
            var newlyExcluded = alter.Types.First(t => !t.Include && !t.Exclude);
            var ordered = alter.Types.ToList();

            TypeScenarioName(window, "Final Plan");
            // Tab order is row order, so visit whichever row comes first.
            if (ordered.IndexOf(drop) < ordered.IndexOf(newlyExcluded))
            {
                TabToTickAndSpace(window, drop.TypeId, exclude: false);
                TabToTickAndSpace(window, newlyExcluded.TypeId, exclude: true);
            }
            else
            {
                TabToTickAndSpace(window, newlyExcluded.TypeId, exclude: true);
                TabToTickAndSpace(window, drop.TypeId, exclude: false);
            }

            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("altered", alter.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);

            var after = ReloadFresh(dir, co);
            var stored = after.FindScenario(before.Id);
            Assert.NotNull(stored);
            Assert.Equal("Final Plan", stored!.Name);
            Assert.Null(after.FindScenarioByName("Draft Plan"));
            Assert.Single(after.Scenarios);
            Assert.False(stored.Includes(drop.TypeId), "The Space-unticked type must leave the scenario.");
            Assert.True(stored.Excludes(newlyExcluded.TypeId), "The Space-ticked Exclude must be stored.");
        }
        finally { Close(window, dir); }
    }

    // =========================================================================== delete (2.10 + 2.9)

    /// <summary>
    /// <b>SCENARIO DELETE, BY KEYS, PROVEN ON DISK.</b> Alt+D then Y on the highlighted scenario. The reloaded book
    /// — through a store the test never saved through — must not hold it, and a sibling must survive.
    /// </summary>
    [AvaloniaFact]
    public void A_scenario_deleted_with_AltD_then_Y_is_gone_from_the_reloaded_book()
    {
        const string co = "W10 Scenario Delete Keys Co";
        var (window, vm, dir) = NewWindow(co);
        try
        {
            OpenMaster(window, vm, "Scenario", Screen.ScenarioMaster);
            TypeScenarioName(window, "Doomed");
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            TypeScenarioName(window, "Kept");
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Equal(2, ReloadFresh(dir, co).Scenarios.Count);

            OpenMaster(window, vm, "Scenario", Screen.ScenarioMaster);
            ArrowToRow(window, vm, "Doomed");
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);
            Assert.Contains("deleted", vm.Notice ?? string.Empty);

            var reloaded = ReloadFresh(dir, co);
            Assert.Null(reloaded.FindScenarioByName("Doomed"));
            Assert.NotNull(reloaded.FindScenarioByName("Kept"));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// <b>BUDGET DELETE, BY KEYS, PROVEN ON DISK</b> (2.9's Delete clause). The W33 C3 test saves the company
    /// itself before checking; this one reads a fresh store only.
    /// </summary>
    [AvaloniaFact]
    public void A_budget_deleted_with_AltD_then_Y_is_gone_from_the_reloaded_book()
    {
        const string co = "W10 Budget Delete Keys Co";
        var (window, vm, dir) = NewWindow(co);
        try
        {
            OpenMaster(window, vm, "Budget", Screen.BudgetMaster);
            foreach (var name in new[] { "Doomed Budget", "Kept Budget" })
            {
                var m = vm.BudgetMaster!;
                m.Name = name;
                m.SelectedTarget = m.Targets.First(t => t.Display.StartsWith("Indirect Expenses", StringComparison.Ordinal));
                m.LineAmountText = "100.01";
                Assert.True(m.AddLine(), m.Message);
                Key(window, PhysicalKey.A, RawInputModifiers.Control);
                Assert.Contains("created", m.Message);
            }
            Assert.Equal(2, ReloadFresh(dir, co).Budgets.Count);

            OpenMaster(window, vm, "Budget", Screen.BudgetMaster);
            ArrowToRow(window, vm, "Doomed Budget");
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);
            Assert.Contains("deleted", vm.Notice ?? string.Empty);

            var reloaded = ReloadFresh(dir, co);
            Assert.Null(reloaded.FindBudgetByName("Doomed Budget"));
            Assert.NotNull(reloaded.FindBudgetByName("Kept Budget"));
        }
        finally { Close(window, dir); }
    }
}
