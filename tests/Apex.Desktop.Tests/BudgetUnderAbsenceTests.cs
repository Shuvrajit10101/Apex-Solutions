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
/// 🔴 <b>WAVE 10 / TRACK gb — THE BUDGET HAS NO GROUP "UNDER" PICKER, AND THAT IS PINNED (census 2.9).</b>
///
/// <para><b>WHY.</b> A first cut of this track offered every ACCOUNT GROUP as a budget's <i>Under</i>. The
/// vendor shape is a parent <b>BUDGET</b>: the November 3, 2025 version of
/// <c>help.tallysolutions.com/budgets-tally/</c> (web.archive.org snapshot 20260310093634, read by content
/// 2026-11-02) says <i>"you need to create the budget under Primary from the List of Budgets"</i> and then
/// <i>"Sales Department Budget … under the main Department Budget. This shows the budget structure in a
/// hierarchical form."</i> <c>budgets.under_id</c> REFERENCES groups(id), so the group picker wrote a value with
/// no downstream effect except making the chosen group undeletable
/// (<c>MasterDeletionRules.EnsureGroupDeletable</c> counts budgets filed under a group). The A12 decision was to
/// remove the control; the parent-budget model needs a migration and is OPEN (docs/invented-vs-cloned.md
/// IV-300).</para>
///
/// <para><b>WHAT REDDENS.</b> Restoring the group picker (Tab from Name lands on it, type-to-jump picks a group)
/// reddens both tests: the first on the Tab target and the realised "Under" label, the second on the stored
/// <c>budgets.under_id</c> and on the group-delete refusal it brings back.</para>
/// </summary>
public sealed class BudgetUnderAbsenceTests
{
    // ======================================================================================= harness

    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewWindow(string company)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexW10GBU_" + Guid.NewGuid().ToString("N"));
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
    private static BudgetMasterViewModel OpenBudgetMaster(MainWindow window, MainWindowViewModel vm)
    {
        vm.ShowGateway();
        Pump(window);
        ArrowToAndEnter(window, vm, "Create");
        ArrowToAndEnter(window, vm, "Budget");
        Assert.Equal(Screen.BudgetMaster, vm.CurrentScreen);
        Assert.NotNull(vm.BudgetMaster);
        return vm.BudgetMaster!;
    }

    private static TextBox BudgetNameBox(MainWindow window) =>
        Descendants(window).OfType<TextBox>().Single(t => t.PlaceholderText == "e.g. Q1 Budget");

    /// <summary>Every realised control on the Budget master pane (its DataContext is the master's view model).</summary>
    private static List<Control> BudgetPaneControls(MainWindow window, BudgetMasterViewModel m) =>
        Descendants(window).OfType<Control>().Where(c => ReferenceEquals(c.DataContext, m)).ToList();

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

    // ============================================================================== the pane

    /// <summary>
    /// The realised Budget master has no <i>Under</i> control: Tab from the Name box lands on the From date, no
    /// "Under" label is painted on the pane, and no control named <c>BudgetUnderPicker</c> exists.
    /// </summary>
    [AvaloniaFact]
    public void Tab_from_the_budget_Name_box_lands_on_the_From_date_and_no_Under_control_is_realised()
    {
        var (window, vm, dir) = NewWindow("W10 Budget No Under Co");
        try
        {
            var m = OpenBudgetMaster(window, vm);
            var nameBox = BudgetNameBox(window);
            nameBox.Focus();
            Pump(window);

            Key(window, PhysicalKey.Tab);
            var focused = window.FocusManager?.GetFocusedElement();
            var fromBox = Assert.IsType<TextBox>(focused);
            Assert.Equal(m.PeriodFromText, fromBox.Text);

            var pane = BudgetPaneControls(window, m);
            Assert.DoesNotContain(pane.OfType<TextBlock>(), t => string.Equals(t.Text, "Under", StringComparison.Ordinal));
            Assert.DoesNotContain(Descendants(window).OfType<Control>(), c => c.Name == "BudgetUnderPicker");
        }
        finally { Close(window, dir); }
    }

    // ============================================================================== the stored value

    /// <summary>
    /// 🔴 <b>THE DRIVING TEST.</b> A custom group "Gbx Projects" exists. The operator Tabs through the whole
    /// Budget master header from the Name box and types "Gbx" on EVERY selector Tab reaches, then saves with
    /// Ctrl+A. Off a FRESH store the budget is Primary (raw <c>budgets.under_id</c> is NULL) and the custom group
    /// is still deletable — so no route on this screen files a budget under a group.
    /// </summary>
    [AvaloniaFact]
    public void Typing_a_group_name_on_every_budget_selector_still_files_the_budget_at_Primary_and_the_group_stays_deletable()
    {
        const string co = "W10 Budget Primary Only Co";
        const string budgetName = "Gbx Budget";
        var (window, vm, dir) = NewWindow(co);
        try
        {
            var company = vm.Company!;
            var indirect = company.Groups.Single(g => g.Name == "Indirect Expenses");
            var custom = new GroupService(company).CreateGroup("Gbx Projects", indirect.Id);

            var m = OpenBudgetMaster(window, vm);
            var nameBox = BudgetNameBox(window);
            nameBox.Focus();
            Pump(window);
            nameBox.Text = string.Empty;
            TypeText(window, budgetName);
            Assert.Equal(budgetName, nameBox.Text);

            var selectorsTyped = 0;
            for (var i = 0; i < 12; i++)
            {
                Key(window, PhysicalKey.Tab);
                if (window.FocusManager?.GetFocusedElement() is ComboBox combo && ReferenceEquals(combo.DataContext, m))
                {
                    TypeText(window, "Gbx");
                    selectorsTyped++;
                }
            }
            Assert.True(selectorsTyped >= 2, $"The Tab sweep reached only {selectorsTyped} budget selector(s).");

            m.SelectedTarget = m.Targets.First(t => t.Display.StartsWith("Indirect Expenses", StringComparison.Ordinal));
            m.LineAmountText = "250.05";
            Assert.True(m.AddLine(), m.Message);
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("created", m.Message);

            Assert.Null(RawUnderId(dir, co, budgetName));
            var reloaded = ReloadFresh(dir, co);
            Assert.Null(reloaded.FindBudgetByName(budgetName)!.UnderId);
            var reloadedGroup = reloaded.Groups.Single(g => g.Id == custom.Id);
            Assert.Null(Record.Exception(() => MasterDeletionRules.EnsureGroupDeletable(reloaded, reloadedGroup)));
        }
        finally { Close(window, dir); }
    }
}
