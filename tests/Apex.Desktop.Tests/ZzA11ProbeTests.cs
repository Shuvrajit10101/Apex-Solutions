using System;
using System.IO;
using System.Linq;
using Avalonia.Input;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;

namespace Apex.Desktop.Tests;

// TEMPORARY A11 REVIEW PROBE — deleted immediately after measurement. Fixes nothing.
public sealed class ZzA11ProbeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ZzA11ProbeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "A11Probe_" + Guid.NewGuid().ToString("N"));
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

    private MainWindowViewModel NewCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        return vm;
    }

    [Fact]
    public void PROBE_switch_to_and_company_menu_are_claimed_on_company_select_with_a_company_still_open()
    {
        var vm = NewCompany("Probe Co");
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        Assert.True(vm.IsGatewayCascade);

        // Alt+F3 — the NEW table entry.
        ShellChordTable.Match(vm, Key.F3, KeyModifiers.Alt)!.Fire(vm);

        Assert.Equal(Screen.CompanySelect, vm.CurrentScreen);
        Assert.NotNull(vm.Company);                 // the book is still open
        Assert.False(vm.IsGatewayCascade);          // ...but the cascade region is now HIDDEN
        Assert.Empty(vm.Columns);

        // Ctrl+G is still claimed here.
        var ctrlG = ShellChordTable.Match(vm, Key.G, KeyModifiers.Control);
        Assert.NotNull(ctrlG);
        ctrlG!.Fire(vm);

        Assert.NotNull(vm.SwitchTo);
        Assert.Equal(Screen.SwitchTo, vm.CurrentScreen);
        Assert.False(vm.IsGatewayCascade);          // the column was pushed into a HIDDEN region
        Assert.Single(vm.Columns);
        Assert.NotEmpty(vm.SwitchTo!.Rows);

        // And Alt+K too, from the same state.
        var vm2 = NewCompany("Probe Co 2");
        ShellChordTable.Match(vm2, Key.F3, KeyModifiers.Alt)!.Fire(vm2);
        var altK = ShellChordTable.Match(vm2, Key.K, KeyModifiers.Alt);
        Assert.NotNull(altK);
        altK!.Fire(vm2);
        Assert.Equal(Screen.CompanyMenu, vm2.CurrentScreen);
        Assert.False(vm2.IsGatewayCascade);
        Assert.Equal("Company", vm2.Columns.Last().Title);
    }

    [Fact]
    public void PROBE_shut_company_leaves_the_go_to_overlay_up()
    {
        var vm = NewCompany("Probe GoTo Co");
        vm.ToggleGoTo();
        Assert.True(vm.IsGoToOpen);

        ShellChordTable.Match(vm, Key.F3, KeyModifiers.Control)!.Fire(vm);

        Assert.Null(vm.Company);
        Assert.Equal(Screen.CompanySelect, vm.CurrentScreen);
        Assert.True(vm.IsGoToOpen, "ClearSubScreens does not null GoTo, so the overlay survives the shut.");
    }

    [Fact]
    public void PROBE_ctrl_g_is_claimed_while_the_go_to_overlay_is_open()
    {
        var vm = NewCompany("Probe Stack Co");
        vm.ToggleGoTo();
        Assert.True(vm.IsGoToOpen);

        var ctrlG = ShellChordTable.Match(vm, Key.G, KeyModifiers.Control);
        Assert.NotNull(ctrlG);
        ctrlG!.Fire(vm);

        Assert.NotNull(vm.SwitchTo);
        Assert.True(vm.IsGoToOpen, "Both jump lists are now up at once.");
    }
}
