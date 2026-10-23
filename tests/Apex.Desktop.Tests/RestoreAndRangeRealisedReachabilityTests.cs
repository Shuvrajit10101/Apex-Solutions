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
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Census 13.2 and 12.6 — THE LAST MILE: controls an operator can actually reach.</b>
///
/// <para>🔴 <b>WHY THIS FILE EXISTS, AND IT IS NOT DEFENCE IN DEPTH — IT IS THE ONLY TEST THAT PINS WHAT EACH
/// ROW'S CENSUS CLAUSE ACTUALLY SAID.</b> Row 13.2's gap was not "the engine cannot retarget a restore" — the
/// engine always could, and <c>CompanyBackup.Restore</c> has taken an arbitrary target path all along. The gap
/// the census recorded was literally a <b>markup</b> gap: <i>"the target-name property has zero bindings in the
/// XAML, so even with a company open the target cannot be redirected"</i>. <c>RestoreOnFreshInstallTests</c>
/// proves the view model and the engine retarget correctly, but every one of those tests sets
/// <c>TargetCompanyName</c> from C#. <b>Delete the <c>TextBox</c> from <c>MainWindow.axaml</c> and they all stay
/// green while the operator is back to exactly the position the census described.</b> That is this project's
/// most-repeated defect — the one caught on census 13.6, where removing the HTML export radio left eight
/// view-model tests green and the format unreachable by any user.</para>
///
/// <para>So these tests realise the <b>real</b> <see cref="MainWindow"/> with the <b>real</b> panels open, and
/// ask the realised controls what they offer — the idiom <c>ExportFormatRealisedReachabilityTests</c>
/// established and <c>MultiAccountPrintRealisedReachabilityTests</c> followed for row 12.6's other half. They
/// read the realised control rather than the markup, so they also bite the refactor's own failure mode: a
/// control that renders but is bound to nothing.</para>
/// </summary>
public sealed class RestoreAndRangeRealisedReachabilityTests
{
    /// <summary>Flushes bindings and forces a layout pass so the panel's DataTemplate is realised.</summary>
    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(1280, 800));
        window.Arrange(new Rect(0, 0, 1280, 800));
        Dispatcher.UIThread.RunJobs();
    }

    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "ApexRestoreRangeRealised_" + Guid.NewGuid().ToString("N"));

    /// <summary>The one realised TextBox bound to the restore panel's editable target name.</summary>
    private static TextBox[] RealisedTargetNameBoxes(Window window)
        => window.GetVisualDescendants()
                 .OfType<TextBox>()
                 .Where(t => t.DataContext is RestoreCompanyViewModel
                             && t.GetValue(TextBox.TextProperty) is not null
                             && IsBoundTo(t, nameof(RestoreCompanyViewModel.TargetCompanyName)))
                 .ToArray();

    /// <summary>
    /// Whether the control's <c>Text</c> really tracks that view-model property, established by MOVING the
    /// property and seeing the realised control follow. A control bound to nothing, or bound to a different
    /// property, fails this — which is the point.
    /// </summary>
    private static bool IsBoundTo(TextBox box, string _)
    {
        if (box.DataContext is not RestoreCompanyViewModel vm) return false;
        var original = vm.TargetCompanyName;
        try
        {
            var probe = "Binding Probe " + Guid.NewGuid().ToString("N")[..8];
            vm.TargetCompanyName = probe;
            Dispatcher.UIThread.RunJobs();
            return box.Text == probe;
        }
        finally
        {
            vm.TargetCompanyName = original;
            Dispatcher.UIThread.RunJobs();
        }
    }

    // ------------------------------------------------------------------ 13.2

    /// <summary>
    /// <b>THE OPERATOR-FACING ASSERTION FOR 13.2.</b> On a machine with <b>zero</b> companies the restore panel
    /// opens and realises an <b>editable</b> target-name box, and typing into the realised control moves the
    /// view model. Both directions are asserted because each fails differently: a read-only
    /// <see cref="TextBlock"/> would satisfy "something is displayed" and still leave the target un-redirectable,
    /// and a <c>OneWay</c> binding would display the name and silently discard what the operator typed.
    /// </summary>
    [AvaloniaFact]
    public void With_no_company_the_restore_target_is_realised_as_an_editable_box_that_moves_the_view_model()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        Assert.Null(vm.Company);           // a genuinely fresh install — the case the row is for

        vm.OpenRestoreCompany();
        Assert.NotNull(vm.RestoreCompanyPanel);

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Pump(window);

        var boxes = RealisedTargetNameBoxes(window);
        Assert.True(boxes.Length >= 1,
            "No realised, TargetCompanyName-bound TextBox exists, so census 13.2's own clause — the target " +
            "cannot be redirected — is still true for the operator however well the view model behaves.");

        var box = boxes[0];
        Assert.False(box.IsReadOnly, "a read-only box cannot redirect a restore");
        Assert.True(box.IsEffectivelyEnabled, "a disabled box cannot redirect a restore");

        // Operator -> view model, through the realised control.
        box.Text = "Recovered Books";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Recovered Books", vm.RestoreCompanyPanel!.TargetCompanyName);

        // And the panel reacted as it would for a real keystroke: this name is not on disk, so it is a CREATE.
        Assert.False(vm.RestoreCompanyPanel.TargetExists);
        Assert.Contains("CREATED", vm.RestoreCompanyPanel.TargetEffectNote, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ 12.6

    /// <summary>
    /// <b>THE OPERATOR-FACING ASSERTION FOR 12.6's MULTI-VOUCHER HALF.</b> All three vendor-attested selection
    /// axes — voucher type, party, period — are realised as controls bound to the panel, not merely present as
    /// view-model properties. Dropping any one of the three from the markup would leave
    /// <c>MultiVoucherPrintTests</c> entirely green while making that axis unreachable.
    /// </summary>
    [AvaloniaFact]
    public void All_three_multi_voucher_axes_are_realised_as_controls_on_the_panel()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        vm.LoadRobertDemo();
        vm.OpenMultiVoucherPrint();
        Assert.NotNull(vm.MultiVoucherPrint);

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Pump(window);

        var onPanel = window.GetVisualDescendants()
                            .Where(v => v is Control c && c.DataContext is MultiVoucherPrintViewModel)
                            .ToArray();

        var combos = onPanel.OfType<ComboBox>().ToArray();
        Assert.True(combos.Length >= 2,
            $"expected a voucher-type chooser and a party chooser realised on the panel; found {combos.Length}");

        var periodBoxes = onPanel.OfType<TextBox>().ToArray();
        Assert.True(periodBoxes.Length >= 2,
            $"expected realised From and To period boxes; found {periodBoxes.Length}");

        // The period boxes really are the panel's period: move the view model, watch the controls follow.
        var panel = vm.MultiVoucherPrint!;
        panel.PeriodFromText = "01-05-2025";
        panel.PeriodToText = "31-05-2025";
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(periodBoxes, t => t.Text == "01-05-2025");
        Assert.Contains(periodBoxes, t => t.Text == "31-05-2025");

        // And the voucher-type chooser is populated, so it can actually be used.
        Assert.NotEmpty(panel.VoucherTypes);
        Assert.Contains(combos, c => c.ItemCount > 0);
    }

    /// <summary>
    /// The panel is reachable the way the operator reaches it — by the hub row, not by calling the opener. The
    /// label is the vendor's own screen name, <i>"Multi-Voucher Printing"</i>
    /// (<c>help.tallysolutions.com/gcc-sales-and-print-fta-tally/</c>).
    /// </summary>
    [AvaloniaFact]
    public void The_hub_row_opens_the_multi_voucher_panel()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        vm.LoadRobertDemo();
        vm.ShowStatementsOfAccountsMenu();

        var row = vm.Menu.SingleOrDefault(m => m.IsSelectable && m.Label == "Multi-Voucher Printing");
        Assert.NotNull(row);

        row!.Activate();
        Assert.NotNull(vm.MultiVoucherPrint);
    }
}
