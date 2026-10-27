using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 5.6(b), WAVE 8 (IV-239) — the Day Book's Alt+A picker OFFERS an inactive voucher type and ASKS to
/// activate it.</b> Vendor, opened by content: help.tallysolutions.com/accounting-entry-tally/ — <i>"In case the
/// voucher is inactive, you will be prompted to activate the Memorandum voucher type"</i>;
/// help.tallysolutions.com/tally-prime/accounting/accounting-faq/ — <i>"TallyPrime prompts you to activate the voucher
/// types"</i>. Everything is driven through the REAL window keys (Alt+A, arrows, Enter) and the activation is read
/// back off a book RELOADED through a fresh store handle.
/// </summary>
public sealed class AddVoucherInactiveTypeTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewWindow()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexAddInactive_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        return (window, vm, dir);
    }

    private static void Close(MainWindow window, string dir)
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

    private static Company Reload(string dir, string name)
    {
        var fresh = new CompanyStorage(dir);
        return fresh.Load(fresh.ListCompanies().Single(e => e.Name == name));
    }

    /// <summary>A company with one posted Receipt (so the Day Book has a row) and its CONTRA type deactivated and
    /// saved — the shape an operator gets by switching a series off on the Voucher Type screen.</summary>
    private static VoucherType Seed(MainWindow window, MainWindowViewModel vm, string dir, string name)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        var capital = new DomainLedger(Guid.NewGuid(), "Capital A/c", c.FindGroupByName("Capital Account")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(capital);
        vm.OpenVoucher(VoucherBaseType.Receipt);
        var e = vm.VoucherEntry!;
        e.Date = c.FinancialYearStart.AddDays(5);
        e.Lines[0].SelectedLedger = c.FindLedgerByName("Cash");
        e.Lines[0].Side = DrCr.Debit;
        e.Lines[0].AmountText = "4321.07";
        e.Lines[1].SelectedLedger = capital;
        e.Lines[1].Side = DrCr.Credit;
        e.Lines[1].AmountText = "4321.07";
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Assert.Single(c.Vouchers);

        var contra = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Contra);
        contra.IsActive = false;
        new CompanyStorage(dir).Save(c);
        Assert.False(Reload(dir, name).VoucherTypes.Single(t => t.Id == contra.Id).IsActive);
        return contra;
    }

    private static void NavigateMenuTo(MainWindowViewModel vm, MainWindow window, string label)
    {
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            if (vm.Menu[vm.SelectedIndex].Label == label) return;
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        }
        Assert.Equal(label, vm.Menu[vm.SelectedIndex].Label);
    }

    /// <summary>
    /// The inactive type is LISTED, after every active one and marked as inactive. <b>Mutation (A1):</b> deleting
    /// the second (inactive) loop in <c>FillAddVoucherPicker</c> reddens this.
    /// </summary>
    [AvaloniaFact]
    public void AltA_lists_an_inactive_type_after_the_active_ones_marked_inactive()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var contra = Seed(window, vm, dir, "AddInactive List Co");
            vm.OpenReport(ReportKind.DayBook);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
            Assert.Equal(Screen.AddVoucherPicker, vm.CurrentScreen);

            var labels = vm.Columns[^1].Items.Where(i => i.IsSelectable).Select(i => i.Label).ToList();
            Assert.Equal(contra.Name + MainWindowViewModel.InactiveTypeSuffix, labels[^1]);
            Assert.DoesNotContain(contra.Name, labels);   // never offered as if it were active
            // The feature-gated inactive families stay out: job-order processing and payroll are off here.
            Assert.DoesNotContain(labels, l => l.StartsWith("Material", StringComparison.Ordinal));
            Assert.DoesNotContain(labels, l => l.StartsWith("Payroll", StringComparison.Ordinal));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>YES.</b> Enter on the inactive row raises the activation prompt; Enter on <i>Yes</i> activates the
    /// type — persisted, read off the RELOADED book — and opens THAT type's entry over the Day Book, seeded like any
    /// other pick. <b>Mutations:</b> (A2) making the inactive row open the type directly (skipping the prompt)
    /// reddens the prompt assertion; (A3) deleting <c>_storage.Save(Company!)</c> in the Yes arm reddens the
    /// reloaded assertion; (A4) deleting <c>SetActive(type.Id, true)</c> reddens it too.
    /// </summary>
    [AvaloniaFact]
    public void Choosing_an_inactive_type_asks_to_activate_it_and_yes_activates_and_opens_it()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var contra = Seed(window, vm, dir, "AddInactive Yes Co");
            vm.OpenReport(ReportKind.DayBook);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
            NavigateMenuTo(vm, window, contra.Name + MainWindowViewModel.InactiveTypeSuffix);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            // The prompt, in the same column: nothing activated yet.
            Assert.Equal(Screen.AddVoucherPicker, vm.CurrentScreen);
            Assert.Contains(vm.Columns[^1].Items, i => !i.IsSelectable && i.Label.Contains("is inactive"));
            Assert.Equal("Yes - activate and enter", vm.Menu[vm.SelectedIndex].Label);
            Assert.False(contra.IsActive);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.True(Reload(dir, "AddInactive Yes Co").VoucherTypes.Single(t => t.Id == contra.Id).IsActive);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Equal(contra.Id, vm.VoucherEntry!.Type.Id);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>w8 fix (review ec LOW-1) — YES OPENS THE TYPE THAT WAS CHOSEN, EVEN ON A DEDICATED SCREEN.</b> A
    /// second, inactive Material Out series ("Material Out - Site B") is picked and activated through the real
    /// Alt+A, arrows and Enter. The Material movement screen that opens is bound to SITE B, not to the seeded
    /// Material Out the resolver prefers. <b>Mutation (L1):</b> dropping the chosen type from
    /// <c>PickAddVoucherType</c>'s <c>OpenMaterialMovement(VoucherBaseType.MaterialOut, type)</c> call reddens the
    /// type assertion.
    /// </summary>
    [AvaloniaFact]
    public void Yes_on_an_inactive_second_material_out_series_opens_that_series()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            Seed(window, vm, dir, "AddInactive SiteB Co");
            vm.ShowGstConfig();
            vm.GstConfig!.EnableJobOrderProcessing = true;   // the F11 feature, through its own screen
            vm.Back();
            var c = vm.Company!;
            var seeded = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.MaterialOut && t.IsActive);
            var siteB = new VoucherType(Guid.NewGuid(), "Material Out - Site B", VoucherBaseType.MaterialOut,
                isActive: false, useForJobWork: true);
            c.AddVoucherType(siteB);
            new CompanyStorage(dir).Save(c);

            vm.OpenReport(ReportKind.DayBook);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
            NavigateMenuTo(vm, window, siteB.Name + MainWindowViewModel.InactiveTypeSuffix);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal("Yes - activate and enter", vm.Menu[vm.SelectedIndex].Label);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            Assert.True(Reload(dir, "AddInactive SiteB Co").VoucherTypes.Single(t => t.Id == siteB.Id).IsActive);
            Assert.Equal(Screen.MaterialMovementEntry, vm.CurrentScreen);
            Assert.NotEqual(seeded.Id, vm.MaterialMovementEntry!.Type.Id);
            Assert.Equal(siteB.Id, vm.MaterialMovementEntry!.Type.Id);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// <b>NO.</b> Choosing <i>No</i> puts the list back with the inactive row still offered, and the type stays
    /// inactive on the reloaded book. <b>Mutations:</b> (A5) making <i>No</i> activate as well reddens this; (A6)
    /// dropping the menu re-sync at the end of <c>FillAddVoucherPicker</c> reddens the highlight assertion.
    /// </summary>
    [AvaloniaFact]
    public void No_leaves_the_type_inactive_and_restores_the_list()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var contra = Seed(window, vm, dir, "AddInactive No Co");
            vm.OpenReport(ReportKind.DayBook);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
            NavigateMenuTo(vm, window, contra.Name + MainWindowViewModel.InactiveTypeSuffix);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            NavigateMenuTo(vm, window, "No");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            Assert.Equal(Screen.AddVoucherPicker, vm.CurrentScreen);
            Assert.Contains(vm.Columns[^1].Items, i => i.Label == contra.Name + MainWindowViewModel.InactiveTypeSuffix);
            // The keys walk the RESTORED rows, highlighted on the first (A6: dropping the re-sync at the end of
            // FillAddVoucherPicker leaves the shell's menu copy on the prompt's rows).
            Assert.Equal(vm.Columns[^1].Items.First(i => i.IsSelectable).Label, vm.Menu[vm.SelectedIndex].Label);
            Assert.Equal(vm.Columns[^1].Items.Count, vm.Menu.Count);
            Assert.False(contra.IsActive);
            Assert.False(Reload(dir, "AddInactive No Co").VoucherTypes.Single(t => t.Id == contra.Id).IsActive);
        }
        finally { Close(window, dir); }
    }
}
