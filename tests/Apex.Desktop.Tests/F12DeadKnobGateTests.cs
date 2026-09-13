using System;
using System.IO;
using System.Linq;
using Apex.Ledger.Reports;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>USER RULING 21's SECOND HALF — THE DEAD F12 BUTTON.</b>
///
/// <para><b>The defect.</b> The right-hand button bar carried
/// <c>ButtonBarItem("F12", "Configure", F12Configure)</c> with <b>no enable predicate at all</b>. F12 in the
/// reference product is CONTEXTUAL — it configures the screen you are standing on — and in this build it really
/// does act on only four surfaces (a report, a print preview, the ledger master, and a voucher-numbering
/// context). Everywhere else the operator read an ENABLED badge captioned "Configure", pressed it, and got a
/// stub sentence claiming <i>"F12 Configure — display options (Phase 1 defaults)"</i> that configured nothing.
/// A shipped button that does nothing is a DEAD KNOB, and this project has filed several.</para>
///
/// <para><b>What the fix is NOT.</b> It is not a new global configuration tree. The reference product removed
/// that surface outright — <i>"Button F12 has been removed from Menu context."</i>, <i>"The menu
/// 'Configuration' has been removed."</i> — and the global layer now lives at F1 (Help) &gt; Settings, which is
/// census row 1.8 and is covered by <see cref="AppSettingsF1Tests"/>. F12 keeps doing exactly what it did; it
/// is only ADVERTISED where it does it.</para>
///
/// <para>🔴 <b>DISABLING THE ROW DISABLES THE KEY TOO, AND THAT IS THE POINT.</b> The window's
/// <c>Fire()</c> helper runs a bar row's action only when the row is enabled, so where the badge is dim the bare
/// F12 keystroke is inert as well — the badge now tells the truth about the key. The two contexts the key tunnel
/// handles BEFORE the bar (a report page, a print preview) are unaffected, because the tunnel returns before it
/// reaches <c>Fire</c>; the ON cases below prove those doors still open.</para>
/// </summary>
public sealed class F12DeadKnobGateTests
{
    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    private static ButtonBarItem F12Row(MainWindowViewModel vm) => vm.ButtonBar.Single(b => b.Key == "F12");

    private static ButtonBarItem F1Row(MainWindowViewModel vm) => vm.ButtonBar.Single(b => b.Key == "F1");

    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) Open()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexF12Gate_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
        window.Show();

        vm.NewCompanyName = "F12 Gate Co";
        vm.CreateCompany();
        Pump(window);
        return (window, vm, tempDir);
    }

    private static void Cleanup(MainWindow window, string dir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ==========================================================================================
    // THE OFF HALF — where F12 does nothing, it must not be advertised.
    // ==========================================================================================

    /// <summary>
    /// 🔴 <b>The clause that fails on today's main.</b> On the Gateway F12 configures nothing — it falls straight
    /// through to the stub — yet the badge is offered enabled.
    /// </summary>
    [AvaloniaFact]
    public void The_F12_badge_is_dimmed_on_the_gateway_where_it_configures_nothing()
    {
        var (window, vm, dir) = Open();
        try
        {
            Assert.Equal(Screen.Gateway, vm.CurrentScreen);
            Assert.False(F12Row(vm).Enabled,
                "The Gateway offers an ENABLED 'F12 Configure' badge, but F12 configures nothing there. "
              + "A shipped button that does nothing is a dead knob.");
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// The same on a master screen F12 has no arm for. The Currency master is chosen because it is a real,
    /// reachable master with no F12 configuration block of its own — unlike the Ledger master, which has one.
    /// </summary>
    [AvaloniaFact]
    public void The_F12_badge_is_dimmed_on_a_master_screen_with_no_configuration()
    {
        var (window, vm, dir) = Open();
        try
        {
            vm.ShowCurrencyMaster();
            Pump(window);

            Assert.NotNull(vm.CurrencyMaster);
            Assert.False(F12Row(vm).Enabled,
                "The Currency master offers an ENABLED 'F12 Configure' badge and has no F12 configuration.");
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>And the KEY is inert there too, silently.</b> This is the half a badge test alone cannot prove: an
    /// operator who presses F12 on the Gateway must not get a message pretending something was configured. The
    /// old stub set <c>Message</c> to "F12 Configure — display options (Phase 1 defaults)", which was a false
    /// caption — there were no display options behind it.
    /// </summary>
    [AvaloniaFact]
    public void Pressing_F12_on_the_gateway_does_not_claim_to_have_configured_anything()
    {
        var (window, vm, dir) = Open();
        try
        {
            vm.Message = string.Empty;
            window.KeyPressQwerty(PhysicalKey.F12, RawInputModifiers.None);
            Pump(window);

            Assert.True(string.IsNullOrEmpty(vm.Message),
                $"F12 on the Gateway reported \"{vm.Message}\" — it configured nothing, so it must say nothing. "
              + "A message is what made the dead knob look alive.");
        }
        finally { Cleanup(window, dir); }
    }

    // ==========================================================================================
    // THE ON HALF — every context that really configures must still be advertised AND still work.
    // A gate that dimmed F12 everywhere would pass the OFF cases above on its own.
    // ==========================================================================================

    /// <summary>A report page is the RQ-6 F12 configuration context; the badge stays lit and the panel opens.</summary>
    [AvaloniaFact]
    public void The_F12_badge_stays_lit_on_a_report_and_the_config_panel_still_opens()
    {
        var (window, vm, dir) = Open();
        try
        {
            vm.OpenReport(ReportKind.TrialBalance);
            Pump(window);
            Assert.True(F12Row(vm).Enabled, "A report page must keep advertising F12 — it configures the report.");

            window.KeyPressQwerty(PhysicalKey.F12, RawInputModifiers.None);
            Pump(window);
            Assert.NotNull(vm.ReportConfig);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>The ledger master has its own F12 configuration block; the badge stays lit and still toggles it.</summary>
    [AvaloniaFact]
    public void The_F12_badge_stays_lit_on_the_ledger_master_and_still_toggles_its_configuration()
    {
        var (window, vm, dir) = Open();
        try
        {
            vm.ShowLedgerMaster();
            Pump(window);
            Assert.True(F12Row(vm).Enabled, "The ledger master must keep advertising F12 — it has a config block.");

            var before = vm.LedgerMaster!.ShowConfiguration;
            window.KeyPressQwerty(PhysicalKey.F12, RawInputModifiers.None);
            Pump(window);
            Assert.NotEqual(before, vm.LedgerMaster!.ShowConfiguration);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// A voucher-entry screen is the per-type voucher-numbering F12 context (numbering S4); the badge stays lit
    /// and the numbering column still opens.
    /// </summary>
    [AvaloniaFact]
    public void The_F12_badge_stays_lit_on_a_voucher_entry_and_still_opens_voucher_numbering()
    {
        var (window, vm, dir) = Open();
        try
        {
            vm.OpenVoucher(Apex.Ledger.Domain.VoucherBaseType.Payment);
            Pump(window);
            Assert.NotNull(vm.VoucherEntry);
            Assert.True(F12Row(vm).Enabled,
                "A voucher-entry screen must keep advertising F12 — it opens the per-type numbering config.");

            window.KeyPressQwerty(PhysicalKey.F12, RawInputModifiers.None);
            Pump(window);
            Assert.NotNull(vm.VoucherNumberingConfig);
        }
        finally { Cleanup(window, dir); }
    }

    // ==========================================================================================
    // THE F1 BADGE — the same discipline applied to the row this wave added.
    // ==========================================================================================

    /// <summary>
    /// The F1 badge is lit wherever the Settings page can open, and is DIMMED before a company is opened, where
    /// the cascade the page needs does not exist yet. Dimmed rather than enabled-and-inert: this build's own
    /// rule, applied to its own new row.
    ///
    /// <para>🔴 <b>Recorded as a DIVERGENCE:</b> the vendor's F1 popup opens from anywhere, including its
    /// company-select screen. Here it needs the column strip, which only renders once a company is open.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_F1_badge_is_lit_in_the_cascade_and_dimmed_before_a_company_is_open()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexF1Gate_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
        window.Show();
        try
        {
            Pump(window);
            Assert.False(F1Row(vm).Enabled,
                "The F1 badge is offered before a company is open, but the Settings page needs the cascade "
              + "column strip, which is not rendered there — it would open a column nobody can see.");

            vm.NewCompanyName = "F1 Gate Co";
            vm.CreateCompany();
            Pump(window);
            Assert.True(F1Row(vm).Enabled, "F1 must be advertised once the cascade is live.");
        }
        finally { Cleanup(window, tempDir); }
    }
}
