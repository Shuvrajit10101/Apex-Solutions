using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// The entry-mode keystrokes and the REALISED accounting-invoice surface, driven through the REAL
/// <see cref="MainWindow"/> tunnel handler (<c>window.KeyPressQwerty</c>) and the REAL XAML — never by asserting a
/// binding exists in isolation:
/// <list type="bullet">
/// <item><b>Ctrl+H</b> ("Change Mode") cycles a Sales voucher As Voucher → Item Invoice → Accounting Invoice →
/// As Voucher, and is a <b>no-op AND UNHANDLED</b> on a non-invoiceable entry (Payment).</item>
/// <item><b>Ctrl+I</b> stays the 2-way As-Voucher ↔ Item-Invoice toggle (regression guard).</item>
/// <item>On a <b>Purchase</b> the accounting mode is DEFERRED, so Ctrl+H must never reach it.</item>
/// <item>The rendered tree actually swaps grids: the plain Dr/Cr grid hides and the Particulars grid (with a
/// working remove affordance) shows.</item>
/// </list>
/// Proves the Ctrl+H block consumes the key ONLY on an invoiceable entry, so the b8c617e keystroke arbitration is
/// undisturbed.
/// </summary>
public sealed class ServiceAccountingInvoiceKeyboardTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexServiceKeys_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        vm.NewCompanyName = "Svc Keys Co";
        vm.CreateCompany();
        return (window, vm, tempDir);
    }

    /// <summary>Flushes bindings and forces a layout pass so the voucher-entry DataTemplate is really realised.</summary>
    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(1280, 800));
        window.Arrange(new Rect(0, 0, 1280, 800));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Raises a REAL tunnelled KeyDown on the window and reports whether the app CONSUMED it. This is the
    /// observation <c>KeyPressQwerty</c> cannot give: the b8c617e non-swallowing guarantee is about
    /// <c>e.Handled</c>, not about whether some view-model field happened to move.
    /// </summary>
    private static bool KeyWasHandled(MainWindow window, Key key, KeyModifiers modifiers)
    {
        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = window,
        };
        window.RaiseEvent(args);
        return args.Handled;
    }

    /// <summary>Every string a realised, EFFECTIVELY VISIBLE control is showing. Avalonia keeps collapsed controls in
    /// the visual tree, so an unfiltered walk would make every negative assertion vacuous.</summary>
    private static IEnumerable<string> ShownText(Window window)
    {
        foreach (var v in window.GetVisualDescendants().Where(x => x.IsEffectivelyVisible))
            switch (v)
            {
                case TextBlock tbl:
                    if (tbl.Text is { } t) yield return t;
                    // Several captions are bound through Run inlines, which a naive TextBlock.Text probe misses.
                    if (tbl.Inlines is { } inlines)
                        foreach (var run in inlines.OfType<Avalonia.Controls.Documents.Run>())
                            if (run.Text is { } rt) yield return rt;
                    break;
                case ContentControl { Content: string s }: yield return s; break;
            }
    }

    [AvaloniaFact]
    public void CtrlH_cycles_three_modes_on_a_sales_voucher()
    {
        var (window, vm, _) = NewWindow();
        vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = vm.VoucherEntry!;
        Assert.True(entry.IsAsVoucherMode);

        window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
        Assert.True(entry.IsItemInvoice);

        window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
        Assert.True(entry.IsAccountingInvoice);

        window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
        Assert.True(entry.IsAsVoucherMode);
    }

    [AvaloniaFact]
    public void CtrlI_stays_a_two_way_item_toggle()
    {
        var (window, vm, _) = NewWindow();
        vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = vm.VoucherEntry!;

        window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Control);
        Assert.True(entry.IsItemInvoice);

        window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Control);
        Assert.True(entry.IsAsVoucherMode);   // 2-way flip back, never into Accounting mode
        Assert.False(entry.IsAccountingInvoice);
    }

    /// <summary>
    /// Ctrl+H must not swallow the key app-wide — the b8c617e guarantee. The gate widened for G-6 (Single Entry now
    /// lives on Ctrl+H for Contra/Payment/Receipt), so the negative case moved to a <b>Journal</b>: it has no
    /// alternative entry mode at all, so the key must fall through UNHANDLED there.
    /// <para><b>This test bites on <c>e.Handled</c>, deliberately.</b> An earlier version asserted only
    /// <c>IsAsVoucherMode</c>, which <c>ChangeMode()</c> guards on its own — so deleting the gate from the tunnel
    /// block left it GREEN. Observing consumption is what actually locks the behaviour.</para>
    /// </summary>
    [AvaloniaFact]
    public void CtrlH_is_unhandled_on_a_voucher_with_no_alternative_mode()
    {
        var (window, vm, _) = NewWindow();
        vm.OpenVoucher(VoucherBaseType.Journal);
        var entry = vm.VoucherEntry!;
        Assert.False(entry.CanBeItemInvoice);
        Assert.False(entry.CanBeSingleEntry);
        Assert.False(vm.IsChangeModeEntry);

        Assert.False(KeyWasHandled(window, Key.H, KeyModifiers.Control));  // NOT swallowed — falls through
        Assert.True(entry.IsAsVoucherMode);                                // and nothing changed

        // Contrast 1: on an invoiceable entry the SAME key IS consumed (so the guard above is not vacuous).
        vm.OpenVoucher(VoucherBaseType.Sales);
        Assert.True(vm.IsChangeModeEntry);
        Assert.True(KeyWasHandled(window, Key.H, KeyModifiers.Control));
        Assert.True(vm.VoucherEntry!.IsItemInvoice);
    }

    /// <summary>
    /// <b>G-6 end-to-end through the real key tunnel.</b> The view-model change alone was not enough: Ctrl+H was
    /// gated on <c>IsInvoiceableEntry</c> (Purchase/Sales only), so Single Entry — though implemented and unit
    /// tested — was unreachable from the keyboard on the three vouchers it belongs to. This asserts the key really
    /// arrives, on a Payment, and that the rendered tree swaps to the Single-Entry grid.
    /// </summary>
    [AvaloniaFact]
    public void CtrlH_on_a_payment_enters_single_entry_mode()
    {
        var (window, vm, _) = NewWindow();
        vm.OpenVoucher(VoucherBaseType.Payment);
        var entry = vm.VoucherEntry!;
        Assert.True(entry.CanBeSingleEntry);
        Assert.True(vm.IsChangeModeEntry);
        Pump(window);
        Assert.Contains("Particulars (Ledger)", ShownText(window));   // the Dr/Cr grid header, in Double Entry

        Assert.True(KeyWasHandled(window, Key.H, KeyModifiers.Control));
        Assert.True(entry.IsSingleEntry);
        Assert.False(entry.ShowPlainDrCrGrid);
        Pump(window);

        // The realised tree really swapped: the Account field is up and the Dr/Cr grid header is gone.
        var shown = ShownText(window);
        Assert.Contains("Account", shown);
        Assert.DoesNotContain("Particulars (Ledger)", shown);

        // Payment polarity, surfaced to the operator (BOOK p.32).
        Assert.Contains("Account is credited", entry.SingleEntryModeHint);

        // …and Ctrl+H flips straight back.
        Assert.True(KeyWasHandled(window, Key.H, KeyModifiers.Control));
        Assert.False(entry.IsSingleEntry);
        Assert.True(entry.ShowPlainDrCrGrid);
    }

    /// <summary>
    /// <b>G-7 — this test was inverted, deliberately.</b> It previously asserted that Ctrl+H on a Purchase could
    /// NEVER reach Accounting mode, because the purchase-side accounting invoice was deferred scope: it silently
    /// dropped the §194J TDS carve-out and mis-evaluated RCM, both because the detectors read the (empty) plain
    /// <c>Lines</c> collection.
    ///
    /// <para>That precondition has been met — TDS and RCM detection now read the Particulars lines
    /// (<c>DetectAccountingTdsShape</c> / <c>DetectAccountingRcmShape</c>), and
    /// <c>PurchaseAccountingInvoiceTdsTests</c> proves §194J still fires, still rounds to the rupee, still records a
    /// below-threshold assessment and still honours the decline sentinel. So the correct assertion is now the
    /// opposite: Ctrl+H on a Purchase cycles all THREE modes, exactly as on a Sales (BOOK p.33; SG p.80).</para>
    /// </summary>
    [AvaloniaFact]
    public void CtrlH_on_a_purchase_cycles_all_three_modes()
    {
        var (window, vm, _) = NewWindow();
        vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = vm.VoucherEntry!;
        Assert.True(entry.CanBeItemInvoice);
        Assert.True(entry.CanBeAccountingInvoice);

        Assert.True(entry.IsAsVoucherMode);
        window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
        Assert.True(entry.IsItemInvoice);
        window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
        Assert.True(entry.IsAccountingInvoice);
        Assert.True(entry.ShowParticularsGrid);
        window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
        Assert.True(entry.IsAsVoucherMode);

        // The 3-way Purchase cycle now has period 3, so six presses still land back on As Voucher.
        for (var i = 0; i < 6; i++) window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
        Assert.True(entry.IsAsVoucherMode);
    }

    /// <summary>
    /// BLOCKER-2 rendered lock: the four plain-grid gates were repointed from <c>!IsItemInvoice</c> to
    /// <c>IsAsVoucherMode</c>. Reverting any of them renders the plain Dr/Cr grid UNDERNEATH the accounting overlay
    /// — two live grids on one screen. Previously uncovered; this asserts on the REALISED tree.
    /// </summary>
    [AvaloniaFact]
    public void AccountingMode_hides_the_plain_DrCr_grid_and_shows_Particulars()
    {
        var (window, vm, _) = NewWindow();
        vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = vm.VoucherEntry!;
        Pump(window);
        Assert.Contains("Particulars (Ledger)", ShownText(window));   // the plain header, in As-Voucher mode
        Assert.Contains("Dr/Cr", ShownText(window));

        entry.ChangeMode();   // Item Invoice
        entry.ChangeMode();   // Accounting Invoice
        Assert.True(entry.IsAccountingInvoice);
        Pump(window);

        // The plain grid's own "Dr/Cr" column header is the sentinel unique to it — the accounting Particulars
        // header shares the "Particulars (Ledger)" caption on purpose.
        Assert.DoesNotContain("Dr/Cr", ShownText(window));
        Assert.DoesNotContain("Difference: ", ShownText(window));
        Assert.Contains("Particulars (Ledger)", ShownText(window));   // now the SERVICE grid's header
        Assert.Contains("Services Total ₹ ", ShownText(window));      // FIX-7 caption, mode-aware
    }

    /// <summary>
    /// FIX-5 rendered lock: <c>RemoveAccountingInvoiceLine</c> had zero call sites and the grid's declared 40px
    /// remove column was EMPTY. The affordance must exist in the realised tree and actually drop the row.
    /// </summary>
    [AvaloniaFact]
    public void Particulars_grid_ships_a_working_remove_affordance()
    {
        var (window, vm, _) = NewWindow();
        vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = vm.VoucherEntry!;
        entry.ChangeMode();
        entry.ChangeMode();
        Assert.True(entry.IsAccountingInvoice);
        entry.AddAccountingInvoiceLine();
        Pump(window);

        var removes = window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsEffectivelyVisible && b.DataContext is AccountingInvoiceLineViewModel)
            .ToList();
        Assert.NotEmpty(removes);                       // before: the 40px column shipped no child at all

        var before = entry.AccountingInvoiceLines.Count;
        var target = (AccountingInvoiceLineViewModel)removes[0].DataContext!;
        removes[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump(window);

        Assert.Equal(before - 1, entry.AccountingInvoiceLines.Count);
        Assert.DoesNotContain(target, entry.AccountingInvoiceLines);
    }
}
