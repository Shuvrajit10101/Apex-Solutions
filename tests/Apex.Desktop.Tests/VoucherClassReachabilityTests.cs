using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS ROW 2.6 — CAN AN OPERATOR ACTUALLY REACH THE GENERAL VOUCHER CLASS?</b> (schema v62)
///
/// <para><b>Why this file exists.</b> Three features have shipped on this project complete, correct, tested and
/// COMPLETELY DEAD, the largest ~625 lines whose only writers were test files — and every one had green engine
/// tests. Row 2.6 is a bigger version of that risk, because it writes LEDGER ENTRIES: an unreachable pre-map is
/// merely dead, but a reachable one the operator cannot inspect posts money they never approved. So these tests
/// walk the REALISED VISUAL TREE from a real <see cref="MainWindow"/> and prove the whole route exists —
/// Masters → Voucher Type → Alter → the class list → its Default Accounting Allocations and Additional
/// Accounting Entries — and that keying it through the SCREEN reaches the aggregate.</para>
///
/// <para><b>Why the visual tree and not a view-model flag.</b> Asserting <c>ShowClassAccountingTables</c> is
/// exactly the test that passes on a build where the XAML never binds it. What makes a field reachable is that a
/// control for it is REALISED and <c>IsEffectivelyVisible</c>.</para>
///
/// <para><b>R7 — ATTESTED.</b> Every caption asserted below is vendor-verbatim:
/// <c>help.tallysolutions.com/tally-prime/accounting/voucher-types-tally/</c> for <i>"Name of Class"</i> and
/// <i>"Default Accounting Allocations for all items in Invoice"</i>;
/// <c>help.tallysolutions.com/tally-prime/importer-excise-masters/ei-configure-vch-class-customs-duty-tally/</c>
/// for <i>"Additional Accounting Entries"</i> and its columns; and
/// <c>help.tallysolutions.com/round-off-invoice-and-ledger-values/</c> for the rounding vocabulary.</para>
///
/// <para>Headless-safe: visual-tree and layout inspection only. No Skia, no rendered frame.</para>
/// </summary>
public sealed class VoucherClassReachabilityTests
{
    /// <summary>
    /// The full route: alter a Sales type, add a class, open its accounting tables, and confirm BOTH vendor
    /// section captions are realised and visible. On today's main this is red at the first caption — the general
    /// machinery did not exist and the class section was gated to Stock Journal only.
    /// </summary>
    [AvaloniaFact]
    public void The_two_census_2_6_tables_are_reachable_on_a_sales_class()
    {
        var (window, vm, dir) = Open();
        try
        {
            var sales = vm.Company!.FindVoucherTypeByName("Sales")!;
            AlterVoucherType(vm, sales.Id);
            Pump(window);

            var master = vm.VoucherTypeMaster!;
            Assert.True(master.ShowVoucherClasses, "A Sales type offers no voucher class at all.");
            Assert.True(master.ShowClassAccountingTables);

            // 🔴 The Inter-Godown box is census 9.9's and belongs to the Stock Journal. Offering it here would
            // put a checkbox in front of the operator whose only possible outcome is an engine refusal.
            Assert.False(master.ShowInterGodownOption);

            master.NewClassName = "Retail";
            Assert.True(master.AddClass(), master.Message);
            Pump(window);

            // Nothing is shown until a class is picked — the two tables belong TO a class.
            Assert.False(master.HasSelectedClass);
            Assert.False(HasVisibleLabel(window, "Additional Accounting Entries"));

            var row = Assert.Single(master.Classes);
            master.SelectClass(row.Id);
            Pump(window);

            Assert.True(HasVisibleLabel(window, "Default Accounting Allocations for all items in Invoice"),
                "The vendor's Default Accounting Allocations table is not drawn, so census row 2.6's ledger "
                + "pre-map cannot be configured by any operator.");
            Assert.True(HasVisibleLabel(window, "Additional Accounting Entries"),
                "The vendor's Additional Accounting Entries table is not drawn, so freight, per-unit duties and "
                + "the invoice round-off cannot be configured by any operator.");
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>KEYING THE TABLES THROUGH THE SCREEN REACHES THE AGGREGATE AND THE POSTED FIGURES ARE THE ONES THE
    /// OPERATOR CONFIGURED.</b> This is the assertion that the row is genuinely wired rather than merely drawn: a
    /// 60/40 pre-map plus a normal round-off to the rupee is keyed through the view model, and the class is then
    /// asked to post — the legs must be the operator's own.
    /// </summary>
    [AvaloniaFact]
    public void Keying_the_tables_through_the_screen_configures_a_class_that_posts_the_operator_s_figures()
    {
        var (window, vm, dir) = Open();
        try
        {
            var salesType = vm.Company!.FindVoucherTypeByName("Sales")!;
            AlterVoucherType(vm, salesType.Id);
            Pump(window);

            var master = vm.VoucherTypeMaster!;
            master.NewClassName = "Retail";
            Assert.True(master.AddClass(), master.Message);
            var classId = Assert.Single(master.Classes).Id;
            master.SelectClass(classId);
            Pump(window);

            // Two ledgers out of the company's own list — the picker the screen offers, not a fabricated Guid.
            Assert.NotEmpty(master.LedgerOptions);
            var first = master.LedgerOptions[0];
            var second = master.LedgerOptions[1];

            master.NewAllocationLedger = first;
            master.NewAllocationPercentText = "60";
            Assert.True(master.AddAllocation(), master.Message);

            // While the table is half-keyed the running total says so, in the operator's own terms.
            Assert.Contains("must reach 100%", master.AllocationTotalCaption);

            master.NewAllocationLedger = second;
            master.NewAllocationPercentText = "40";
            Assert.True(master.AddAllocation(), master.Message);
            Assert.Equal("Allocated: 100%", master.AllocationTotalCaption);

            master.NewEntryLedger = second;
            master.NewEntryCalculationType =
                master.CalculationTypes.Single(c => c.Value == VoucherClassCalculationType.AsTotalAmountRounding);
            master.NewEntryRoundingMethod =
                master.RoundingMethods.Single(r => r.Value == VoucherClassRoundingMethod.Normal);
            master.NewEntryRoundingLimitText = "1";
            Assert.True(master.AddAdditionalEntry(), master.Message);

            Assert.Equal(2, master.Allocations.Count);
            Assert.Single(master.AdditionalEntries);

            // …and the class in the AGGREGATE posts exactly those figures on a real amount.
            var cls = vm.Company.FindVoucherType(salesType.Id)!.Classes.Single(c => c.Id == classId);
            var result = Apex.Ledger.Services.VoucherClassPosting.Compute(
                cls, Money.FromRupees(1_000.40m), totalBaseQuantity: 1m);

            Assert.Equal(600.24m, result.AllocationLegs[0].Amount.Amount);   // 60% of 1 000.40
            Assert.Equal(400.16m, result.AllocationLegs[1].Amount.Amount);   // the remainder
            Assert.Equal(-0.40m, Assert.Single(result.AdditionalLegs).Amount.Amount);
            Assert.Equal(1_000.00m, result.Total.Amount);                    // and it IS round
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>An allocation percentage is parsed INVARIANT-culture. The gate runs on ubuntu and macOS, where a
    /// culture-sensitive parse of "33.33" under a comma-decimal locale yields 3 333 — a 333 300% allocation that
    /// the basis-point ceiling would reject with a baffling message, or worse, silently accept elsewhere.</summary>
    [AvaloniaFact]
    public void An_allocation_percentage_is_parsed_invariantly_and_a_bad_one_is_refused()
    {
        var (window, vm, dir) = Open();
        try
        {
            var salesType = vm.Company!.FindVoucherTypeByName("Sales")!;
            AlterVoucherType(vm, salesType.Id);
            var master = vm.VoucherTypeMaster!;
            master.NewClassName = "Retail";
            Assert.True(master.AddClass(), master.Message);
            master.SelectClass(master.Classes[0].Id);

            master.NewAllocationLedger = master.LedgerOptions[0];

            foreach (var bad in new[] { "", "abc", "0", "-5", "100.5", "33.333" })
            {
                master.NewAllocationPercentText = bad;
                Assert.False(master.AddAllocation(), $"'{bad}' was accepted as an allocation percentage.");
                Assert.False(string.IsNullOrWhiteSpace(master.Message));
            }

            master.NewAllocationPercentText = "33.33";
            Assert.True(master.AddAllocation(), master.Message);
            Assert.Equal("33.33%", master.Allocations[0].Percent);
        }
        finally { Cleanup(window, dir); }
    }

    // ───────────────────────────────────────────────────────────────────────── helpers

    private static bool HasVisibleLabel(MainWindow w, string caption) =>
        Descendants(w).Any(v =>
            v is TextBlock { IsEffectivelyVisible: true } t
            && t.Bounds.Width > 0 && t.Bounds.Height > 0
            && string.Equals(t.Text, caption, StringComparison.Ordinal));

    private static IEnumerable<Avalonia.Visual> Descendants(Avalonia.Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    /// <summary>Opens the Voucher Type master in ALTER mode over one type, driving the highlight with the same
    /// public arrow verb the operator's Down key uses — there is no test-only setter, and adding one would let
    /// these tests pass over a highlight the UI cannot reach.</summary>
    private static void AlterVoucherType(MainWindowViewModel vm, Guid typeId)
    {
        vm.ShowVoucherTypeMaster();
        var master = vm.VoucherTypeMaster!;
        var guard = 0;
        while (master.HighlightedRow?.MasterId != typeId)
        {
            master.MoveHighlight(1);
            Assert.True(++guard < 300,
                "The voucher type never came under the highlight, so Alter mode cannot be reached.");
        }
        Assert.True(vm.AlterHighlightedVoucherTypeRow(),
            "The Voucher Type master would not open in Alter mode, so the class tables can never be reached.");
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) Open()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexVClassReach_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
        window.Show();

        vm.NewCompanyName = "Voucher Class Reachability Co";
        vm.CreateCompany();
        Pump(window);
        return (window, vm, tempDir);
    }

    private static void Cleanup(MainWindow window, string dir)
    {
        window.Close();
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { /* temp dir */ }
    }
}
