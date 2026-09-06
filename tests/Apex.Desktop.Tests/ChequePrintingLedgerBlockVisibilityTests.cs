using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>THE CHEQUE-PRINTING BLOCK HAS TO BE ON THE SCREEN, NOT JUST IN THE VIEW MODEL.</b>
///
/// <para><b>Why this test does not assert <c>ShowChequePrinting</c>.</b> Asserting the flag is exactly the test
/// that passes on the broken build: this project has already shipped a keyboard cursor that four row templates
/// never drew while <c>IsHighlighted</c> flipped perfectly, and has filed two whole capabilities that existed
/// only as unreferenced code. A property nobody bound is not a field the operator can fill in. So this walks the
/// <b>realised visual tree</b> of the real <see cref="MainWindow"/> and requires the checkbox and its bank-name
/// box to be actually there, actually visible, and actually laid out with non-zero bounds.</para>
///
/// <para><b>What it guards.</b> <c>Ledger.EnableChequePrinting</c> and <c>Ledger.ChequePrintingBankName</c> are
/// schema-v5 columns that had no screen anywhere in the product for nine phases. Without them set, the Cheque
/// Printing report (census 8.4) is structurally empty for every real company — a menu row that leads to a
/// permanently blank page. Vendor grounding: <c>help.tallysolutions.com/cheque-payments-set-up/</c>, section
/// "Specify Cheque Range and Format in Bank Ledger" — the settings belong on the BANK LEDGER master.</para>
///
/// <para>Headless-safe: visual-tree, visibility and layout-bounds inspection only. No Skia, no rendered frame.</para>
/// </summary>
public sealed class ChequePrintingLedgerBlockVisibilityTests
{
    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    /// <summary>A realised, visible, laid-out control matching the predicate — the three conditions together are
    /// what "the operator can see and use it" means. Any one alone is satisfiable by a control that is present
    /// but collapsed, or visible but of zero size.</summary>
    private static bool HasLiveControl<T>(MainWindow window, Func<T, bool> match) where T : Control =>
        Descendants(window).OfType<T>().Any(c =>
            c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0 && match(c));

    private static bool HasEnableChequePrintingCheckBox(MainWindow window) =>
        HasLiveControl<CheckBox>(window, cb =>
            cb.Content as string == "Enable cheque printing");

    // The TextBox half used to read `tb.GetValue(TextBox.TextProperty) is not null || tb.PlaceholderText == …`,
    // whose first disjunct matches essentially EVERY text box on the screen — so only the label half was doing
    // any work, and a block that drew its caption with no input box under it would have passed. Both halves now
    // name their own control.
    private static bool HasBankNameBox(MainWindow window) =>
        HasLiveControl<TextBox>(window, tb => tb.PlaceholderText == "defaults to the ledger name")
        && HasLiveControl<TextBlock>(window, tb => tb.Text == "Name of bank (on cheque)");

    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) OpenLedgerMaster()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexChequeBlock_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
        window.Show();

        vm.NewCompanyName = "Cheque Block Co";
        vm.CreateCompany();
        vm.ShowLedgerMaster();
        Pump(window);
        return (window, vm, dir);
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

    /// <summary>
    /// 🔴 THE TEST. Pick "Bank Accounts" on the real ledger-master screen and the cheque-printing block must
    /// appear on it; tick the box and the bank-name field must appear under it. Pick a non-bank group and the
    /// whole block must go away again — a block that renders for every ledger is not the vendor's screen.
    /// </summary>
    [AvaloniaFact]
    public void The_cheque_printing_block_is_drawn_for_a_bank_group_and_only_for_a_bank_group()
    {
        var (window, vm, dir) = OpenLedgerMaster();
        try
        {
            var master = vm.LedgerMaster!;

            // (1) A non-bank group: the block is not on the screen at all.
            master.SelectedGroup = vm.Company!.FindGroupByName("Indirect Expenses");
            Pump(window);
            Assert.False(
                HasEnableChequePrintingCheckBox(window),
                "the cheque-printing checkbox was drawn for a NON-bank ledger, which the vendor's screen does not do");

            // (2) A bank group: the checkbox is realised, visible and laid out.
            master.SelectedGroup = vm.Company!.FindGroupByName("Bank Accounts");
            Pump(window);
            Assert.True(
                HasEnableChequePrintingCheckBox(window),
                "the ledger master offers no way to switch cheque printing on for a BANK ledger, so the Cheque "
                + "Printing report is structurally empty for every real company (census 8.4)");

            // (3) The bank-name field follows the toggle, exactly as the vendor's screen asks for it only then.
            Assert.False(HasBankNameBox(window));
            master.EnableChequePrinting = true;
            Pump(window);
            Assert.True(
                HasBankNameBox(window),
                "'Name of bank (on cheque)' is not drawn once cheque printing is enabled");

            // (4) And back: moving the ledger out of the bank groups withdraws the whole block.
            master.SelectedGroup = vm.Company!.FindGroupByName("Indirect Expenses");
            Pump(window);
            Assert.False(HasEnableChequePrintingCheckBox(window));
            Assert.False(HasBankNameBox(window));
        }
        finally
        {
            Cleanup(window, dir);
        }
    }

    /// <summary>
    /// The realised checkbox is TWO-WAY bound: ticking the control on screen must reach the view model, or the
    /// operator sets a flag that is never saved. A one-way binding draws a checkbox that does nothing.
    /// </summary>
    [AvaloniaFact]
    public void Ticking_the_realised_checkbox_reaches_the_view_model()
    {
        var (window, vm, dir) = OpenLedgerMaster();
        try
        {
            var master = vm.LedgerMaster!;
            master.SelectedGroup = vm.Company!.FindGroupByName("Bank Accounts");
            Pump(window);

            var box = Descendants(window).OfType<CheckBox>()
                .Single(cb => cb.Content as string == "Enable cheque printing");

            Assert.False(master.EnableChequePrinting);
            box.IsChecked = true;
            Pump(window);
            Assert.True(master.EnableChequePrinting);

            box.IsChecked = false;
            Pump(window);
            Assert.False(master.EnableChequePrinting);
        }
        finally
        {
            Cleanup(window, dir);
        }
    }
}
