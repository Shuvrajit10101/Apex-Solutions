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
using Apex.Ledger;
using Apex.Ledger.Services;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Census 10.1 — the LAST mile for Credit Limits: fields an operator can actually type into.</b>
///
/// <para><b>🔴 WHY THIS FILE EXISTS.</b> <c>CreditLimitRulesTests</c> and <c>CreditLimitBlockTests</c> pin the
/// engine, and both would stay green if the Ledger master carried no Credit Limit box at all — leaving a correct,
/// tested, save-blocking rule that <b>no operator could ever configure</b>, i.e. a rule that refuses invoices on a
/// limit nobody can set or clear. That is worse than the dead-capability defect this project has filed twice
/// (<c>CostReports.BuildLedgerBreakup</c>, <c>MultiAccountPrintViewModel</c>), because this one can block work. So
/// every test below realises the shipped <see cref="MainWindow"/> and reads the visual tree.</para>
///
/// <para><b>Fidelity (RULING 14 — help.tallysolutions.com).</b> The three captions and the Sundry
/// Debtors/Creditors scope are the vendor's; the wording of the notice line is OURS (ruling 9).</para>
/// </summary>
public sealed class LedgerMasterCreditLimitReachabilityTests
{
    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(1280, 800));
        window.Arrange(new Rect(0, 0, 1280, 800));
        Dispatcher.UIThread.RunJobs();
    }

    private static MainWindow OpenLedgerMaster(out MainWindowViewModel vm, string dir)
    {
        vm = new MainWindowViewModel(new CompanyStorage(dir));
        vm.LoadRobertDemo();
        vm.ShowLedgerMaster();
        Assert.True(vm.LedgerMaster is not null,
            "ShowLedgerMaster() produced no page — the route itself is broken, so nothing below is meaningful.");

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Pump(window);
        return window;
    }

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

    private static string TempDir(string tag) => Path.Combine(Path.GetTempPath(), tag + Guid.NewGuid().ToString("N"));

    private static TextBox? CreditLimitBox(Window window)
        => window.GetVisualDescendants().OfType<TextBox>()
                 .FirstOrDefault(t => t.Name == "LedgerCreditLimitBox");

    private static CheckBox[] CreditCheckBoxes(Window window)
        => window.GetVisualDescendants().OfType<CheckBox>()
                 .Where(c => c.Content is string s
                          && (s.Contains("credit days", StringComparison.Ordinal)
                           || s.Contains("credit limit", StringComparison.Ordinal)))
                 .ToArray();

    /// <summary>Selects the first group under Sundry Debtors on the open Ledger master.</summary>
    private static void SelectPartyGroup(MainWindowViewModel vm)
    {
        var master = vm.LedgerMaster!;
        master.SelectedGroup = master.Groups.First(
            g => string.Equals(g.Name, "Sundry Debtors", StringComparison.OrdinalIgnoreCase));
        Dispatcher.UIThread.RunJobs();
    }

    // ------------------------------------------------------------------ the block is realised, and scoped

    /// <summary>
    /// 🔴 <b>THE OPERATOR-FACING ASSERTION.</b> On a party group, all three ATTESTED controls are realised and
    /// visible. Fails on today's main: none of them exists.
    /// </summary>
    [AvaloniaFact]
    public void The_credit_limit_fields_are_realised_on_a_party_ledger()
    {
        var dir = TempDir("ApexCreditLimitRealised_");
        MainWindow? window = null;
        try
        {
            window = OpenLedgerMaster(out var vm, dir);
            SelectPartyGroup(vm);
            Pump(window);

            var box = CreditLimitBox(window);
            Assert.True(box is not null,
                "no realised 'Credit limit' box on a Sundry Debtors ledger. The block rule may exist in "
              + "Apex.Ledger, but with no control bound to it an operator cannot set OR CLEAR a limit, and census "
              + "row 10.1 has not moved.");
            Assert.True(box!.IsEffectivelyVisible, "a box that is not visible cannot be typed into.");

            var checks = CreditCheckBoxes(window);
            Assert.True(checks.Length == 2,
                $"expected the two ATTESTED credit switches; found {checks.Length} "
              + $"({string.Join(" | ", checks.Select(c => c.Content as string ?? "?"))}).");
            Assert.All(checks, c => Assert.True(c.IsEffectivelyVisible));
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// ATTESTED scope: limits belong to ledgers under Sundry Debtors / Sundry Creditors. On a NON-party group the
    /// whole block is off screen, so a limit can never be captured on a ledger the engine would then ignore.
    /// </summary>
    [AvaloniaFact]
    public void The_block_is_absent_on_a_non_party_group()
    {
        var dir = TempDir("ApexCreditLimitScope_");
        MainWindow? window = null;
        try
        {
            window = OpenLedgerMaster(out var vm, dir);
            var master = vm.LedgerMaster!;

            master.SelectedGroup = master.Groups.First(
                g => string.Equals(g.Name, "Indirect Expenses", StringComparison.OrdinalIgnoreCase));
            Dispatcher.UIThread.RunJobs();
            Pump(window);

            Assert.False(master.IsPartyGroup, "premise: Indirect Expenses is not a party group.");
            var box = CreditLimitBox(window);
            Assert.True(box is null || !box.IsEffectivelyVisible,
                "the Credit Limits block must be off screen for a non-party ledger — the vendor scopes the feature "
              + "to Sundry Debtors / Sundry Creditors, and CreditLimitRules ignores a limit stored anywhere else.");
        }
        finally { Cleanup(window, dir); }
    }

    // ------------------------------------------------------------------ 🔴 blank ≠ zero, driven through the box

    /// <summary>
    /// 🔴 <b>THE DEFECT THIS ROW IS MOST LIKELY TO SHIP.</b> Typed through the REALISED box, not by setting the
    /// view-model string: a blank stores <c>null</c> ("no limit") and a typed <c>0</c> stores <c>Money.Zero</c> (a
    /// real limit that refuses every credit sale). If these two ever collapse into one another, either every party
    /// is frozen or no limit is ever enforced.
    /// </summary>
    [AvaloniaFact]
    public void A_blank_box_stores_no_limit_and_a_typed_zero_stores_a_limit_of_zero()
    {
        var dir = TempDir("ApexCreditLimitBlankZero_");
        MainWindow? window = null;
        try
        {
            window = OpenLedgerMaster(out var vm, dir);
            SelectPartyGroup(vm);
            Pump(window);
            var master = vm.LedgerMaster!;
            var box = CreditLimitBox(window)!;

            // (1) blank box ⇒ NO limit.
            master.Name = "Blank Limit Party";
            box.Text = string.Empty;
            Dispatcher.UIThread.RunJobs();
            Assert.True(master.Create(), master.Message);

            var blank = vm.Company!.FindLedgerByName("Blank Limit Party")!;
            Assert.Null(blank.CreditLimit);

            // (2) a typed "0" ⇒ a limit OF ZERO, which is a different answer and blocks.
            SelectPartyGroup(vm);
            Pump(window);
            master.Name = "Zero Limit Party";
            box = CreditLimitBox(window)!;
            box.Text = "0";
            Dispatcher.UIThread.RunJobs();
            Assert.True(master.Create(), master.Message);

            var zero = vm.Company!.FindLedgerByName("Zero Limit Party")!;
            Assert.NotNull(zero.CreditLimit);
            Assert.Equal(Money.Zero, zero.CreditLimit!.Value);

            // …and the engine agrees the two differ.
            Assert.Null(CreditLimitRules.EffectiveLimit(vm.Company!, blank));
            Assert.Equal(Money.Zero, CreditLimitRules.EffectiveLimit(vm.Company!, zero)!.Value);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>An ordinary limit typed into the realised box reaches the domain, and the two switches with it.</summary>
    [AvaloniaFact]
    public void A_typed_limit_and_both_switches_reach_the_ledger()
    {
        var dir = TempDir("ApexCreditLimitTyped_");
        MainWindow? window = null;
        try
        {
            window = OpenLedgerMaster(out var vm, dir);
            SelectPartyGroup(vm);
            Pump(window);
            var master = vm.LedgerMaster!;

            master.Name = "Limited Party";
            CreditLimitBox(window)!.Text = "50000";
            foreach (var c in CreditCheckBoxes(window)) c.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(master.Create(), master.Message);

            var l = vm.Company!.FindLedgerByName("Limited Party")!;
            Assert.Equal(Money.FromRupees(50000m), l.CreditLimit!.Value);
            Assert.True(l.CheckCreditDaysOnEntry);
            Assert.True(l.OverrideCreditLimitWithPostDated);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>A non-numeric or negative limit is refused with a readable message rather than silently becoming
    /// "no limit" — a typo must never quietly switch the party's limit off.</summary>
    [AvaloniaFact]
    public void A_bad_limit_is_refused_and_nothing_is_created()
    {
        var dir = TempDir("ApexCreditLimitBad_");
        MainWindow? window = null;
        try
        {
            window = OpenLedgerMaster(out var vm, dir);
            SelectPartyGroup(vm);
            Pump(window);
            var master = vm.LedgerMaster!;
            var before = vm.Company!.Ledgers.Count;

            master.Name = "Typo Party";
            CreditLimitBox(window)!.Text = "50,00O";     // a letter O, the classic typo
            Dispatcher.UIThread.RunJobs();
            Assert.False(master.Create());
            Assert.Contains("Credit limit", master.Message ?? "", StringComparison.Ordinal);
            Assert.Equal(before, vm.Company!.Ledgers.Count);

            CreditLimitBox(window)!.Text = "-1";
            Dispatcher.UIThread.RunJobs();
            Assert.False(master.Create());
            Assert.Contains("negative", master.Message ?? "", StringComparison.Ordinal);
            Assert.Equal(before, vm.Company!.Ledgers.Count);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// R-9: the credit PERIOD is discarded when bill-by-bill is off (<c>ApplyTo</c> writes <c>null</c>). The screen
    /// must SAY so, or an operator sets a period, switches bill-wise off, saves, and silently loses it.
    /// </summary>
    [AvaloniaFact]
    public void The_notice_warns_that_the_credit_period_needs_bill_wise()
    {
        var dir = TempDir("ApexCreditLimitNotice_");
        MainWindow? window = null;
        try
        {
            window = OpenLedgerMaster(out var vm, dir);
            SelectPartyGroup(vm);
            Pump(window);
            var master = vm.LedgerMaster!;

            master.MaintainBillByBill = false;
            Dispatcher.UIThread.RunJobs();
            Pump(window);

            Assert.Contains("period is not kept", master.CreditDaysNotice, StringComparison.Ordinal);

            var realised = window.GetVisualDescendants().OfType<TextBlock>()
                .Any(t => t.Text is { } s && s.Contains("period is not kept", StringComparison.Ordinal)
                       && t.IsEffectivelyVisible);
            Assert.True(realised,
                "the notice exists on the view model but is not rendered — a warning nobody can read is not a "
              + "warning, and R-9 (the silently-lost credit period) is still live.");
        }
        finally { Cleanup(window, dir); }
    }
}
