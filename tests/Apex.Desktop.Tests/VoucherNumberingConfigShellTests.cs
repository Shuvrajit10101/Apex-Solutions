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
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// Numbering slice S4 (numbering-design-v2 §5.1/§5.3) — F12 on a voucher-entry context opens the numbering config as a
/// cascade column PUSHED to the right (prior panes persist; NOT a full-screen replace), and the new screen does not
/// regress the b8c617e keystroke-arbitration contract (F4 stays Contra, arrows move the N1 highlight, Esc pops the
/// column). Driven on the REAL headless <see cref="MainWindow"/>, mirroring <c>KeyboardArbitrationTests</c>.
/// </summary>
public sealed class VoucherNumberingConfigShellTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewWindow(string company)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexNumS4Shell_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        var window = new MainWindow { DataContext = vm, Width = 1920, Height = 1080 };
        window.Show();
        vm.NewCompanyName = company;
        vm.CreateCompany();
        vm.ShowGateway();
        Pump(window);
        return (window, vm, dir);
    }

    private static void Pump(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static void Cleanup(Window window, string dir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }

    // ================================================================ F12 pushes the column (prior pane persists)

    [AvaloniaFact]
    public void F12_onVoucherContext_opensNumberingConfigColumn()
    {
        var (window, vm, dir) = NewWindow("Num S4 F12 Co");
        try
        {
            vm.OpenVoucher(VoucherBaseType.Sales);
            Pump(window);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            var columnsBefore = vm.Columns.Count;
            var salesTypeId = vm.VoucherEntry!.Type.Id;

            window.KeyPressQwerty(PhysicalKey.F12, RawInputModifiers.None);
            Pump(window);

            // A column was PUSHED, not a full-screen replace: the voucher-entry pane still sits beneath.
            Assert.Equal(Screen.VoucherNumberingConfig, vm.CurrentScreen);
            Assert.NotNull(vm.VoucherNumberingConfig);
            Assert.Equal(columnsBefore + 1, vm.Columns.Count);
            Assert.NotNull(vm.Columns[^2].Voucher);                 // prior pane persists (the Sales voucher entry)
            // The config opened pre-selected on the entry's type.
            Assert.Equal(salesTypeId, vm.VoucherNumberingConfig!.SelectedType!.Id);

            // RENDER-VERIFY: the new DataTemplate actually materialised headlessly — its header TextBlock and the N1
            // type ListBox (one row per company voucher type) are in the live visual tree.
            var texts = Descendants(window).OfType<TextBlock>().Select(tb => tb.Text).ToList();
            Assert.Contains("Voucher Numbering (F12)", texts);
            var typeList = Descendants(window).OfType<ListBox>()
                .FirstOrDefault(lb => lb.ItemCount == vm.VoucherNumberingConfig!.Types.Count && lb.ItemCount > 0);
            Assert.NotNull(typeList);
        }
        finally { Cleanup(window, dir); }
    }

    // ================================================================ keyboard arbitration unbroken on the new screen

    [AvaloniaFact]
    public void F12_config_keyboardArbitration_unbroken()
    {
        var (window, vm, dir) = NewWindow("Num S4 Kbd Co");
        try
        {
            vm.OpenVoucher(VoucherBaseType.Payment);
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.F12, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.VoucherNumberingConfig, vm.CurrentScreen);

            // (a) arrows move the N1 voucher-type highlight (routed through StepActive → MoveHighlight).
            var startIndex = vm.VoucherNumberingConfig!.SelectedIndex;
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(window);
            Assert.NotEqual(startIndex, vm.VoucherNumberingConfig!.SelectedIndex); // the highlight moved
            window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(startIndex, vm.VoucherNumberingConfig!.SelectedIndex);    // …and back

            // (b) Esc pops the config column in one press (no dropdown open) — the payment entry survives beneath.
            var columnsBefore = vm.Columns.Count;
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(columnsBefore - 1, vm.Columns.Count);
            Assert.Null(vm.VoucherNumberingConfig);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Equal(VoucherBaseType.Payment, vm.VoucherEntry!.Type.BaseType);

            // (c) F4 is still Contra — re-open the config, then F4 must open a Contra voucher, not do anything on the panel.
            window.KeyPressQwerty(PhysicalKey.F12, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.VoucherNumberingConfig, vm.CurrentScreen);
            window.KeyPressQwerty(PhysicalKey.F4, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Equal(VoucherBaseType.Contra, vm.VoucherEntry!.Type.BaseType);
        }
        finally { Cleanup(window, dir); }
    }

    // ============================== FIX-2: the REAL keyboard accept path completes a pending warn-and-confirm

    // A warn-and-confirm save cannot be finished by mouse/Tab+Space only — the shell's accept key (Ctrl+A / Enter →
    // ActivateSelected) must confirm a pending save. First accept warns (pending); a SECOND accept persists. Driven on
    // the REAL MainWindowViewModel accept entry, so it exercises the exact routing MainWindow.axaml.cs invokes.
    [AvaloniaFact]
    public void F12_config_secondAccept_confirmsPendingSave()
    {
        var (window, vm, dir) = NewWindow("Num S4 Confirm Co");
        try
        {
            var c = vm.Company!;
            var booksBegin = c.BooksBeginFrom;
            var type = new VoucherType(Guid.NewGuid(), "Confirm Journal", VoucherBaseType.Journal,
                prefixes: new[] { new VoucherNumberAffix(Guid.NewGuid(), booksBegin, "OLD/") });
            c.AddVoucherType(type);
            // A plain posted accounting voucher covered by OLD/ (no e-invoice / e-Way ⇒ warn-and-confirm, not block).
            var a = AddCashLedger(c, "Confirm Cash A", openingIsDebit: true);
            var b = AddCashLedger(c, "Confirm Cash B", openingIsDebit: false);
            var sale = new LedgerService(c).Post(new Voucher(Guid.NewGuid(), type.Id, booksBegin,
                new List<EntryLine>
                {
                    new(a.Id, Money.FromRupees(100m), DrCr.Debit),
                    new(b.Id, Money.FromRupees(100m), DrCr.Credit),
                }));
            Assert.Equal("OLD/1", c.FormatVoucherNumber(sale));

            vm.OpenVoucherNumberingConfig(type.Id);
            Pump(window);
            Assert.Equal(Screen.VoucherNumberingConfig, vm.CurrentScreen);
            vm.VoucherNumberingConfig!.Prefixes[0].Particulars = "NEW/"; // rewrites the covered voucher's number

            // FIRST accept → warns (pending); nothing persisted.
            vm.ActivateSelected();
            Assert.True(vm.VoucherNumberingConfig!.IsConfirmPending);
            Assert.Equal("OLD/", c.FindVoucherType(type.Id)!.Prefixes.Single().Particulars);
            Assert.Equal("OLD/1", c.FormatVoucherNumber(sale));

            // SECOND accept → confirms and persists (the fix routes the accept key to ConfirmSave when pending).
            vm.ActivateSelected();
            Assert.False(vm.VoucherNumberingConfig!.IsConfirmPending);
            Assert.Equal("NEW/", c.FindVoucherType(type.Id)!.Prefixes.Single().Particulars);
            Assert.Equal("NEW/1", c.FormatVoucherNumber(sale));
        }
        finally { Cleanup(window, dir); }
    }

    private static Apex.Ledger.Domain.Ledger AddCashLedger(Company c, string name, bool openingIsDebit)
    {
        var l = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName("Cash-in-Hand")!.Id,
            Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
