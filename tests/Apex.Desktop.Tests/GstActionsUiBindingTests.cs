using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Binding-reachability locks</b> for the Phase-9 UI-2 action screens (RQ-24 / RQ-25).
///
/// <para>A view-model guard is worthless if no control surfaces the property it guards on, and a command is worthless
/// if no button invokes it. Both shipped: the e-Invoice <b>Record failure</b> button was permanently dead (the VM
/// demands an <c>ErrorCode</c>; no XAML control bound it, so every click failed with advice the user could not
/// follow), e-Way's statutory <b>Close</b> was implemented and command-generated but bound to no button at all, and
/// <c>CancelReasonCode</c> was hard-coded to "1" on BOTH screens with no control to pick it — so the recorded
/// statutory reason could never reflect what was actually filed.</para>
///
/// <para>These drive the <b>real MainWindow XAML</b> headlessly and assert the affordance genuinely exists. This is
/// the class of defect a view-model-only test can never see.</para>
/// </summary>
public sealed class GstActionsUiBindingTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    /// <summary>Boots the real window on a Regular-GST company with e-invoicing + e-Way switched on (the ER-13 gate
    /// the action screens sit behind), then lays it out so the page DataTemplates are actually realised.</summary>
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow(string name)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexGstBind_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();

        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        c.Gst!.EInvoicingEnabled = true;
        c.Gst.EInvoiceApplicableFrom = FyStart;
        c.Gst.EInvoiceApplicabilityOverride = true;
        c.Gst.EWayBillEnabled = true;
        c.Gst.EWayApplicableFrom = FyStart;
        storage.Save(c);
        vm.ShowGateway();

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Pump(window);
        return (window, vm, tempDir);
    }

    /// <summary>Flushes bindings and forces a layout pass so a freshly-shown page's DataTemplate is realised.</summary>
    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(1280, 800));
        window.Arrange(new Rect(0, 0, 1280, 800));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>True iff SOME realised control actually surfaces <paramref name="sentinel"/> — i.e. a control is really
    /// bound to the view-model property the sentinel was just written to. A TextBox or a ComboBox both count: the fix
    /// is "let the user supply this value", not "use this specific widget".</summary>
    private static bool SomeControlSurfaces(Window window, string sentinel) =>
        window.GetVisualDescendants().Any(x =>
            (x is TextBox tb && tb.Text == sentinel)
            || (x is ComboBox cb && cb.SelectedItem as string == sentinel));

    private static void Cleanup(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best-effort */ }
    }

    // ================================================================ FIX 3: the dead "Record failure" button

    /// <summary>
    /// (FIX 3) The e-Invoice <b>error code</b> and <b>error message</b> must be reachable from the UI. The VM refuses
    /// <c>RecordFailure</c> without an <c>ErrorCode</c> — and nothing bound it, so the button could never succeed.
    /// </summary>
    [AvaloniaFact]
    public void Einvoice_error_code_and_message_are_surfaced_so_record_failure_is_reachable()
    {
        var (window, vm, dir) = NewWindow("EInv Binding Co");
        try
        {
            vm.OpenGenerateEInvoice();
            Pump(window);
            var page = vm.GenerateEInvoice!;
            Assert.NotNull(page);

            page.ErrorCode = "ERR-2172";
            page.ErrorMessage = "Duplicate IRN for the document";
            Pump(window);

            Assert.True(SomeControlSurfaces(window, "ERR-2172"),
                "no control binds ErrorCode — the Record-failure button is permanently dead");
            Assert.True(SomeControlSurfaces(window, "Duplicate IRN for the document"),
                "no control binds ErrorMessage — the IRP's rejection text can never be recorded");
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ================================================================ FIX 7: the hard-coded cancellation reason

    /// <summary>(FIX 7) The e-Invoice cancellation reason code must be pickable — it was hard-coded to "1", so the
    /// recorded statutory reason never reflected what was filed.</summary>
    [AvaloniaFact]
    public void Einvoice_cancel_reason_code_is_surfaced_for_the_user_to_pick()
    {
        var (window, vm, dir) = NewWindow("EInv Reason Co");
        try
        {
            vm.OpenGenerateEInvoice();
            Pump(window);
            var page = vm.GenerateEInvoice!;

            page.CancelReasonCode = "3";
            Pump(window);

            Assert.True(SomeControlSurfaces(window, "3"),
                "no control binds CancelReasonCode — the recorded statutory reason is always the hard-coded '1'");
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>(FIX 7) The same on the e-Way screen.</summary>
    [AvaloniaFact]
    public void Eway_cancel_reason_code_is_surfaced_for_the_user_to_pick()
    {
        var (window, vm, dir) = NewWindow("EWay Reason Co");
        try
        {
            vm.OpenGenerateEWayBill();
            Pump(window);
            var page = vm.GenerateEWayBill!;

            page.CancelReasonCode = "3";
            Pump(window);

            Assert.True(SomeControlSurfaces(window, "3"),
                "no control binds CancelReasonCode — the recorded statutory reason is always the hard-coded '1'");
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ================================================================ FIX 4: the unreachable statutory Close

    /// <summary>
    /// (FIX 4) e-Way's statutory <b>Close</b> must be reachable from the shipped UI: <c>CloseAction</c> was
    /// implemented and command-generated, but no button invoked it and it was not on the Ctrl+A path — so the
    /// mechanism existed only in the view model.
    /// </summary>
    [AvaloniaFact]
    public void Eway_close_is_bound_to_a_real_button_on_the_action_row()
    {
        var (window, vm, dir) = NewWindow("EWay Close Binding Co");
        try
        {
            vm.OpenGenerateEWayBill();
            Pump(window);
            var page = vm.GenerateEWayBill!;

            var closeButton = window.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => ReferenceEquals(b.Command, page.CloseActionCommand));

            Assert.True(closeButton is not null,
                "no button invokes CloseActionCommand — the statutory closure mechanism is unreachable from the UI");
        }
        finally { window.Close(); Cleanup(dir); }
    }
}
