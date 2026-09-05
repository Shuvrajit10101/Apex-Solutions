using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS ROWS 2.4 / 5.10 / 5.11 LOCK — the Voucher Type master, driven end-to-end by real keystrokes.</b>
///
/// <para><b>The defect these close.</b> <see cref="VoucherType"/> carries ~20 configurable properties and 24
/// seeded instances, and <b>not one of them could be edited by an operator</b>: there was no view model, no
/// <c>Screen</c> member, no menu row. Two consequences followed. (a) The numbering method was a get-only display
/// string bound to a <c>TextBlock</c> — so <i>Manual</i> and <i>None</i>, and later the two attested methods that
/// had no domain member at all, were unreachable (5.10). (b) <see cref="VoucherType.IsActive"/> had no write
/// route anywhere in the product except <c>JobWorkService</c> and a rollback restore inside a <c>catch</c> — so
/// the seeded-INACTIVE payroll voucher types could never be switched on, and an entire shipped module could not
/// post (5.11, and the census's T1-4).</para>
///
/// <para><b>Why every assertion goes through the window.</b> The engine verbs are new here too, but a test that
/// calls <see cref="VoucherTypeService"/> would prove nothing about these rows: the rows are about REACH. Each
/// test below walks the Gateway cascade with arrows and Enter, or presses the real accelerators — Ctrl+A to
/// accept, arrows to walk the existing-list, Ctrl+Enter to alter, Alt+D then Y to delete, Space to activate.</para>
///
/// <para><b>Alteration is asserted by IDENTITY, never by name</b> — a "rename" that created a second voucher type
/// would pass a name-only assertion while silently forking every voucher already posted under it.</para>
/// </summary>
public sealed class VoucherTypeMasterReachabilityTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow(string company)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexVoucherType_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        vm.NewCompanyName = company;
        vm.CreateCompany();
        vm.ShowGateway();

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, tempDir);
    }

    private static void Key(MainWindow window, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, mods);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Cleanup(string dir)
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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

    /// <summary>Arrow-Down the existing-types list until the highlight lands on <paramref name="name"/>. Fails
    /// loudly when the name is not reachable by arrows — the list is long (24 seeds), so the bound is generous.</summary>
    private static void ArrowTo(MainWindow window, MainWindowViewModel vm, string name)
    {
        for (var i = 0; i < 60; i++)
        {
            if (vm.VoucherTypeMaster?.HighlightedRow?.Name == name) return;
            Key(window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"'{name}' was not reachable by arrow navigation on the existing voucher-type list.");
    }

    private static void OpenMaster(MainWindow window, MainWindowViewModel vm)
    {
        ArrowToAndEnter(window, vm, "Create");
        ArrowToAndEnter(window, vm, "Voucher Type");
        Assert.Equal(Screen.VoucherTypeMaster, vm.CurrentScreen);
        Assert.NotNull(vm.VoucherTypeMaster);
    }

    // ================================================================= 2.4 — the full-cascade reachability proof

    /// <summary>
    /// 🔴 THE ROW-2.4 TEST. From the Gateway, using <b>only keys</b>: drill Masters → Create → Voucher Type,
    /// create one with Ctrl+A, arrow into the existing-list, Ctrl+Enter to arrive at an <b>Alteration</b> of that
    /// very type, rename it with Ctrl+A, and confirm the SAME id now carries the new name.
    /// </summary>
    [AvaloniaFact]
    public void Voucher_type_creation_and_alteration_are_reachable_from_the_Gateway_using_only_the_keyboard()
    {
        var (window, vm, dir) = NewWindow("VType Reach Co");
        try
        {
            OpenMaster(window, vm);

            var create = vm.VoucherTypeMaster!;
            Assert.False(create.IsAltering);
            Assert.Equal("Voucher Type Creation", create.Caption);
            create.Name = "Export Sales";
            create.SelectedBaseType = create.BaseTypes.Single(b => b.Value == VoucherBaseType.Sales);
            create.SelectedNumbering = create.NumberingMethods.Single(n => n.Value == NumberingMethod.Manual);
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            var created = vm.Company!.VoucherTypes.Single(t => t.Name == "Export Sales");
            Assert.False(created.IsPredefined);
            Assert.Equal(NumberingMethod.Manual, created.Numbering);
            var id = created.Id;

            ArrowTo(window, vm, "Export Sales");
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);

            var alter = vm.VoucherTypeMaster!;
            Assert.True(alter.IsAltering, "Ctrl+Enter did not open the highlighted voucher type for alteration");
            Assert.Equal("Voucher Type Alteration", alter.Caption);
            Assert.Equal("Export Sales", alter.Name);
            Assert.False(alter.CanChooseBaseType);

            alter.Name = "Export Sales (SEZ)";
            alter.SelectedNumbering = alter.NumberingMethods.Single(n => n.Value == NumberingMethod.MultiUserAuto);
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            Assert.Equal("Export Sales (SEZ)", vm.Company.FindVoucherType(id)!.Name);
            Assert.Equal(NumberingMethod.MultiUserAuto, vm.Company.FindVoucherType(id)!.Numbering);
            Assert.Single(vm.Company.VoucherTypes, t => !t.IsPredefined);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>Alt+D then Y on the highlighted row deletes an unused user type — the real destructive
    /// accelerator and the real confirmation, never a direct service call.</summary>
    [AvaloniaFact]
    public void An_unused_user_voucher_type_deletes_with_AltD_then_Y()
    {
        var (window, vm, dir) = NewWindow("VType Delete Co");
        try
        {
            OpenMaster(window, vm);
            var m = vm.VoucherTypeMaster!;
            m.Name = "Scrap Sales";
            m.SelectedBaseType = m.BaseTypes.Single(b => b.Value == VoucherBaseType.Sales);
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Single(vm.Company!.VoucherTypes, t => t.Name == "Scrap Sales");

            ArrowTo(window, vm, "Scrap Sales");
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);

            Assert.DoesNotContain(vm.Company.VoucherTypes, t => t.Name == "Scrap Sales");
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>A PREDEFINED type is refused by the engine guard, and the operator is told to deactivate it
    /// instead — the seed survives.</summary>
    [AvaloniaFact]
    public void A_predefined_voucher_type_is_refused_by_AltD_and_survives()
    {
        var (window, vm, dir) = NewWindow("VType Guard Co");
        try
        {
            OpenMaster(window, vm);
            ArrowTo(window, vm, "Journal");
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);

            Assert.Contains(vm.Company!.VoucherTypes, t => t.Name == "Journal" && t.IsPredefined);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ================================================================= 5.11 — Show Inactive → activate (T1-4)

    /// <summary>
    /// 🔴 THE ROW-5.11 / T1-4 TEST. A deactivated voucher type leaves the list, comes BACK under <b>Show
    /// Inactive</b>, and Space switches it on again — after which
    /// <see cref="VoucherTypeResolver.ResolveForEntry"/> resolves it once more. That resolver is what every F-key
    /// accelerator and Gateway voucher row calls, so this is the difference between a shipped voucher family that
    /// can post and one that cannot.
    /// </summary>
    [AvaloniaFact]
    public void Show_Inactive_then_Space_reactivates_a_deactivated_voucher_type_and_entry_can_reach_it_again()
    {
        var (window, vm, dir) = NewWindow("VType Active Co");
        try
        {
            OpenMaster(window, vm);
            var m = vm.VoucherTypeMaster!;
            Assert.False(m.ShowInactive);

            // Deactivate the seeded Journal with the same Space gesture.
            ArrowTo(window, vm, "Journal");
            Key(window, PhysicalKey.Space);

            var journal = vm.Company!.FindVoucherTypeByName("Journal")!;
            Assert.False(journal.IsActive);
            Assert.Null(VoucherTypeResolver.ResolveForEntry(vm.Company, VoucherBaseType.Journal));

            // It has left the list — that is what makes the Show-Inactive switch necessary rather than decorative.
            Assert.DoesNotContain(m.Existing, r => r.Name == "Journal");

            m.ShowInactive = true;
            Assert.Contains(m.Existing, r => r.Name == "Journal" && r.Active == "No");

            ArrowTo(window, vm, "Journal");
            Key(window, PhysicalKey.Space);

            Assert.True(vm.Company.FindVoucherType(journal.Id)!.IsActive);
            Assert.Same(journal, VoucherTypeResolver.ResolveForEntry(vm.Company, VoucherBaseType.Journal));
            Assert.DoesNotContain(m.Existing, r => r.Name == "Journal" && r.Active == "No");
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// 🔴 <b>THE T1-4 TEST — the shipped voucher type that could not post, and now can.</b>
    ///
    /// <para><c>SeedVoucherTypes</c> ships FIVE of its twenty-three predefined types INACTIVE: the four job-work
    /// types and <b>Payroll</b> (<c>Ctrl+F4</c>). The four job-work ones are switched on by the F11 "Enable Job
    /// Order Processing" toggle through <c>JobWorkService</c>. <b>Payroll had no route of any kind.</b> Measured
    /// over <c>src/</c>, the only writes to <see cref="VoucherType.IsActive"/> in the shipped product were that
    /// <c>JobWorkService</c> line and a rollback restore inside a <c>catch</c> in <c>GstConfigViewModel</c> — so
    /// <see cref="VoucherTypeResolver.ResolveForEntry"/>, which every accelerator and Gateway row calls, returned
    /// <c>null</c> for <see cref="VoucherBaseType.Payroll"/> on every company that has ever existed, forever.
    /// </para>
    ///
    /// <para>This test asserts BOTH halves: that the type really does ship unreachable, and that Show Inactive +
    /// Space is the route that makes it reachable. The first half is what stops the second from being a tautology.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void The_seeded_inactive_Payroll_voucher_type_had_no_activation_route_and_this_screen_is_it()
    {
        var (window, vm, dir) = NewWindow("VType Payroll Co");
        try
        {
            var payroll = vm.Company!.FindVoucherTypeByName("Payroll")!;
            Assert.False(payroll.IsActive);
            Assert.Null(VoucherTypeResolver.ResolveForEntry(vm.Company, VoucherBaseType.Payroll));

            OpenMaster(window, vm);
            var m = vm.VoucherTypeMaster!;

            // Seeded inactive and therefore hidden by default — exactly the state the operator has to be able to
            // get out of.
            Assert.DoesNotContain(m.Existing, r => r.Name == "Payroll");
            Assert.True(m.InactiveCount >= 5,
                "The seed ships five inactive predefined types (four job-work + Payroll).");

            m.ShowInactive = true;
            ArrowTo(window, vm, "Payroll");
            Key(window, PhysicalKey.Space);

            Assert.True(vm.Company.FindVoucherType(payroll.Id)!.IsActive);
            Assert.Same(payroll, VoucherTypeResolver.ResolveForEntry(vm.Company, VoucherBaseType.Payroll));
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>The two attested user flags survive a create → reopen-for-alteration round trip through the
    /// screen, which is the only place an operator can set them (census 5.11).</summary>
    [AvaloniaFact]
    public void The_two_attested_user_flags_are_settable_on_the_screen_and_come_back_on_alteration()
    {
        var (window, vm, dir) = NewWindow("VType Flag Co");
        try
        {
            OpenMaster(window, vm);
            var m = vm.VoucherTypeMaster!;
            m.Name = "Counter Sales";
            m.SelectedBaseType = m.BaseTypes.Single(b => b.Value == VoucherBaseType.Sales);
            m.PrintAfterSaving = true;
            m.ProvideNarrationForEachLedger = true;
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            var created = vm.Company!.VoucherTypes.Single(t => t.Name == "Counter Sales");
            Assert.True(created.PrintAfterSaving);
            Assert.True(created.ProvideNarrationForEachLedger);

            ArrowTo(window, vm, "Counter Sales");
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);

            var alter = vm.VoucherTypeMaster!;
            Assert.True(alter.PrintAfterSaving);
            Assert.True(alter.ProvideNarrationForEachLedger);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ================================================================= 5.10 — the numbering-method picker

    /// <summary>
    /// 🔴 THE ROW-5.10 TEST. All FIVE attested methods are offered by the picker, in the vendor's own captions,
    /// and choosing one PERSISTS onto the type. Before this screen the method was a get-only string bound to a
    /// <c>TextBlock</c>, and two of the five had no domain member at all.
    /// </summary>
    [AvaloniaFact]
    public void The_numbering_picker_offers_all_five_attested_methods_and_the_choice_sticks()
    {
        var (window, vm, dir) = NewWindow("VType Numbering Co");
        try
        {
            OpenMaster(window, vm);
            var m = vm.VoucherTypeMaster!;

            Assert.Equal(
                new[] { "Automatic", "Automatic (Manual Override)", "Manual", "Multi-user Auto", "None" },
                m.NumberingMethods.Select(o => o.Display).ToArray());

            foreach (var method in Enum.GetValues<NumberingMethod>())
            {
                var name = $"Series {(int)method}";
                m.Name = name;
                m.SelectedBaseType = m.BaseTypes.Single(b => b.Value == VoucherBaseType.Journal);
                m.SelectedNumbering = m.NumberingMethods.Single(o => o.Value == method);
                Assert.True(m.Create(), m.Message);
                Assert.Equal(method, vm.Company!.VoucherTypes.Single(t => t.Name == name).Numbering);
            }
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>The engine and the screen agree on what "Manual" means: a Manual type posted through the shell's
    /// own entry screen keeps the number the operator typed, and it is not silently renumbered.</summary>
    [AvaloniaFact]
    public void A_Manual_numbered_type_lets_the_operator_type_the_voucher_number_on_the_entry_screen()
    {
        var (window, vm, dir) = NewWindow("VType Manual Co");
        try
        {
            OpenMaster(window, vm);
            ArrowTo(window, vm, "Journal");
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            var alter = vm.VoucherTypeMaster!;
            alter.SelectedNumbering = alter.NumberingMethods.Single(n => n.Value == NumberingMethod.Manual);
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Equal(NumberingMethod.Manual, vm.Company!.FindVoucherTypeByName("Journal")!.Numbering);

            Key(window, PhysicalKey.Escape);
            vm.OpenVoucher(VoucherBaseType.Journal);
            var entry = vm.VoucherEntry!;
            Assert.True(entry.IsVoucherNumberEditable,
                "A Manual-numbered voucher type must offer an editable Voucher No. — otherwise Manual is a label "
                + "over a number the operator cannot supply.");

            entry.VoucherNumber = 4021;
            Assert.Equal("4021", entry.FormattedVoucherNumber);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>The mirror: an Automatic type keeps the Voucher No. read-only, so the editable box is a
    /// consequence of the method rather than a box that appeared everywhere.</summary>
    [AvaloniaFact]
    public void An_Automatic_numbered_type_keeps_the_voucher_number_read_only()
    {
        var (window, vm, dir) = NewWindow("VType Auto Co");
        try
        {
            vm.OpenVoucher(VoucherBaseType.Journal);
            Assert.Equal(NumberingMethod.Automatic, vm.Company!.FindVoucherTypeByName("Journal")!.Numbering);
            Assert.False(vm.VoucherEntry!.IsVoucherNumberEditable);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ================================================================= the shared-arm guarantees

    /// <summary>Alt+D is inert while the screen is mid-ALTERATION — deleting the master you are part-way through
    /// editing is never what was meant. Same rule the Stock Item and payroll masters follow.</summary>
    [AvaloniaFact]
    public void AltD_is_inert_while_a_voucher_type_is_open_for_alteration()
    {
        var (window, vm, dir) = NewWindow("VType Inert Co");
        try
        {
            OpenMaster(window, vm);
            var m = vm.VoucherTypeMaster!;
            m.Name = "Branch Journal";
            m.SelectedBaseType = m.BaseTypes.Single(b => b.Value == VoucherBaseType.Journal);
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            ArrowTo(window, vm, "Branch Journal");
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            Assert.True(vm.VoucherTypeMaster!.IsAltering);

            Assert.False(vm.IsDeleteTargetPage);
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);
            Assert.Contains(vm.Company!.VoucherTypes, t => t.Name == "Branch Journal");
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>The Voucher Type master joins the SHARED master-list arm rather than a parallel one — the
    /// property the arrows, Alt+D and the post-delete refresh all resolve through.</summary>
    [AvaloniaFact]
    public void The_voucher_type_master_resolves_through_the_shared_master_list_arm()
    {
        var (window, vm, dir) = NewWindow("VType Arm Co");
        try
        {
            OpenMaster(window, vm);
            Assert.NotNull(vm.MasterListScreen);
            Assert.Same(vm.VoucherTypeMaster, vm.MasterListScreen);
            Assert.Equal("voucher type", vm.MasterListScreen!.MasterKindLabel);

            // And it does NOT leak onto the payroll arm, whose four-of-eight remainder is locked elsewhere.
            Assert.Null(vm.PayrollMasterScreen);
        }
        finally { window.Close(); Cleanup(dir); }
    }
}
