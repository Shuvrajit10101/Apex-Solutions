using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger.Services;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Census 3.7 — BF-4: the two Godown F12 switches, reachable by a user.</b>
///
/// <para>🔴 <b>WHY THIS FILE EXISTS.</b> v67 built both godown fields and gated each behind its own company
/// switch — and then shipped with <b>no writer anywhere in <c>src/</c></b>. Twenty-nine occurrences of
/// <c>ConfigAllowStorageOfMaterials</c> / <c>ConfigUseAddressForLocations</c>, <b>zero assignments</b>: the
/// only code that ever set either one lived in tests. Both godown controls bind <c>IsVisible</c> to those
/// gates, so both were permanently invisible to every operator, while the divergence register read
/// "🟢 CLOSED … Both fields ship, and each is behind its own F12 switch". The gate design was correct; the
/// surface did not exist. <b>A capability nobody can reach is not built</b> — Alt+P was once inert, three
/// reports printed pages of blank cells, and this is the same defect class.</para>
///
/// <para>So every test here drives the <b>shipped</b> <see cref="MainWindow"/>: it finds the real CheckBox in
/// the visual tree, flips it the way a user would, and then opens the Godown master and reads the realised
/// control's own <c>IsEffectivelyVisible</c>. Nothing asserts on an internal the test just set.</para>
///
/// <para><b>Fidelity (R7, ruling 14).</b>
/// help.tallysolutions.com/inventory-storage-using-godowns-locations-tally/, opened by content: "setting this
/// option to Yes to displays the Allow storage of materials option on the Godown Creation screen"; "You can
/// add the Locations address by enabling this option"; "Press F12 (Configure) to change the options." The
/// PLACEMENT of the switches on the company Configuration screen rather than behind an F12 key on the Godown
/// screen is ours (IV-80), as is the single free-text address box.</para>
/// </summary>
public sealed class GodownF12SwitchReachabilityTests
{
    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(1280, 800));
        window.Arrange(new Rect(0, 0, 1280, 800));
        Dispatcher.UIThread.RunJobs();
    }

    private static string TempDir(string tag) =>
        Path.Combine(Path.GetTempPath(), tag + Guid.NewGuid().ToString("N"));

    private static void Cleanup(Window? window, string dir)
    {
        window?.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static CheckBox? Box(Window window, string name)
        => window.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault(c => c.Name == name);

    /// <summary>Reads the company back off a FRESH storage handle — the book as it is on disk.</summary>
    private static Apex.Ledger.Domain.Company Reload(string dir, string companyName)
    {
        var storage = new CompanyStorage(dir);
        return storage.Load(storage.ListCompanies().Single(e => e.Name == companyName));
    }

    private static MainWindow Open(out MainWindowViewModel vm, string dir)
    {
        vm = new MainWindowViewModel(new CompanyStorage(dir));
        vm.LoadRobertDemo();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Pump(window);
        return window;
    }

    // ------------------------------------------------------------- the switches exist on a realised screen

    /// <summary>
    /// 🔴 <b>THE BF-4 REGRESSION.</b> Both switches are present in the realised visual tree of the shipped
    /// window, carrying the vendor's own captions. Deleting either CheckBox from MainWindow.axaml reddens
    /// here — and that deletion is precisely the state v67 shipped in.
    /// </summary>
    [AvaloniaFact]
    public void Both_godown_F12_switches_are_realised_on_the_configuration_screen()
    {
        var dir = TempDir("bf4a");
        MainWindow? w = null;
        try
        {
            w = Open(out var vm, dir);
            vm.ShowGstConfig();
            Pump(w);

            var storage = Box(w, "ConfigAllowStorageOfMaterialsCheck");
            var address = Box(w, "ConfigUseAddressForLocationsCheck");

            Assert.True(storage is not null,
                "The 'Allow storage of materials' F12 switch is not in the visual tree — census 3.7's field "
              + "is unreachable again.");
            Assert.True(address is not null,
                "The 'Use address for Locations' F12 switch is not in the visual tree.");
            Assert.True(storage!.IsEffectivelyVisible);
            Assert.True(address!.IsEffectivelyVisible);
            Assert.Equal("Allow storage of materials (Godown F12)", storage.Content);
            Assert.Equal("Use address for Locations (Godown F12)", address.Content);
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// Flipping the realised CheckBox reaches the company aggregate AND the .db — read back off a RELOADED
    /// book, not off the object the click touched.
    /// </summary>
    [AvaloniaFact]
    public void Flipping_the_realised_switches_persists_to_the_reloaded_book()
    {
        var dir = TempDir("bf4b");
        MainWindow? w = null;
        try
        {
            w = Open(out var vm, dir);
            var name = vm.Company!.Name;
            vm.ShowGstConfig();
            Pump(w);

            // Deliberately DIFFERENT values, so a swap of the two store ordinals cannot pass (BF-7).
            Box(w, "ConfigAllowStorageOfMaterialsCheck")!.IsChecked = true;
            Box(w, "ConfigUseAddressForLocationsCheck")!.IsChecked = false;
            Pump(w);

            var reloaded = Reload(dir, name);
            Assert.True(reloaded.ConfigAllowStorageOfMaterials);
            Assert.False(reloaded.ConfigUseAddressForLocations);

            // And the other way round, so neither assertion above can be satisfied by a constant.
            Box(w, "ConfigAllowStorageOfMaterialsCheck")!.IsChecked = false;
            Box(w, "ConfigUseAddressForLocationsCheck")!.IsChecked = true;
            Pump(w);

            var again = Reload(dir, name);
            Assert.False(again.ConfigAllowStorageOfMaterials);
            Assert.True(again.ConfigUseAddressForLocations);
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// The switch re-reads as ON when the Configuration screen is re-entered. Omitting the load leg is the
    /// defect the W-K1 note in <c>GstConfigViewModel</c> warns about: the operator turns a switch on, finds
    /// it apparently off next time, and the next click turns it OFF for real.
    /// </summary>
    [AvaloniaFact]
    public void The_switches_read_back_as_on_when_the_screen_is_re_entered()
    {
        var dir = TempDir("bf4c");
        MainWindow? w = null;
        try
        {
            w = Open(out var vm, dir);
            vm.ShowGstConfig();
            Pump(w);
            Box(w, "ConfigAllowStorageOfMaterialsCheck")!.IsChecked = true;
            Box(w, "ConfigUseAddressForLocationsCheck")!.IsChecked = true;
            Pump(w);

            vm.ShowGateway();
            vm.ShowGstConfig();
            Pump(w);

            Assert.True(Box(w, "ConfigAllowStorageOfMaterialsCheck")!.IsChecked);
            Assert.True(Box(w, "ConfigUseAddressForLocationsCheck")!.IsChecked);
        }
        finally { Cleanup(w, dir); }
    }

    // ------------------------------------------------------- the switches actually REACH the godown fields

    /// <summary>
    /// 🔴 <b>THE WHOLE POINT.</b> With both switches OFF neither godown control is visible on the realised
    /// Godown master — and with them ON, both are. This is the end-to-end claim census 3.7 needs: a user can
    /// turn the vendor's F12 option on and then see and fill the field it gates.
    /// </summary>
    [AvaloniaFact]
    public void Turning_the_switches_on_makes_both_godown_fields_visible_on_the_realised_master()
    {
        var dir = TempDir("bf4d");
        MainWindow? w = null;
        try
        {
            w = Open(out var vm, dir);

            // --- off (the shipped default, and the only state v67 could ever be in) ---
            vm.ShowGodownMaster();
            Pump(w);
            var offStorage = Box(w, "GodownAllowStorageCheck");
            var offAddress = w!.GetVisualDescendants().OfType<TextBox>()
                              .FirstOrDefault(t => t.Name == "GodownAddressBox");
            Assert.True(offStorage is null || !offStorage.IsEffectivelyVisible,
                "With its F12 switch off the vendor hides 'Allow storage of materials'.");
            Assert.True(offAddress is null || !offAddress.IsEffectivelyVisible,
                "With its F12 switch off the vendor hides the Locations address.");

            // --- the user turns both on, on the real screen ---
            vm.ShowGstConfig();
            Pump(w);
            Box(w, "ConfigAllowStorageOfMaterialsCheck")!.IsChecked = true;
            Box(w, "ConfigUseAddressForLocationsCheck")!.IsChecked = true;
            Pump(w);

            // --- and re-opens the Godown master ---
            vm.ShowGodownMaster();
            Pump(w);

            var onStorage = Box(w, "GodownAllowStorageCheck");
            var onAddress = w.GetVisualDescendants().OfType<TextBox>()
                             .FirstOrDefault(t => t.Name == "GodownAddressBox");

            Assert.True(onStorage is not null && onStorage.IsEffectivelyVisible,
                "After turning the F12 switch on, 'Allow storage of materials' must be reachable on the "
              + "Godown master. If this fails the field is unreachable again — BF-4.");
            Assert.True(onAddress is not null && onAddress.IsEffectivelyVisible,
                "After turning the F12 switch on, the Locations address box must be reachable.");
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// Each switch gates only ITS OWN field. Without this, one switch wired to both gates would satisfy the
    /// test above.
    /// </summary>
    [AvaloniaFact]
    public void Each_switch_gates_only_its_own_godown_field()
    {
        var dir = TempDir("bf4e");
        MainWindow? w = null;
        try
        {
            w = Open(out var vm, dir);
            vm.ShowGstConfig();
            Pump(w);
            Box(w, "ConfigAllowStorageOfMaterialsCheck")!.IsChecked = true;
            Box(w, "ConfigUseAddressForLocationsCheck")!.IsChecked = false;
            Pump(w);

            vm.ShowGodownMaster();
            Pump(w);

            var storage = Box(w, "GodownAllowStorageCheck");
            var address = w!.GetVisualDescendants().OfType<TextBox>()
                            .FirstOrDefault(t => t.Name == "GodownAddressBox");

            Assert.True(storage is not null && storage.IsEffectivelyVisible);
            Assert.True(address is null || !address.IsEffectivelyVisible,
                "'Use address for Locations' is off, so the address box must stay hidden.");
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// The gate still decides what is WRITTEN, not merely what is drawn — the contract the v67 mutation M3
    /// already proved, re-asserted here through the REAL switch rather than an object a test poked. With the
    /// storage switch off, a godown saved from the realised master carries the vendor default
    /// (<c>Allow storage of materials = Yes</c>) and no address, whatever the hidden form fields hold.
    /// </summary>
    [AvaloniaFact]
    public void With_the_switches_off_a_godown_saved_from_the_real_screen_takes_the_vendor_defaults()
    {
        var dir = TempDir("bf4f");
        MainWindow? w = null;
        try
        {
            w = Open(out var vm, dir);
            var name = vm.Company!.Name;

            vm.ShowGodownMaster();
            Pump(w);

            var master = vm.GodownMaster!;
            master.Name = "Hidden-Gate Store";
            master.AllowStorageOfMaterials = false;       // hidden: must NOT reach the book
            master.Address = "should not be written";     // hidden: must NOT reach the book
            Assert.True(master.Create(), master.Message);

            var reloaded = Reload(dir, name);
            var saved = reloaded.Godowns.Single(g => g.Name == "Hidden-Gate Store");

            Assert.True(saved.AllowStorageOfMaterials);   // the vendor's "By default, each godown can store materials"
            Assert.True(string.IsNullOrEmpty(saved.Address));
        }
        finally { Cleanup(w, dir); }
    }
}
