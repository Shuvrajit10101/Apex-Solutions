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
public sealed class RestoreAndRangeRealisedReachabilityTests : IDisposable
{
    /// <summary>
    /// Every temp directory this class hands out, so <see cref="Dispose"/> can take them all away again.
    ///
    /// <para>🔴 <b>WHY THIS EXISTS: the first version of this file leaked one directory per test, and that is
    /// measurably not harmless here.</b> A sweep of <c>%TEMP%</c> during this slice found <b>3,716</b>
    /// leftover <c>Apex*</c> fixture directories on a disk at <b>92% full</b> — <c>ApexServiceKeys</c> 1,584,
    /// <c>ApexMultiParty</c> 1,110 and <c>ApexMultiPrint</c> 900 — and 18 of them were this file's. The
    /// precedent this file follows, <c>MultiAccountPrintRealisedReachabilityTests</c>, deletes its directory
    /// in a <c>finally</c>; copying its idiom and not its cleanup was an oversight, not a decision.</para>
    /// </summary>
    private readonly List<string> _temps = new();

    /// <summary>Removes every fixture directory this class created. Never fails a test over cleanup.</summary>
    public void Dispose()
    {
        foreach (var d in _temps)
        {
            try { if (Directory.Exists(d)) Directory.Delete(d, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Flushes bindings and forces a layout pass so the panel's DataTemplate is realised.</summary>
    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(1280, 800));
        window.Arrange(new Rect(0, 0, 1280, 800));
        Dispatcher.UIThread.RunJobs();
    }

    private string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "ApexRestoreRangeRealised_" + Guid.NewGuid().ToString("N"));
        _temps.Add(d);
        return d;
    }

    /// <summary>The one realised TextBox bound to the restore panel's editable target name.</summary>
    private static TextBox[] RealisedTargetNameBoxes(Window window)
        => window.GetVisualDescendants()
                 .OfType<TextBox>()
                 .Where(t => t.DataContext is RestoreCompanyViewModel
                             && t.GetValue(TextBox.TextProperty) is not null
                             && TracksTargetCompanyName(t))
                 .ToArray();

    /// <summary>
    /// Whether the control's <c>Text</c> really tracks <see cref="RestoreCompanyViewModel.TargetCompanyName"/>,
    /// established by MOVING the property and seeing the realised control follow. A control bound to nothing, or
    /// bound to a different property, fails this — which is the point.
    ///
    /// <para>(It used to be <c>IsBoundTo(TextBox, string _)</c>, which took a property name and ignored it — the
    /// probe only ever moves this one property, so the name now says so. A12 review w6, F9.)</para>
    /// </summary>
    private static bool TracksTargetCompanyName(TextBox box)
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
    /// <b>THE OPERATOR-FACING ASSERTION FOR 13.2, AND THE ONE THAT PINS THE CELL'S OWN CLAUSE.</b> The restore
    /// panel realises an <b>editable, two-way-bound</b> target-name box, and typing into the realised control
    /// moves the view model. Both directions are asserted because each fails differently: a read-only
    /// <see cref="TextBlock"/> would satisfy "something is displayed" and still leave the target
    /// un-redirectable (which is exactly what shipped before — <c>DocumentTitle</c> and <c>TargetPath</c>
    /// <i>were</i> bound, read-only), and a <c>OneWay</c> binding would display the name and silently discard
    /// what the operator typed.
    ///
    /// <para>Opened with a company, because that is the state in which the cell's clause was written
    /// (<i>"even with a company open the target cannot be redirected"</i>) and because the panel's template
    /// only realises inside the cascade. The <b>zero-company</b> half of the row is proved at view-model level
    /// by <c>RestoreOnFreshInstallTests</c>, which opens the panel with nothing open at all.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_restore_target_is_realised_as_an_editable_box_that_moves_the_view_model()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        vm.LoadRobertDemo();
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

        // Operator -> view model, through the realised control. The name is deliberately one that is NOT on
        // disk, so the panel must also re-answer the CREATE/REPLACE question off the realised keystroke.
        box.Text = "Recovered Books";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Recovered Books", vm.RestoreCompanyPanel!.TargetCompanyName);
        Assert.False(vm.RestoreCompanyPanel.TargetExists);
        Assert.Contains("CREATED", vm.RestoreCompanyPanel.TargetEffectNote, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>F4 (A12 review w6): THE VENDOR'S OWN ROUTE, WITH NO COMPANY OPEN, THROUGH THE REAL KEY HANDLER.</b>
    /// <i>"Press Alt+Y (Data) &gt; Backup &amp; Restore &gt; Restore"</i>
    /// (<c>help.tallysolutions.com/restore-company-data/</c>). A realised <see cref="MainWindow"/> on Company
    /// Select with nothing on the machine; Alt+Y is PRESSED, then the ordinary arrows and Enter. The key arm used
    /// to require an open company and the Alt+Y badge was built disabled without one, so on exactly this screen
    /// the route did nothing — restore was reachable only through the invented Company Select row (IV-140). The
    /// previous test of this called <c>ShowDataMenu()</c> directly, which no key could reach.
    /// </summary>
    [AvaloniaFact]
    public void Alt_Y_with_no_company_open_reaches_restore_by_the_keyboard()
    {
        var dir = TempDir();
        var vm = new MainWindowViewModel(new CompanyStorage(Path.Combine(dir, "Companies")));
        vm.ShowCompanySelect();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            Pump(window);
            Assert.Null(vm.Company);
            Assert.True(vm.ButtonBar.Single(b => b.Key == "Alt+Y").Enabled,
                "the Alt+Y badge is greyed on Company Select, so the vendor's route is not offered there");

            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.Alt);
            Pump(window);
            Assert.NotEqual(Screen.CompanySelect, vm.CurrentScreen);
            Assert.Contains(vm.Columns, c => c.Title == "Backup / Restore");

            for (var i = 0; i < 8 && vm.RestoreCompanyPanel is null; i++)
            {
                var selected = vm.Menu.FirstOrDefault(m => m.IsSelected)?.Label;
                window.KeyPressQwerty(
                    selected == "Restore Company" ? PhysicalKey.Enter : PhysicalKey.ArrowDown, RawInputModifiers.None);
                Pump(window);
            }

            Assert.NotNull(vm.RestoreCompanyPanel);
            Assert.Equal(Screen.RestoreCompany, vm.CurrentScreen);
            AssertRestorePanelIsDrawn(window);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 🔴 <b>THE FRESH-INSTALL DOOR, DRAWN.</b> Company Select with nothing on the machine; the operator arrows to
    /// the "Restore Company" row (IV-140) and presses Enter on the realised window — and the panel must be ON
    /// SCREEN, its target box realised and editable. Found while fixing F4: the row set
    /// <c>RestoreCompanyPanel</c> (every earlier test, and the reviewer's own probe P6, stopped there) but the
    /// shell was still in pre-company menu mode, where the cascade that draws panel columns is hidden and
    /// <c>IsMenuScreen</c> hid the menu too, so the operator saw an empty work area.
    /// </summary>
    [AvaloniaFact]
    public void The_company_select_restore_row_draws_the_panel_with_no_company_open()
    {
        var dir = TempDir();
        var vm = new MainWindowViewModel(new CompanyStorage(Path.Combine(dir, "Companies")));
        vm.ShowCompanySelect();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            Pump(window);
            for (var i = 0; i < 12 && vm.RestoreCompanyPanel is null; i++)
            {
                var selected = vm.Menu.FirstOrDefault(m => m.IsSelected)?.Label;
                window.KeyPressQwerty(
                    selected == "Restore Company" ? PhysicalKey.Enter : PhysicalKey.ArrowDown, RawInputModifiers.None);
                Pump(window);
            }

            Assert.NotNull(vm.RestoreCompanyPanel);
            AssertRestorePanelIsDrawn(window);

            // Esc leaves it again, back to Company Select — not stranded on an empty cascade.
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.CompanySelect, vm.CurrentScreen);
            Assert.Contains(vm.Menu, m => m.Label == "Restore Company");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>The restore panel's target box is realised, VISIBLE and editable on the window — the panel is on
    /// screen, not merely constructed.</summary>
    private static void AssertRestorePanelIsDrawn(Window window)
    {
        var boxes = RealisedTargetNameBoxes(window).Where(b => b.IsEffectivelyVisible).ToArray();
        Assert.True(boxes.Length >= 1,
            "The restore panel is open in the view model but no visible target box is realised on the window — "
          + "the operator is looking at an empty work area.");
        Assert.False(boxes[0].IsReadOnly);
        Assert.True(boxes[0].IsEffectivelyEnabled);
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
    /// 🔴 <b>F6 (A12 review w6): the panel's PRINT action, through the real key and the real button.</b> No test
    /// touched <c>PrintMultiVoucherJob</c> or the Ctrl+A arm that calls it, so deleting either left the suite
    /// green. Ctrl+A is pressed on the realised window and the Print button is clicked; each must open the print
    /// preview over the panel's range, and the job's PDF must carry one sheet per voucher at least.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_A_and_the_print_button_print_the_panels_range()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        vm.LoadRobertDemo();
        vm.OpenMultiVoucherPrint();
        var panel = vm.MultiVoucherPrint!;
        Assert.True(panel.MatchCount > 1, "the demo company must have several vouchers in its period");

        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);
            Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
            Assert.NotNull(vm.PrintPreview);
            var pages = CountOccurrences(System.Text.Encoding.Latin1.GetString(vm.PrintPreview!.PdfBytes), "/Type /Page ");
            Assert.True(pages >= panel.MatchCount, $"{panel.MatchCount} vouchers printed onto {pages} sheets");

            // Back to the panel, and the BUTTON this time.
            vm.Back();
            Pump(window);
            Assert.Equal(Screen.MultiVoucherPrint, vm.CurrentScreen);
            var printButton = window.GetVisualDescendants().OfType<Button>()
                .Single(b => b.DataContext is MultiVoucherPrintViewModel && (b.Content as string) == "Print (Ctrl+A)");
            Assert.True(printButton.IsEffectivelyEnabled);
            var before = vm.PrintPreview;
            printButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Pump(window);
            Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
            Assert.NotNull(vm.PrintPreview);
            Assert.NotSame(before, vm.PrintPreview);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// <b>F7 (A12 review w6): a CANCELLED voucher is marked in the realised match list</b>, not only on the paper.
    /// The row template bound only the date, number, party and type, so the list the operator reads before printing
    /// showed a withdrawn voucher looking live.
    /// </summary>
    [AvaloniaFact]
    public void A_cancelled_voucher_is_marked_in_the_realised_match_list()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        vm.LoadRobertDemo();
        var company = vm.Company!;
        var target = company.Vouchers.OrderBy(v => v.Date).First();
        new Apex.Ledger.Services.LedgerService(company).Cancel(target.Id);
        vm.OpenMultiVoucherPrint();

        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            Pump(window);
            var rowTexts = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.DataContext is MultiVoucherRowViewModel && t.IsEffectivelyVisible)
                .Select(t => t.Text ?? string.Empty)
                .ToArray();
            Assert.NotEmpty(rowTexts);
            Assert.Single(rowTexts, t => t.StartsWith("(Cancelled)", StringComparison.Ordinal));
        }
        finally
        {
            window.Close();
        }
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int n = 0;
        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
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

        // Selected-then-drilled, the idiom MultiAccountPrintReachabilityTests established for this hub —
        // the operator's actual gesture, not a direct call to the opener.
        var hub = vm.Columns[^1];
        var found = false;
        for (var i = 0; i < hub.Items.Count; i++)
            if (hub.Items[i].IsSelectable && hub.Items[i].Label == "Multi-Voucher Printing")
            {
                hub.SetSelected(i);
                found = true;
                break;
            }

        Assert.True(found,
            "Reports → Statements of Accounts carries no \"Multi-Voucher Printing\" item — the panel is " +
            "unreachable, so census 12.6's multi-voucher half is not closed however well the projector works.");

        vm.DrillIn();
        Assert.NotNull(vm.MultiVoucherPrint);
    }
}
