using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// Census 6.4 / IV-315 (wave 12, track ha) — the Ledger master's sales/purchase GST block is reached by Tab from the
/// TOP of the form, not only by a test that focuses its first control directly. An operator who opens the Ledger
/// master on a Sales Accounts ledger and keeps pressing Tab must land on "GST Applicable", and the four fields after
/// it, in the order the screen shows them.
/// <para><b>Mutation</b> (logged in the track's mut/log.txt as H8): <c>IsTabStop="False"</c> on the
/// <c>LedgerSalesPurchaseGstApplicable</c> check box reddens
/// <see cref="Tab_from_the_alias_box_reaches_the_gst_block_and_walks_its_fields_in_screen_order"/>; the sibling
/// file's tests do not see it because they focus the box directly.</para>
/// </summary>
public sealed class LedgerMasterGstBlockTabReachTests : IDisposable
{
    private const int MaxTabs = 120; // bounded walk (T2-169): far more stops than the Ledger master has

    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ApexLedgerGstTab_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [AvaloniaFact]
    public void Tab_from_the_alias_box_reaches_the_gst_block_and_walks_its_fields_in_screen_order()
    {
        var vm = new MainWindowViewModel(new CompanyStorage(_dir));
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        try
        {
            vm.NewCompanyName = "Ledger GST Tab Co";
            vm.CreateCompany();
            var c = vm.Company!;
            new GstService(c).EnableGst(new GstConfig
            {
                HomeStateCode = "27", Gstin = "27AAPFU0939F1ZV", RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = c.FinancialYearStart, Periodicity = GstReturnPeriodicity.Monthly,
            });

            vm.ShowLedgerMaster();
            var master = vm.LedgerMaster!;
            master.SelectedGroup = master.Groups.First(g => g.Name == "Sales Accounts");
            master.Name = "Tab Reach Income";
            Pump(window);
            Assert.True(master.ShowSalesPurchaseGst);

            var alias = Realised<TextBox>(window, "LedgerAliasBox");
            Assert.True(alias.Focus(), "the alias box cannot take focus.");
            Pump(window);

            var visited = new List<string>();
            var reached = false;
            for (var i = 0; i < MaxTabs; i++)
            {
                window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                Pump(window);
                var name = Focused(window);
                visited.Add(name);
                if (name == "LedgerSalesPurchaseGstApplicable") { reached = true; break; }
            }
            if (!reached)
                Assert.Fail($"{MaxTabs} Tabs from the alias box never reached 'GST Applicable'. Visited: "
                          + string.Join(" > ", visited.Distinct()));

            // Space ticks it, and the four fields follow in screen order.
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Pump(window);
            Assert.True(master.SalesPurchaseGst.Enabled, "Space did not tick GST Applicable.");
            foreach (var expected in new[]
                     {
                         "LedgerSalesPurchaseGstHsn", "LedgerSalesPurchaseGstTaxability",
                         "LedgerSalesPurchaseGstRate", "LedgerSalesPurchaseGstSupplyType",
                     })
            {
                window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                Pump(window);
                Assert.Equal(expected, Focused(window));
            }
        }
        finally { window.Close(); }
    }

    private static string Focused(Window window)
    {
        var focused = TopLevel.GetTopLevel(window)!.FocusManager!.GetFocusedElement() as Control;
        return focused?.Name ?? focused?.GetType().Name ?? "(nothing)";
    }

    private static T Realised<T>(Window window, string name) where T : Control
    {
        var control = window.GetVisualDescendants().OfType<T>().FirstOrDefault(x => x.Name == name);
        Assert.True(control is { IsEffectivelyVisible: true }, $"no realised, visible '{name}' on the Ledger master.");
        return control!;
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(1440, 900));
        window.Arrange(new Rect(0, 0, 1440, 900));
        Dispatcher.UIThread.RunJobs();
    }
}
