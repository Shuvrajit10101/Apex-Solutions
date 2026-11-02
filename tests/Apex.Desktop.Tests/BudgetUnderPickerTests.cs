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
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>WAVE 10 / TRACK gb — THE BUDGET'S <i>UNDER</i> PICKER (census 2.9).</b>
///
/// <para><b>THE GAP.</b> <c>Budget.UnderId</c> had a column (<c>budgets.under_id REFERENCES groups(id)</c>), a
/// round trip and a group-delete guard (<c>MasterDeletionRules.EnsureGroupDeletable</c> counts
/// <c>budgets.under_id</c>), but no screen could set it — so every budget an operator created was Primary, and
/// the guard had nothing a user could ever trip.</para>
///
/// <para><b>FIDELITY (R7; ruling 14).</b> <c>help.tallysolutions.com/budgets-tally/</c>, opened by content
/// 2026-11-02, step 3 of both "Create Budget for Ledgers" and "Create Budget for Cost Centres":
/// <i>"Under: Select Primary or any other predefined groups, based on your requirement."</i></para>
///
/// <para>Every test drives a realised <see cref="MainWindow"/> through the real key handlers (Tab from the Name
/// box into the picker, type-to-jump, Ctrl+A, Ctrl+Enter, Alt+D) and asserts the value re-read from SQLite
/// through a FRESH <see cref="CompanyStorage"/> — or the raw <c>budgets.under_id</c> column — never the view
/// model it was typed into.</para>
/// </summary>
public sealed class BudgetUnderPickerTests
{
    // ======================================================================================= harness

    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewWindow(string company)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexW10GB_" + Guid.NewGuid().ToString("N"));
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

    private static void Key(MainWindow window, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, mods);
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

    private static ComboBox UnderPicker(MainWindow window) =>
        Descendants(window).OfType<ComboBox>().Single(c => c.Name == "BudgetUnderPicker");

    private static TextBox BudgetNameBox(MainWindow window) =>
        Descendants(window).OfType<TextBox>().Single(t => t.PlaceholderText == "e.g. Q1 Budget");

    /// <summary>A FRESH store over the same folder — nothing cached from the shell's own handle.</summary>
    private static Company ReloadFresh(string dir, string name)
    {
        SqliteConnection.ClearAllPools();
        var fresh = new CompanyStorage(dir);
        return fresh.Load(fresh.ListCompanies().Single(e => string.Equals(e.Name, name, StringComparison.Ordinal)));
    }

    /// <summary>The raw stored column, read with plain SQL off the company's <c>.db</c>.</summary>
    private static string? RawUnderId(string dir, string company, string budget)
    {
        SqliteConnection.ClearAllPools();
        var entry = new CompanyStorage(dir).ListCompanies().Single(e => e.Name == company);
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = entry.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
        }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT under_id FROM budgets WHERE name = $n";
        cmd.Parameters.AddWithValue("$n", budget);
        var value = cmd.ExecuteScalar();
        Assert.NotNull(value);   // the row exists
        return value is DBNull ? null : (string)value!;
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

    /// <summary>Gateway → Create → Budget, by keys.</summary>
    private static void OpenBudgetMaster(MainWindow window, MainWindowViewModel vm)
    {
        vm.ShowGateway();
        Pump(window);
        ArrowToAndEnter(window, vm, "Create");
        ArrowToAndEnter(window, vm, "Budget");
        Assert.Equal(Screen.BudgetMaster, vm.CurrentScreen);
        Assert.NotNull(vm.BudgetMaster);
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

    /// <summary>
    /// Types the name into the realised Name box, TABs into the Under picker (asserting focus actually lands
    /// there), and type-to-jumps to <paramref name="underPrefix"/> (null = leave the default).
    /// </summary>
    private static void TypeNameAndUnder(MainWindow window, string name, string? underPrefix)
    {
        var nameBox = BudgetNameBox(window);
        nameBox.Focus();
        Pump(window);
        nameBox.Text = string.Empty;
        TypeText(window, name);
        Assert.Equal(name, nameBox.Text);

        Key(window, PhysicalKey.Tab);
        var picker = UnderPicker(window);
        Assert.True(ReferenceEquals(window.FocusManager?.GetFocusedElement(), picker),
            "Tab from the budget Name box must land on the Under picker.");

        if (underPrefix is not null) TypeText(window, underPrefix);
    }

    private static void AddIndirectExpensesLine(BudgetMasterViewModel m, string amount)
    {
        m.SelectedTarget = m.Targets.First(t => t.Display.StartsWith("Indirect Expenses", StringComparison.Ordinal));
        m.LineAmountText = amount;
        Assert.True(m.AddLine(), m.Message);
    }

    // ============================================================================== create

    /// <summary>
    /// 🔴 <b>THE DRIVING TEST.</b> Name → Tab → type "Indirect" on the Under picker → a line → Ctrl+A. The
    /// group id must be the stored <c>budgets.under_id</c>, both off a fresh store and off the raw column, and the
    /// rendered picker must have shown the group before the save.
    /// </summary>
    [AvaloniaFact]
    public void A_budget_created_under_a_group_by_keys_stores_that_group_in_under_id()
    {
        const string co = "W10 Budget Under Co";
        var (window, vm, dir) = NewWindow(co);
        try
        {
            OpenBudgetMaster(window, vm);
            var m = vm.BudgetMaster!;

            // The picker defaults to Primary.
            Assert.Equal("Primary", (UnderPicker(window).SelectedItem as BudgetUnderOption)?.Name);

            TypeNameAndUnder(window, "Admin Budget", "Indirect");
            Assert.Equal("Indirect Expenses", (UnderPicker(window).SelectedItem as BudgetUnderOption)?.Name);

            AddIndirectExpensesLine(m, "12345.67");
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("created", m.Message);

            var reloaded = ReloadFresh(dir, co);
            var group = reloaded.FindGroupByName("Indirect Expenses")!;
            var budget = reloaded.FindBudgetByName("Admin Budget")!;
            Assert.Equal(group.Id, budget.UnderId);
            Assert.Equal(group.Id.ToString(), RawUnderId(dir, co, "Admin Budget"), ignoreCase: true);

            // The form resets to Primary for the next budget.
            Assert.Equal("Primary", m.SelectedUnder?.Name);
        }
        finally { Close(window, dir); }
    }

    /// <summary>Leaving the picker on <b>Primary</b> stores SQL NULL — Primary is "no parent", not a group.</summary>
    [AvaloniaFact]
    public void A_budget_left_on_Primary_stores_null_under_id()
    {
        const string co = "W10 Budget Primary Co";
        var (window, vm, dir) = NewWindow(co);
        try
        {
            OpenBudgetMaster(window, vm);
            TypeNameAndUnder(window, "Top Budget", underPrefix: null);
            AddIndirectExpensesLine(vm.BudgetMaster!, "100");
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            Assert.Null(ReloadFresh(dir, co).FindBudgetByName("Top Budget")!.UnderId);
            Assert.Null(RawUnderId(dir, co, "Top Budget"));
        }
        finally { Close(window, dir); }
    }

    // =============================================================================== alter

    /// <summary>
    /// Alter: Ctrl+Enter on the existing-list opens Budget Alteration with the stored group PRE-SELECTED in the
    /// rendered picker; typing "Primary" re-files it at the top level, and Ctrl+A writes NULL back.
    /// </summary>
    [AvaloniaFact]
    public void Altering_a_budget_prefills_its_group_and_moving_it_to_Primary_writes_null()
    {
        const string co = "W10 Budget Under Alter Co";
        var (window, vm, dir) = NewWindow(co);
        try
        {
            OpenBudgetMaster(window, vm);
            TypeNameAndUnder(window, "Moving Budget", "Indirect");
            AddIndirectExpensesLine(vm.BudgetMaster!, "500");
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.NotNull(ReloadFresh(dir, co).FindBudgetByName("Moving Budget")!.UnderId);

            OpenBudgetMaster(window, vm);
            ArrowToRow(window, vm, "Moving Budget");
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            var alter = vm.BudgetMaster!;
            Assert.True(alter.IsAltering);

            // PRE-FILLED in the realised control — reds on a build that saves the field but never loads it.
            Assert.Equal("Indirect Expenses", (UnderPicker(window).SelectedItem as BudgetUnderOption)?.Name);

            TypeNameAndUnder(window, "Moving Budget", "Primary");
            Assert.Equal("Primary", (UnderPicker(window).SelectedItem as BudgetUnderOption)?.Name);
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("altered", alter.Message);

            Assert.Null(ReloadFresh(dir, co).FindBudgetByName("Moving Budget")!.UnderId);
            Assert.Null(RawUnderId(dir, co, "Moving Budget"));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE NON-WIPE TEST.</b> Altering ONLY the figure must keep the budget under its group. An Alter that
    /// forgot to pre-select the stored group would re-file it at Primary the first time anyone corrected an
    /// amount.
    /// </summary>
    [AvaloniaFact]
    public void Altering_only_the_figure_keeps_the_budget_under_its_group()
    {
        const string co = "W10 Budget Under Keep Co";
        var (window, vm, dir) = NewWindow(co);
        try
        {
            OpenBudgetMaster(window, vm);
            TypeNameAndUnder(window, "Kept Budget", "Indirect");
            AddIndirectExpensesLine(vm.BudgetMaster!, "500");
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            OpenBudgetMaster(window, vm);
            ArrowToRow(window, vm, "Kept Budget");
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            var alter = vm.BudgetMaster!;
            AddIndirectExpensesLine(alter, "777.77");
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("altered", alter.Message);

            var reloaded = ReloadFresh(dir, co);
            var budget = reloaded.FindBudgetByName("Kept Budget")!;
            Assert.Equal(777.77m, Assert.Single(budget.Lines).Amount.Amount);
            Assert.Equal(reloaded.FindGroupByName("Indirect Expenses")!.Id, budget.UnderId);
        }
        finally { Close(window, dir); }
    }

    // ========================================================================= delete guard

    /// <summary>
    /// A custom group with a budget filed under it (set through the picker) is REFUSED on Alt+D from the Chart of
    /// Accounts, by name and count, and the group survives on the reloaded book. Before the picker this guard
    /// branch was unreachable by any user.
    /// </summary>
    [AvaloniaFact]
    public void Deleting_a_group_a_budget_is_filed_under_is_refused_with_the_budget_count()
    {
        const string co = "W10 Budget Under Guard Co";
        var (window, vm, dir) = NewWindow(co);
        try
        {
            var c = vm.Company!;
            var custom = new GroupService(c).CreateGroup("Marketing Spend", c.FindGroupByName("Indirect Expenses")!.Id);
            new CompanyStorage(dir).Save(c);

            OpenBudgetMaster(window, vm);
            TypeNameAndUnder(window, "Campaign Budget", "Marketing");
            Assert.Equal("Marketing Spend", (UnderPicker(window).SelectedItem as BudgetUnderOption)?.Name);
            AddIndirectExpensesLine(vm.BudgetMaster!, "2500");
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Equal(custom.Id, ReloadFresh(dir, co).FindBudgetByName("Campaign Budget")!.UnderId);

            vm.ShowChartOfAccounts();
            Pump(window);
            var chart = vm.ChartOfAccounts!;
            chart.HighlightedIndex = chart.Rows.ToList().FindIndex(r => r.GroupId == custom.Id);
            Assert.True(chart.HighlightedIndex >= 0);

            Key(window, PhysicalKey.D, RawInputModifiers.Alt);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.Contains("1 master is filed under it", vm.Notice);
            Assert.Contains("1 budget", vm.Notice);
            Assert.NotNull(ReloadFresh(dir, co).FindGroup(custom.Id));
        }
        finally { Close(window, dir); }
    }
}
