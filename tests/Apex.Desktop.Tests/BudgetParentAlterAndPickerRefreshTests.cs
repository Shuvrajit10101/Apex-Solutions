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
/// <b>WAVE 12 / TRACK hc, REVIEW ADDITIONS (census 2.9, schema v70).</b> Two gaps the adversarial review found in
/// the budget-parent work, each pinned by an artefact read off the raw <c>.db</c>, a fresh store or the realised
/// picker:
/// <list type="number">
/// <item><b>Re-filing a budget on ALTER was unpinned.</b> Deleting <c>budget.UnderId = parentId;</c> from
/// <c>BudgetMasterViewModel.Alter</c> left all 178 budget-named Desktop tests green: the shipped Alter test only
/// ever saved Primary over Primary. The first test files an existing budget under another by keys and reads the
/// stored <c>under_id</c>; the second pins that a failed save puts the old parent back on the aggregate.</item>
/// <item><b>The Under picker went stale after Alt+D, Y.</b> The shell refreshes this screen through
/// <c>ReloadExisting</c>, which rebuilt only the list, so the deleted budget was still offered under Under; typing
/// its name selected it and Ctrl+A was refused with "The budget chosen under 'Under' no longer exists."</item>
/// </list>
/// <para><b>Mutations (run 2026-11-23, each reverted with git checkout and confirmed empty):</b>
/// R-M1 <c>budget.UnderId = parentId;</c> removed from <c>Alter</c> →
/// <see cref="Ctrl_Enter_then_typing_on_Under_files_an_existing_budget_under_another_and_it_reaches_the_db"/> RED;
/// R-M2 <c>budget.UnderId = beforeUnder;</c> removed from <c>Alter</c>'s catch →
/// <see cref="A_failed_alteration_save_puts_the_old_parent_back_on_the_aggregate"/> RED;
/// R-M3 <c>ReloadExisting</c> back to <c>RefreshList()</c> only →
/// <see cref="After_AltD_Y_the_Under_picker_no_longer_offers_the_deleted_budget"/> RED.</para>
/// <para><b>Independent verification (wave 12d, 2026-11-23):</b> R-M1..R-M3 re-run, each RED as stated (R-M3 also
/// reddens the keep test below). R-M5, <c>ReloadExisting</c> falling back to Primary instead of keeping the
/// operator's choice, was green on every budget test until
/// <see cref="After_AltD_Y_on_another_budget_the_Under_picker_keeps_the_operators_choice_and_Ctrl_A_files_under_it"/>
/// was added; it is now RED. The <c>IsAltering</c> half of <c>ReloadExisting</c> is not reachable by keys: Alt+D is
/// refused on an open alteration (<c>MainWindowViewModel.IsDeleteTargetPage</c>), so it is defensive only.</para>
/// </summary>
public sealed class BudgetParentAlterAndPickerRefreshTests
{
    // ======================================================================================= harness

    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewWindow(string company)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexW12HCR_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>T2-169: at most Menu.Count presses, then a named failure.</summary>
    private static void ArrowToAndEnter(MainWindow window, MainWindowViewModel vm, string label)
    {
        var rows = vm.Columns[vm.ActiveColumnIndex].Items.Count;
        for (var i = 0; i <= rows; i++)
        {
            if (ActiveLabel(vm) == label) { Key(window, PhysicalKey.Enter); return; }
            Key(window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"'{label}' was not reachable by arrow navigation from the active column.");
    }

    private static BudgetMasterViewModel OpenBudgetMaster(MainWindow window, MainWindowViewModel vm)
    {
        vm.ShowGateway();
        Pump(window);
        ArrowToAndEnter(window, vm, "Create");
        ArrowToAndEnter(window, vm, "Budget");
        Assert.Equal(Screen.BudgetMaster, vm.CurrentScreen);
        return vm.BudgetMaster!;
    }

    /// <summary>Arrows until <paramref name="name"/> is highlighted on the List of Budgets. Bounded: the list's rows
    /// plus 20, because after Ctrl+A the arrows first walk the form's own fields before they reach the list.</summary>
    private static void ArrowToRow(MainWindow window, MainWindowViewModel vm, string name)
    {
        var bound = vm.BudgetMaster!.Existing.Count + 20;
        for (var i = 0; i <= bound; i++)
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

    private static void CreateAtPrimaryByKeys(MainWindow window, BudgetMasterViewModel m, string name, string amount)
    {
        var nameBox = BudgetNameBox(window);
        nameBox.Focus();
        Pump(window);
        nameBox.Text = string.Empty;
        TypeText(window, name);
        m.SelectedTarget = m.Targets.First(t => t.Display.StartsWith("Cash", StringComparison.Ordinal));
        m.LineAmountText = amount;
        Assert.True(m.AddLine(), m.Message);
        Key(window, PhysicalKey.A, RawInputModifiers.Control);
        Assert.Contains("created", m.Message);
    }

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
        Assert.NotNull(value);
        return value is DBNull ? null : (string)value!;
    }

    // ============================================================================== alter re-files

    /// <summary>
    /// "Ops Budget" is created at Primary; Ctrl+Enter opens it for alteration, typing "Dep" on the realised Under
    /// picker chooses "Department Budget", and Ctrl+A saves. The raw <c>under_id</c> is the parent's id, a fresh
    /// store reloads the link, and a fresh screen lists "Ops Budget" beneath "Department Budget".
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_Enter_then_typing_on_Under_files_an_existing_budget_under_another_and_it_reaches_the_db()
    {
        const string co = "W12 Budget Refile Co";
        var (window, vm, dir) = NewWindow(co);
        try
        {
            var m = OpenBudgetMaster(window, vm);
            CreateAtPrimaryByKeys(window, m, "Department Budget", "1000.37");
            CreateAtPrimaryByKeys(window, m, "Ops Budget", "250.05");
            Assert.Null(RawUnderId(dir, co, "Ops Budget"));

            OpenBudgetMaster(window, vm);
            ArrowToRow(window, vm, "Ops Budget");
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            var alter = vm.BudgetMaster!;
            Assert.True(alter.IsAltering);
            Assert.Equal("Primary", alter.SelectedParent?.Name);

            UnderPicker(window).Focus();
            Pump(window);
            TypeText(window, "Dep");
            Assert.Equal("Department Budget", alter.SelectedParent?.Name);
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("altered", alter.Message);

            var parentId = vm.Company!.FindBudgetByName("Department Budget")!.Id;
            Assert.Equal(parentId.ToString("D"), RawUnderId(dir, co, "Ops Budget"));

            SqliteConnection.ClearAllPools();
            var fresh = new CompanyStorage(dir);
            var reloaded = fresh.Load(fresh.ListCompanies().Single(e => e.Name == co));
            Assert.Equal(parentId, reloaded.FindBudgetByName("Ops Budget")!.UnderId);
            var freshScreen = new BudgetMasterViewModel(reloaded, new CompanyStorage(dir), () => { });
            Assert.Equal(
                new[] { ("Department Budget", 0, "Primary"), ("Ops Budget", 1, "Department Budget") },
                freshScreen.Existing.Select(r => (r.Name, r.Depth, r.Under)).ToArray());
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The same re-file, but the save fails (a duplicated cost centre trips the store's PRIMARY KEY before the
    /// budgets are written). The alteration is reported, and the aggregate keeps "Ops Budget" at Primary, so the
    /// next save cannot carry a parent the operator never got to keep.
    /// </summary>
    [AvaloniaFact]
    public void A_failed_alteration_save_puts_the_old_parent_back_on_the_aggregate()
    {
        const string co = "W12 Budget Refile Fail Co";
        var (window, vm, dir) = NewWindow(co);
        try
        {
            var m = OpenBudgetMaster(window, vm);
            CreateAtPrimaryByKeys(window, m, "Department Budget", "10.01");
            CreateAtPrimaryByKeys(window, m, "Ops Budget", "20.02");

            OpenBudgetMaster(window, vm);
            ArrowToRow(window, vm, "Ops Budget");
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            var alter = vm.BudgetMaster!;
            UnderPicker(window).Focus();
            Pump(window);
            TypeText(window, "Dep");
            Assert.Equal("Department Budget", alter.SelectedParent?.Name);

            var company = vm.Company!;
            var centre = new CostCentre(Guid.NewGuid(), "Duplicated Centre", company.CostCategories[0].Id);
            company.AddCostCentre(centre);
            company.AddCostCentre(centre);   // same primary key twice: the next Save throws a SqliteException

            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("cost_centres", alter.Message);   // the planted failure, not an early refusal
            Assert.Null(company.FindBudgetByName("Ops Budget")!.UnderId);
            Assert.Null(RawUnderId(dir, co, "Ops Budget"));
        }
        finally { Close(window, dir); }
    }

    // ============================================================================== picker after delete

    /// <summary>
    /// On the Budget Creation screen, Alt+D, Y deletes the leaf "Beta Budget". The Under picker then offers only
    /// Primary and "Alpha Budget"; typing "Bet" on it cannot choose the deleted budget, and a new budget saved from
    /// there is filed at Primary rather than refused.
    /// </summary>
    [AvaloniaFact]
    public void After_AltD_Y_the_Under_picker_no_longer_offers_the_deleted_budget()
    {
        const string co = "W12 Budget Picker Refresh Co";
        var (window, vm, dir) = NewWindow(co);
        try
        {
            var m = OpenBudgetMaster(window, vm);
            CreateAtPrimaryByKeys(window, m, "Alpha Budget", "1.11");
            CreateAtPrimaryByKeys(window, m, "Beta Budget", "2.22");
            Assert.Equal(new[] { "Primary", "Alpha Budget", "Beta Budget" }, m.Parents.Select(p => p.Name));

            // A freshly opened Creation screen: its picker is built now, BEFORE the delete, and the same screen
            // stays up after it (the shell refreshes it in place through ReloadExisting).
            var screen = OpenBudgetMaster(window, vm);
            Assert.Contains(screen.Parents, p => p.Name == "Beta Budget");
            ArrowToRow(window, vm, "Beta Budget");
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);
            Assert.Contains("deleted", vm.Notice);

            Assert.Same(screen, vm.BudgetMaster);
            Assert.Equal(new[] { "Primary", "Alpha Budget" }, screen.Parents.Select(p => p.Name));
            Assert.DoesNotContain(UnderPicker(window).Items.OfType<BudgetParentOption>(),
                p => p.Name == "Beta Budget");

            var nameBox = BudgetNameBox(window);
            nameBox.Focus();
            Pump(window);
            nameBox.Text = string.Empty;
            TypeText(window, "Gamma Budget");
            Key(window, PhysicalKey.Tab);
            Assert.Equal("BudgetUnderPicker", (window.FocusManager?.GetFocusedElement() as ComboBox)?.Name);
            TypeText(window, "Bet");
            Assert.NotEqual("Beta Budget", screen.SelectedParent?.Name);

            screen.SelectedParent = screen.Parents.First(p => p.IsPrimary);
            screen.SelectedTarget = screen.Targets.First(t => t.Display.StartsWith("Cash", StringComparison.Ordinal));
            screen.LineAmountText = "3.33";
            Assert.True(screen.AddLine(), screen.Message);
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("created", screen.Message);
            Assert.Null(RawUnderId(dir, co, "Gamma Budget"));
        }
        finally { Close(window, dir); }
    }

    // ============================================================ picker after delete: IV additions (wave 12d)

    /// <summary>Types <paramref name="prefix"/> on the realised Under picker, takes the match with Enter if the typing
    /// opened the dropdown, and leaves focus on the CLOSED picker, the state in which the window's arrows walk the
    /// List of Budgets and Alt+D deletes its highlighted row.</summary>
    private static void ChooseUnderByKeysThenRestOnClosedPicker(MainWindow window, string prefix)
    {
        var picker = UnderPicker(window);
        picker.Focus();
        Pump(window);
        TypeText(window, prefix);
        if (picker.IsDropDownOpen) Key(window, PhysicalKey.Enter);
        picker.Focus();
        Pump(window);
        Assert.False(picker.IsDropDownOpen);
    }

    /// <summary>
    /// IV wave 12d. The operator has already chosen "Alpha Budget" under Under (typed on the realised picker) when
    /// Alt+D, Y deletes a DIFFERENT budget, "Beta Budget". The rebuilt picker must keep "Alpha Budget" chosen, on
    /// the view model and on the realised control, and Ctrl+A then files the new budget under it in the raw
    /// <c>.db</c>. Reddens if <c>ReloadExisting</c> drops the kept choice and falls back to Primary.
    /// </summary>
    [AvaloniaFact]
    public void After_AltD_Y_on_another_budget_the_Under_picker_keeps_the_operators_choice_and_Ctrl_A_files_under_it()
    {
        const string co = "W12 Budget Picker Keep Co";
        var (window, vm, dir) = NewWindow(co);
        try
        {
            var m = OpenBudgetMaster(window, vm);
            CreateAtPrimaryByKeys(window, m, "Alpha Budget", "1.11");
            CreateAtPrimaryByKeys(window, m, "Beta Budget", "2.22");

            var screen = OpenBudgetMaster(window, vm);
            var nameBox = BudgetNameBox(window);
            nameBox.Focus();
            Pump(window);
            nameBox.Text = string.Empty;
            TypeText(window, "Gamma Budget");
            ChooseUnderByKeysThenRestOnClosedPicker(window, "Alp");
            Assert.Equal("Alpha Budget", screen.SelectedParent?.Name);
            ArrowToRow(window, vm, "Beta Budget");
            Assert.Equal("Alpha Budget", screen.SelectedParent?.Name);   // the arrows walked the list, not the picker
            Assert.Equal("Beta Budget", vm.MasterListScreen?.HighlightedMasterRow?.MasterName);

            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);
            Assert.Contains("deleted", vm.Notice);
            Assert.Same(screen, vm.BudgetMaster);
            Assert.Equal(new[] { "Primary", "Alpha Budget" }, screen.Parents.Select(p => p.Name));

            Assert.Equal("Alpha Budget", screen.SelectedParent?.Name);
            Assert.Equal("Alpha Budget", (UnderPicker(window).SelectedItem as BudgetParentOption)?.Name);

            screen.SelectedTarget = screen.Targets.First(t => t.Display.StartsWith("Cash", StringComparison.Ordinal));
            screen.LineAmountText = "3.33";
            Assert.True(screen.AddLine(), screen.Message);
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("created", screen.Message);
            var alphaId = vm.Company!.FindBudgetByName("Alpha Budget")!.Id;
            Assert.Equal(alphaId.ToString("D"), RawUnderId(dir, co, "Gamma Budget"));
        }
        finally { Close(window, dir); }
    }
}
