using System;
using System.IO;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Apex.Ledger;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

// A11 TEMPORARY PROBE — measurement only, deleted before reporting. Fixes nothing.
public sealed class A11ProbeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public A11ProbeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexA11Probe_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    private MainWindowViewModel NewCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name + " " + Guid.NewGuid().ToString("N")[..6];
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    [AvaloniaFact]
    public void PROBE_AltA_while_the_F12_config_panel_is_stacked_over_the_receivables_report()
    {
        var vm = NewCompany("Probe F12");
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        vm.OpenReport(ReportKind.ReceivablesOutstanding);
        Pump(window);
        Assert.Equal(Screen.Report, vm.CurrentScreen);

        // The re-home is what GAVE this report F12 at all.
        vm.OpenReportConfig();
        Pump(window);
        Assert.Equal(Screen.ReportConfig, vm.CurrentScreen);   // operator is standing IN the config panel

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
        Pump(window);

        // The operator pressed Alt+A inside the F12 panel. They must still be in it.
        Assert.Equal(Screen.ReportConfig, vm.CurrentScreen);

        window.Close();
    }

    [AvaloniaFact]
    public void PROBE_AltA_while_a_print_preview_column_is_stacked_over_the_payables_report()
    {
        var vm = NewCompany("Probe Print");
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        vm.OpenReport(ReportKind.PayablesOutstanding);
        Pump(window);
        Assert.Equal(Screen.Report, vm.CurrentScreen);

        vm.OpenPrintPreview();
        Pump(window);
        Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
        Pump(window);

        Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);

        window.Close();
    }

    // CONTROL: the same stacked-panel press on the DAY BOOK, whose arm carries a proper guard.
    // If this passes while the two above fail, the defect is specific to the new arm, not to the harness.
    [AvaloniaFact]
    public void PROBE_CONTROL_AltA_while_the_F12_config_panel_is_stacked_over_the_day_book()
    {
        var vm = NewCompany("Probe Control");
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        vm.OpenReport(ReportKind.DayBook);
        Pump(window);
        Assert.Equal(Screen.Report, vm.CurrentScreen);

        vm.OpenReportConfig();
        Pump(window);
        Assert.Equal(Screen.ReportConfig, vm.CurrentScreen);

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
        Pump(window);

        Assert.Equal(Screen.ReportConfig, vm.CurrentScreen);

        window.Close();
    }
}
