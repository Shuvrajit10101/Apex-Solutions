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
using Path = System.IO.Path;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 6.4 (iii) — THE TWO SOURCE ORDERS ARE CAPTURABLE, FROM THE KEYBOARD, AND WHAT IS CAPTURED IS ON DISK.</b>
///
/// <para><b>The defect, on <c>origin/main</c> 0fbf8e2.</b> <c>GstConfig.SourceOfGstRate</c> and
/// <c>SourceOfHsnSacDetails</c> have existed since schema v51 and both now steer a walk, but
/// <c>grep -rn "SourceOfGstRate\|SourceOfHsnSacDetails" src/Apex.Desktop</c> returned zero: only the canonical
/// importer and the v51 back-fill could write them.</para>
///
/// <para><b>VENDOR (R7, opened by content 2026-10-29,
/// <c>help.tallysolutions.com/india-gst-manage-hsn-code-sac-and-tax-rates-tally/</c>):</b> "The Ledger hierarchy is
/// the default option. You can change it from: F11 (Company Features) &gt; Set/Alter Company GST Rate and other
/// details &gt; Additional Configurations, select the source of GST Rate &amp; HSN/SAC."</para>
///
/// <para>Driven through the real key handlers: <c>F11</c> opens the page, the picker is focused and moved with the
/// arrow key, <c>Ctrl+A</c> accepts — and the result is read off a book loaded through a FRESH
/// <see cref="CompanyStorage"/> over the same folder.</para>
/// </summary>
public sealed class GstSourceOrderCaptureTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) Open(string name)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexSourceOrder_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Pump(window);
        return (window, vm, tempDir);
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
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

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static T? Named<T>(MainWindow w, string name) where T : Visual =>
        Descendants(w).OfType<T>().FirstOrDefault(x => x.Name == name);

    private static GstConfigViewModel OpenF11(MainWindow w, MainWindowViewModel vm)
    {
        w.KeyPressQwerty(PhysicalKey.F11, RawInputModifiers.None);
        Pump(w);
        Assert.True(vm.GstConfig is not null, "F11 did not open the company-features page.");
        return vm.GstConfig!;
    }

    private static Company ReloadFresh(string dir, string name)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var fresh = new CompanyStorage(dir);
        return fresh.Load(fresh.ListCompanies().Single(e => e.Name == name));
    }

    /// <summary>Focus a picker and pick the next value as an operator does — Enter opens the closed picker (the
    /// shell's V3 arm), the arrow key moves, Enter commits — never by setting the binding.</summary>
    private static void StepDown(MainWindow w, ComboBox box)
    {
        Assert.True(box.Focus(), $"{box.Name} cannot take keyboard focus.");
        Pump(w);
        w.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump(w);
        Assert.True(box.IsDropDownOpen, $"Enter did not open {box.Name}.");
        w.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Pump(w);
        w.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump(w);
        Assert.False(box.IsDropDownOpen, $"Enter did not commit {box.Name}.");
    }

    [AvaloniaFact]
    public void Both_source_orders_are_on_the_F11_page_default_Ledger_and_offer_exactly_the_two_vendor_orders()
    {
        const string name = "Source Order Visible Co";
        var (w, vm, dir) = Open(name);
        try
        {
            var page = OpenF11(w, vm);
            page.GstEnabled = true;
            Pump(w);

            foreach (var n in new[] { "SourceOfGstRateChoice", "SourceOfHsnSacChoice" })
            {
                var box = Named<ComboBox>(w, n);
                Assert.True(box is { IsEffectivelyVisible: true }, $"{n} is not on the F11 GST block.");
                Assert.Equal(new[] { "Ledger", "Stock Item" },
                    box!.Items.Cast<GstDetailSourceOption>().Select(o => o.Display).ToArray());
                Assert.Equal("Ledger", ((GstDetailSourceOption)box.SelectedItem!).Display);   // vendor default
            }
        }
        finally { Close(w, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE HSN ORDER, CAPTURED BY KEYSTROKE AND SAVED.</b> Arrow-down on the HSN picker, Ctrl+A, and the
    /// re-loaded book carries <c>StockItemFirst</c> for HSN while the RATE order stays <c>LedgerFirst</c> — the two
    /// are separate columns and moving one must not move the other. Re-opening F11 shows the saved value back.
    /// </summary>
    [AvaloniaFact]
    public void Arrow_down_and_Ctrl_A_save_the_hsn_order_and_leave_the_rate_order_alone()
    {
        const string name = "Source Order Hsn Co";
        var (w, vm, dir) = Open(name);
        try
        {
            var page = OpenF11(w, vm);
            page.GstEnabled = true;
            page.Gstin = GstinMaharashtra;
            page.HomeState = page.HomeStates.First(s => s.Code == "27");
            Pump(w);

            StepDown(w, Named<ComboBox>(w, "SourceOfHsnSacChoice")!);
            Assert.Equal(GstDetailSource.StockItemFirst, page.SelectedSourceOfHsnSac!.Value);

            w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(w);
            Assert.True(vm.Company!.GstEnabled, $"Ctrl+A did not apply the GST block: {page.Message}");

            var reloaded = ReloadFresh(dir, name);
            Assert.Equal(GstDetailSource.StockItemFirst, reloaded.Gst!.SourceOfHsnSacDetails);
            Assert.Equal(GstDetailSource.LedgerFirst, reloaded.Gst.SourceOfGstRate);

            var reopened = OpenF11(w, vm);
            Assert.NotSame(page, reopened);
            Assert.Equal(GstDetailSource.StockItemFirst, reopened.SelectedSourceOfHsnSac!.Value);
            Assert.Equal(GstDetailSource.LedgerFirst, reopened.SelectedSourceOfGstRate!.Value);
        }
        finally { Close(w, dir); }
    }

    /// <summary>The RATE order, the mirror case: captured by keystroke, saved, and the HSN order untouched.</summary>
    [AvaloniaFact]
    public void Arrow_down_and_Ctrl_A_save_the_rate_order_and_leave_the_hsn_order_alone()
    {
        const string name = "Source Order Rate Co";
        var (w, vm, dir) = Open(name);
        try
        {
            var page = OpenF11(w, vm);
            page.GstEnabled = true;
            page.Gstin = GstinMaharashtra;
            page.HomeState = page.HomeStates.First(s => s.Code == "27");
            Pump(w);

            StepDown(w, Named<ComboBox>(w, "SourceOfGstRateChoice")!);
            w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(w);

            var reloaded = ReloadFresh(dir, name);
            Assert.Equal(GstDetailSource.StockItemFirst, reloaded.Gst!.SourceOfGstRate);
            Assert.Equal(GstDetailSource.LedgerFirst, reloaded.Gst.SourceOfHsnSacDetails);
        }
        finally { Close(w, dir); }
    }
}
