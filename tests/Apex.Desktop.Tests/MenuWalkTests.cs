using System;
using System.IO;
using System.Threading.Tasks;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit.Sdk;

namespace Apex.Desktop.Tests;

/// <summary>
/// T2-169: pins that the shared menu walk TERMINATES on a label the open column does not hold, and fails naming it.
/// The old inline walk wrapped for ever (MoveDown wraps), so a renamed menu row hung the gate instead of failing one test.
/// </summary>
public sealed class MenuWalkTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public MenuWalkTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexMenuWalkTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    [Fact]
    public void A_label_the_column_does_not_hold_fails_within_one_lap_naming_the_label()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();
        vm.ShowExceptionReportsMenu();

        // Run on a worker so a regression to an unbounded walk shows up as a timeout, not a hung gate.
        var walk = Task.Run(() => Record.Exception(() => MenuWalk.ArrowTo(vm, "No Such Report")));

        Assert.True(walk.Wait(TimeSpan.FromSeconds(20)), "the menu walk did not terminate on a missing label");
        var failure = Assert.IsType<FailException>(walk.Result);
        Assert.Contains("'No Such Report'", failure.Message);
        Assert.Contains("Negative Stock", failure.Message); // the column's actual rows are listed
    }

    [Fact]
    public void A_label_the_column_holds_is_reached_by_arrowing()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();
        vm.ShowExceptionReportsMenu();

        MenuWalk.ArrowTo(vm, "Overdue Payables");

        Assert.Equal("Overdue Payables", vm.Menu[vm.SelectedIndex].Label);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}
