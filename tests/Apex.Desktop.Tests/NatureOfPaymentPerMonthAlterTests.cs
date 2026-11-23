using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// Census 6.28 (wave 12, track hf), continuation — <b>altering a PER-MONTH section (§194-I) on a book that still
/// carries the superseded annualised ₹6,00,000 in its Cumulative-FY threshold.</b> That stored figure is inert (the
/// engine tests §194-I per month), and Create refuses a cumulative figure on such a section. If the alteration form
/// pre-filled the stored figure, every alteration of that row would refuse itself until the operator blanked a
/// number the engine never applies. The form therefore loads the field blank, and the save drops the inert value.
/// This behaviour is OURS (docs/invented-vs-cloned.md §2AG, IV-344): no vendor page speaks to it.
///
/// <para>Driven through the realised <see cref="MainWindow"/>'s real key handler (arrows, Enter, Ctrl+Enter,
/// Ctrl+A) and asserted on a book reloaded through a FRESH <see cref="CompanyStorage"/>. Menu and list walks are
/// bounded (T2-169).</para>
/// </summary>
public sealed class NatureOfPaymentPerMonthAlterTests
{
    private const string ValidTan = "MUMA12345B";

    [AvaloniaFact]
    public void Altering_a_per_month_section_carrying_an_inert_cumulative_figure_saves_and_drops_that_figure()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexW12hfPm_" + Guid.NewGuid().ToString("N"));
        const string name = "NOP Per Month Alter Co";
        var storage = new CompanyStorage(dir);
        var vm = new MainWindowViewModel(storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan });

        // An OLDER book's shape: §194I(b) still carrying the superseded annualised ₹6,00,000.
        var seeded = c.FindNatureOfPaymentByCode("194I(b)")!;
        Assert.True(seeded.ThresholdWindowIsPerMonth);
        c.Tds!.ReplaceNatureOfPayment(seeded.WithTerms(
            seeded.SectionCode, seeded.Name, seeded.RateWithPanBp, seeded.RateWithoutPanBp, seeded.FvuSectionCode,
            seeded.SingleTransactionThreshold, new Money(600_000m)));
        storage.Save(c);
        Assert.Equal(600_000m, Reload(dir, name).FindNatureOfPaymentByCode("194I(b)")!.CumulativeThreshold!.Value.Amount);

        vm.ShowGateway();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Pump(window);
        try
        {
            ArrowToAndEnter(window, vm, "Create");
            ArrowToAndEnter(window, vm, "Nature of Payment");
            Assert.Equal(Screen.NatureOfPaymentMaster, vm.CurrentScreen);

            var bound = vm.NatureOfPaymentMaster!.Natures.Count + 2;
            var found = false;
            for (var i = 0; i < bound && !found; i++)
            {
                if (vm.NatureOfPaymentMaster?.HighlightedRow?.SectionCode == "194I(b)") found = true;
                else Key(window, PhysicalKey.ArrowDown);
            }
            Assert.True(found, "Section '194I(b)' was not reachable by arrow navigation on the Nature of Payment list.");

            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            var alter = vm.NatureOfPaymentMaster!;
            Assert.True(alter.IsAltering);
            Assert.Equal(string.Empty, alter.CumulativeThresholdText);

            alter.RateWithPanText = "9";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("altered", alter.Message);

            var stored = Reload(dir, name).FindNatureOfPaymentByCode("194I(b)")!;
            Assert.Equal(seeded.Id, stored.Id);
            Assert.Equal(900, stored.RateWithPanBp);
            Assert.Null(stored.CumulativeThreshold);
        }
        finally
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
    }

    private static Company Reload(string dir, string name)
    {
        var fresh = new CompanyStorage(dir);
        return fresh.Load(fresh.ListCompanies().Single(e => string.Equals(e.Name, name, StringComparison.Ordinal)));
    }

    private static void Pump(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Key(MainWindow window, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, mods);
        Pump(window);
    }

    private static void ArrowToAndEnter(MainWindow window, MainWindowViewModel vm, string label)
    {
        var bound = vm.Columns[vm.ActiveColumnIndex].Items.Count + 2;
        for (var i = 0; i < bound; i++)
        {
            if (vm.Columns[vm.ActiveColumnIndex].Selected?.Label == label) { Key(window, PhysicalKey.Enter); return; }
            Key(window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"'{label}' was not reachable by arrow navigation from the active column.");
    }
}
