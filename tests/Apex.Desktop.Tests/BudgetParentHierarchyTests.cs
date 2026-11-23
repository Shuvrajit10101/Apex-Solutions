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
/// 🔴 <b>WAVE 12 / TRACK hc — A BUDGET IS FILED UNDER ANOTHER BUDGET (census 2.9, schema v70).</b> Replaces
/// <c>BudgetUnderAbsenceTests</c>, which pinned the ABSENCE of an Under control while <c>budgets.under_id</c> still
/// REFERENCED groups(id). That column now REFERENCES budgets(id) and the screen carries the picker the vendor shape
/// describes.
///
/// <para><b>Fidelity (R7, ruling 14).</b> help.tallysolutions.com/budgets-tally/, November 3, 2025 version
/// (web.archive.org snapshot 20260310093634, read by content 2026-11-22): <i>"you need to create the budget under
/// Primary from the List of Budgets. After the Department Budget is created, you can further go ahead and create
/// specific Department Budgets such as Sales Department Budget, Marketing Department Budget and so on under the main
/// Department Budget. This shows the budget structure in a hierarchical form."</i> ⚠️ The live page (November 19,
/// 2026) says <i>"Select Primary or any other predefined groups"</i> instead — the conflict is IV-325. A GROUP is
/// never offered here (a group parent had no effect except making the group undeletable; W10 gb).</para>
///
/// <para>Every artefact is read off a FRESH <see cref="CompanyStorage"/> or the raw <c>.db</c>, or is a realised
/// control. Every Under choice is made by keystrokes on the realised <c>BudgetUnderPicker</c>.</para>
///
/// <para><b>Mutations (run 2026-11-22, each reverted with git checkout and confirmed empty):</b>
/// (1) <c>underId: parentId</c> dropped from <c>BudgetMasterViewModel.Create</c> →
/// <see cref="Tab_from_Name_reaches_the_Under_picker_and_typing_files_the_new_budget_under_a_budget_by_keys"/> RED
/// (raw under_id NULL); (2) <c>RefreshList</c> back to a flat name sort with <c>Depth</c> 0 → the same test RED
/// (the realised indented row is absent); (3) the <c>EnsureBudgetDeletable</c> call removed from
/// <c>DeleteMaster</c> → <see cref="AltD_Y_on_a_budget_with_budgets_under_it_is_refused_by_name_and_both_survive_a_reload"/>
/// RED; (4) the <c>ParentRefusal</c> re-ask removed from <c>Alter</c> →
/// <see cref="Alter_leaves_out_the_budget_and_its_subtree_and_refuses_a_parent_that_became_a_descendant"/> RED.</para>
/// </summary>
public sealed class BudgetParentHierarchyTests
{
    // ======================================================================================= harness

    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewWindow(string company)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexW12HC_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Arrows on the existing-list until <paramref name="name"/> is highlighted (bounded).</summary>
    private static void ArrowToRow(MainWindow window, MainWindowViewModel vm, string name)
    {
        for (var i = 0; i < 40; i++)
        {
            if (vm.MasterListScreen?.HighlightedMasterRow?.MasterName == name) return;
            Key(window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"'{name}' was not reachable by arrow navigation on the List of Budgets.");
    }

    private static TextBox BudgetNameBox(MainWindow window) =>
        Descendants(window).OfType<TextBox>().Single(t => t.PlaceholderText == "e.g. Q1 Budget");

    private static ComboBox UnderPicker(MainWindow window) =>
        Descendants(window).OfType<ComboBox>().Single(c => c.Name == "BudgetUnderPicker");

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

    /// <summary>
    /// Types the Name, Tabs ONCE to the Under picker, types <paramref name="underPrefix"/> on it (null = leave it
    /// on Primary), adds one ledger line, and presses Ctrl+A.
    /// </summary>
    private static void CreateByKeys(MainWindow window, BudgetMasterViewModel m, string name, string? underPrefix,
        string amount)
    {
        var nameBox = BudgetNameBox(window);
        nameBox.Focus();
        Pump(window);
        nameBox.Text = string.Empty;
        TypeText(window, name);
        Assert.Equal(name, nameBox.Text);

        Key(window, PhysicalKey.Tab);
        var focused = window.FocusManager?.GetFocusedElement();
        var picker = Assert.IsType<ComboBox>(focused);
        Assert.Equal("BudgetUnderPicker", picker.Name);
        if (underPrefix is not null) TypeText(window, underPrefix);

        m.SelectedTarget = m.Targets.First(t => t.Display.StartsWith("Cash", StringComparison.Ordinal));
        m.LineAmountText = amount;
        Assert.True(m.AddLine(), m.Message);
        Key(window, PhysicalKey.A, RawInputModifiers.Control);
        Assert.Contains("created", m.Message);
    }

    // ============================================================================== create + list

    /// <summary>
    /// 🔴 <b>THE DRIVING TEST.</b> "Department Budget" is created at Primary; then "Sales Department Budget" is
    /// created by Tab from Name onto the realised Under picker and typing "Dep". The picker offers Primary and the
    /// List of Budgets only — no account group. Off the raw <c>.db</c> the child's <c>under_id</c> is the parent's
    /// id; off a FRESH store the parent is the parent; the List of Budgets paints the child INDENTED under it, and a
    /// fresh screen over the reloaded book lists it the same way with "Under" = the parent.
    /// </summary>
    [AvaloniaFact]
    public void Tab_from_Name_reaches_the_Under_picker_and_typing_files_the_new_budget_under_a_budget_by_keys()
    {
        const string co = "W12 Budget Tree Co";
        var (window, vm, dir) = NewWindow(co);
        try
        {
            var m = OpenBudgetMaster(window, vm);
            Assert.Equal("Primary", m.SelectedParent?.Name);
            Assert.Equal(new[] { "Primary" }, m.Parents.Select(p => p.Name));   // nothing else yet — no groups
            Assert.Contains(Descendants(window).OfType<TextBlock>(),
                t => ReferenceEquals(t.DataContext, m) && t.Text == "Under");

            CreateByKeys(window, m, "Department Budget", underPrefix: null, amount: "10000.37");
            Assert.Null(RawUnderId(dir, co, "Department Budget"));

            // The new budget is offered for the next one; a group never is.
            Assert.Equal(new[] { "Primary", "Department Budget" }, m.Parents.Select(p => p.Name));
            Assert.DoesNotContain(m.Parents, p => vm.Company!.Groups.Any(g => g.Name == p.Name));

            CreateByKeys(window, m, "Sales Department Budget", underPrefix: "Dep", amount: "2500.05");

            var parentId = vm.Company!.FindBudgetByName("Department Budget")!.Id;
            Assert.Equal(parentId.ToString("D"), RawUnderId(dir, co, "Sales Department Budget"));

            // The realised List of Budgets: the child is painted indented, directly beneath its parent.
            Pump(window);
            var rows = Descendants(window).OfType<TextBlock>()
                .Where(t => t.DataContext is BudgetListRow && t.Text is { } s && s.TrimStart().EndsWith("Budget"))
                .Select(t => t.Text!).ToList();
            Assert.Equal(new[] { "Department Budget", "    Sales Department Budget" }, rows);

            var reloaded = ReloadFresh(dir, co);
            Assert.Equal(parentId, reloaded.FindBudgetByName("Sales Department Budget")!.UnderId);
            var freshScreen = new BudgetMasterViewModel(reloaded, new CompanyStorage(dir), () => { });
            Assert.Equal(
                new[] { ("Department Budget", 0, "Primary"), ("Sales Department Budget", 1, "Department Budget") },
                freshScreen.Existing.Select(r => (r.Name, r.Depth, r.Under)).ToArray());
            var snapshot = freshScreen.ToMasterListSnapshot();
            Assert.Equal("Under", snapshot.Columns[1].Caption);
            Assert.Equal("Department Budget", snapshot.Rows[1][1]);
        }
        finally { Close(window, dir); }
    }

    // ============================================================================== delete refusal

    /// <summary>
    /// Alt+D, Y on "Department Budget" while "Sales Department Budget" is filed under it is REFUSED, naming the
    /// child; both survive a FRESH reload with the parent link intact. Deleting the child first, then the parent,
    /// succeeds — and the book saves again cleanly.
    /// </summary>
    [AvaloniaFact]
    public void AltD_Y_on_a_budget_with_budgets_under_it_is_refused_by_name_and_both_survive_a_reload()
    {
        const string co = "W12 Budget Delete Co";
        var (window, vm, dir) = NewWindow(co);
        try
        {
            var m = OpenBudgetMaster(window, vm);
            CreateByKeys(window, m, "Department Budget", underPrefix: null, amount: "100.01");
            CreateByKeys(window, m, "Sales Department Budget", underPrefix: "Dep", amount: "50.50");
            var parentId = vm.Company!.FindBudgetByName("Department Budget")!.Id;

            OpenBudgetMaster(window, vm);
            ArrowToRow(window, vm, "Department Budget");
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);

            Assert.Contains("Cannot delete budget 'Department Budget'", vm.Notice);
            Assert.Contains("Sales Department Budget", vm.Notice);
            var afterRefusal = ReloadFresh(dir, co);
            Assert.Equal(2, afterRefusal.Budgets.Count);
            Assert.Equal(parentId, afterRefusal.FindBudgetByName("Sales Department Budget")!.UnderId);

            OpenBudgetMaster(window, vm);
            ArrowToRow(window, vm, "Sales Department Budget");
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);
            Assert.Contains("deleted", vm.Notice);

            OpenBudgetMaster(window, vm);
            ArrowToRow(window, vm, "Department Budget");
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);
            Assert.Contains("deleted", vm.Notice);
            Assert.Empty(ReloadFresh(dir, co).Budgets);
        }
        finally { Close(window, dir); }
    }

    // ============================================================================== alter + cycle guard

    /// <summary>
    /// Ctrl+Enter on "Department Budget" opens it for alteration with a picker that leaves out itself and the
    /// budget filed under it. Then, altering "Ops Budget", "Sales" is chosen by keys — and before Ctrl+A another
    /// surface files "Department Budget" under "Ops Budget". Saving would close a loop, so Ctrl+A is REFUSED by
    /// name and the raw <c>.db</c> is untouched. Choosing Primary instead saves.
    /// </summary>
    [AvaloniaFact]
    public void Alter_leaves_out_the_budget_and_its_subtree_and_refuses_a_parent_that_became_a_descendant()
    {
        const string co = "W12 Budget Alter Co";
        var (window, vm, dir) = NewWindow(co);
        try
        {
            var m = OpenBudgetMaster(window, vm);
            CreateByKeys(window, m, "Department Budget", underPrefix: null, amount: "1.11");
            CreateByKeys(window, m, "Sales Department Budget", underPrefix: "Dep", amount: "2.22");
            CreateByKeys(window, m, "Ops Budget", underPrefix: null, amount: "3.33");
            var c = vm.Company!;

            OpenBudgetMaster(window, vm);
            ArrowToRow(window, vm, "Department Budget");
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            var alterDept = vm.BudgetMaster!;
            Assert.True(alterDept.IsAltering);
            Assert.Equal(new[] { "Primary", "Ops Budget" }, alterDept.Parents.Select(p => p.Name));
            Assert.Equal("Primary", alterDept.SelectedParent?.Name);

            OpenBudgetMaster(window, vm);
            ArrowToRow(window, vm, "Ops Budget");
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            var alterOps = vm.BudgetMaster!;
            Assert.True(alterOps.IsAltering);
            var picker = UnderPicker(window);
            picker.Focus();
            Pump(window);
            TypeText(window, "Sal");
            Assert.Equal("Sales Department Budget", alterOps.SelectedParent?.Name);

            // Another surface re-files Department Budget under Ops Budget while the question is up.
            c.FindBudgetByName("Department Budget")!.UnderId = c.FindBudgetByName("Ops Budget")!.Id;

            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Equal(
                "A budget cannot be filed under 'Sales Department Budget', which is filed under it.",
                alterOps.Message);
            Assert.Null(RawUnderId(dir, co, "Ops Budget"));
            Assert.Null(c.FindBudgetByName("Ops Budget")!.UnderId);   // the aggregate was not touched either

            // Undo the foreign re-file, then choose Primary: saves.
            c.FindBudgetByName("Department Budget")!.UnderId = null;
            alterOps.SelectedParent = alterOps.Parents.First(p => p.IsPrimary);
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("altered", alterOps.Message);
            Assert.Null(RawUnderId(dir, co, "Ops Budget"));
        }
        finally { Close(window, dir); }
    }
}
