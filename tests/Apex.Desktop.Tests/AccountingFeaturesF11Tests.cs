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
/// 🔴 <b>CENSUS ROW 1.7 — F11 COMPANY FEATURES → THE ACCOUNTING GROUP.</b>
///
/// <para><b>What the row was.</b> <c>ABSENT</c>: <i>"No per-company switch for bill-wise, interest, cost
/// centres…"</i>. Three capabilities this product has shipped for a long time — bill-wise balances, cost
/// centres and interest calculation — were <b>always on with no way to turn them off</b>, which is the
/// mirror image of the dead-knob defect: real behaviour behind no door.</para>
///
/// <para><b>The source.</b> <c>help.tallysolutions.com/company-features-f11-tally/</c> prints the Accounting
/// group as four options — <i>Maintain Accounts</i>, <i>Enable Bill-wise entry</i>, <i>Enable Cost Centres</i>,
/// <i>Enable Interest Calculation</i>. Three ship here. <b>Maintain Accounts deliberately does not</b>: this
/// product has no accounts-less mode, so that switch could only ever read Yes and gate nothing, and a toggle
/// that changes no behaviour is worse than an absent one because it looks shipped.</para>
///
/// <para>🔴 <b>WHY THESE TESTS LOOK AT THE REALISED VISUAL TREE AND AT MENU STRUCTURE, NOT AT THE FLAGS.</b>
/// Asserting <c>Company.EnableCostCentres == false</c> is exactly the test that passes on a build where the
/// flag is bound to nothing — the dead knob this project has filed several times. So each case flips the
/// company feature and then asks the questions an operator would: <i>is the caption on the F11 page a control
/// I can see?</i> and <i>did the thing it claims to gate actually leave the screen?</i></para>
///
/// <para>Headless-safe: visual-tree and layout-bounds inspection only. No Skia, no rendered frame.</para>
/// </summary>
public sealed class AccountingFeaturesF11Tests
{
    /// <summary>The vendor's Accounting-group captions, verbatim, in the vendor's order.</summary>
    public static IEnumerable<object[]> VendorAccountingCaptions() => new[]
    {
        new object[] { "Enable Bill-wise entry" },
        new object[] { "Enable Cost Centres" },
        new object[] { "Enable Interest Calculation" },
    };

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

    /// <summary>
    /// Every REALISED, EFFECTIVELY-VISIBLE, non-degenerate <see cref="CheckBox"/> whose content reads exactly
    /// <paramref name="caption"/>. "Effectively visible" and non-zero bounds together are the difference
    /// between a control the operator can reach and one that merely exists in the XAML.
    /// </summary>
    private static List<CheckBox> VisibleCheckBoxes(MainWindow window, string caption) =>
        Descendants(window)
            .OfType<CheckBox>()
            .Where(cb => cb.IsEffectivelyVisible
                         && cb.Bounds.Width > 0 && cb.Bounds.Height > 0
                         && string.Equals(cb.Content as string, caption, StringComparison.Ordinal))
            .ToList();

    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) Open()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexAcctF11_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
        window.Show();

        vm.NewCompanyName = "Accounting Features Co";
        vm.CreateCompany();
        Pump(window);
        return (window, vm, tempDir);
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
    /// The labels of the selectable rows of the deepest (active) cascade column — what the operator can arrow
    /// onto right now.
    /// </summary>
    private static List<string> SelectableLabels(MainWindowViewModel vm) =>
        vm.Columns[^1].Items.Where(i => i.IsSelectable).Select(i => i.Label).ToList();

    /// <summary>Every label of the deepest column, headers included — so a stranded header is visible to the test.</summary>
    private static List<string> AllLabels(MainWindowViewModel vm) =>
        vm.Columns[^1].Items.Select(i => i.Label).ToList();

    // ==========================================================================================
    // 1. THE DOOR. The row is only shipped if a user can REACH the three options from the keyboard.
    // ==========================================================================================

    /// <summary>
    /// 🔴 <b>REACHABILITY, FROM THE KEYBOARD, WITH NO VIEW-MODEL CALL.</b> F11 from the Gateway must land on
    /// the Company Features page and each vendor caption must be a control that is actually on screen. This is
    /// the clause that fails on today's main — the three captions do not exist there at all.
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(VendorAccountingCaptions))]
    public void F11_opens_company_features_and_the_accounting_caption_is_on_screen(string caption)
    {
        var (window, vm, dir) = Open();
        try
        {
            window.KeyPressQwerty(PhysicalKey.F11, RawInputModifiers.None);
            Pump(window);

            Assert.True(vm.GstConfig != null,
                "F11 did not open the Company Features page, so the Accounting group is unreachable.");

            var found = VisibleCheckBoxes(window, caption);
            Assert.True(found.Count == 1,
                $"The vendor's F11 → Accounting option \"{caption}\" is not a single visible control on the " +
                $"Company Features page (found {found.Count}). A capability no user can reach is not complete.");
        }
        finally { Cleanup(window, dir); }
    }

    // ==========================================================================================
    // 2. THE KNOBS ARE NOT DEAD. Each flips something the operator can see leave the screen.
    // ==========================================================================================

    /// <summary>
    /// 🔴 <b>"Enable Interest Calculation" gates the ledger master's interest block.</b> On today's main the
    /// block is unconditional, so the OFF half of this test fails there. Both halves are asserted against the
    /// same fixture: a gate that hides everything always would pass the OFF clause alone.
    /// </summary>
    [AvaloniaFact]
    public void Enable_interest_calculation_gates_the_ledger_masters_interest_block()
    {
        var (window, vm, dir) = Open();
        try
        {
            vm.Company!.EnableInterestCalculation = true;
            vm.ShowLedgerMaster();
            Pump(window);
            Assert.True(VisibleCheckBoxes(window, "Activate Interest Calculation").Count == 1,
                "With the F11 Interest feature ON the ledger master must offer 'Activate Interest Calculation'.");

            vm.Back();
            vm.Company.EnableInterestCalculation = false;
            vm.ShowLedgerMaster();
            Pump(window);
            Assert.True(VisibleCheckBoxes(window, "Activate Interest Calculation").Count == 0,
                "With the F11 Interest feature OFF the ledger master still shows 'Activate Interest " +
                "Calculation' — the toggle is a dead knob: it changes no behaviour the operator can see.");
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>"Enable Bill-wise entry" gates the ledger master's bill-wise switch.</b> The block is also
    /// party-group-gated, so the ON half picks a party group first — otherwise the OFF clause would be
    /// satisfied by the pre-existing party test rather than by the new company feature, and the test would
    /// prove nothing about the flag.
    /// </summary>
    [AvaloniaFact]
    public void Enable_bill_wise_entry_gates_the_ledger_masters_bill_wise_switch()
    {
        var (window, vm, dir) = Open();
        try
        {
            const string Caption = "Maintain balances bill-by-bill";

            vm.Company!.EnableBillWiseEntry = true;
            vm.ShowLedgerMaster();
            var master = vm.LedgerMaster!;
            master.SelectedGroup = master.Groups.First(g => g.Name == "Sundry Debtors");
            Pump(window);
            Assert.True(master.IsPartyGroup,
                "Fixture guard: 'Sundry Debtors' must resolve as a party group or this test asserts nothing.");
            Assert.True(VisibleCheckBoxes(window, Caption).Count == 1,
                "With the F11 Bill-wise feature ON a party ledger must offer 'Maintain balances bill-by-bill'.");

            vm.Back();
            vm.Company.EnableBillWiseEntry = false;
            vm.ShowLedgerMaster();
            var off = vm.LedgerMaster!;
            off.SelectedGroup = off.Groups.First(g => g.Name == "Sundry Debtors");
            Pump(window);
            Assert.True(off.IsPartyGroup, "Fixture guard: the OFF half must also be standing on a party group.");
            Assert.True(VisibleCheckBoxes(window, Caption).Count == 0,
                "With the F11 Bill-wise feature OFF the ledger master still shows 'Maintain balances " +
                "bill-by-bill' on a party ledger — the company feature gates nothing.");
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>"Enable Cost Centres" gates the Cost Masters section of Masters → Create — the HEADER too.</b>
    /// A section heading left standing over no rows is the empty scaffolding the cascade contract forbids, and
    /// it is the exact half a naive gate forgets, so the header is asserted separately from its two rows.
    /// </summary>
    [AvaloniaFact]
    public void Enable_cost_centres_gates_the_cost_masters_section_of_the_create_column()
    {
        var (window, vm, dir) = Open();
        try
        {
            vm.Company!.EnableCostCentres = true;
            vm.ShowCreateMenu();
            Pump(window);
            var on = SelectableLabels(vm);
            Assert.Contains("Cost Category", on);
            Assert.Contains("Cost Centre", on);
            Assert.Contains("Cost Masters", AllLabels(vm));

            vm.Company.EnableCostCentres = false;
            vm.ShowCreateMenu();
            Pump(window);
            var off = SelectableLabels(vm);
            Assert.False(off.Contains("Cost Category") || off.Contains("Cost Centre"),
                "With the F11 Cost Centres feature OFF the Cost Category / Cost Centre masters are still on " +
                "Masters → Create — the company feature gates nothing.");
            Assert.DoesNotContain("Cost Masters", AllLabels(vm));

            // The gate must be surgical: it removes its own section and nothing else.
            Assert.Contains("Ledger", off);
            Assert.Contains("Stock Item", off);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>The two report entries the vendor gates on these features leave Statements of Accounts with
    /// them</b> — and the neighbours that are gated by NEITHER feature stay, which is what stops a gate that
    /// simply empties the hub from passing.
    /// </summary>
    [AvaloniaFact]
    public void The_two_accounting_features_gate_their_statements_of_accounts_entries()
    {
        var (window, vm, dir) = Open();
        try
        {
            vm.Company!.EnableCostCentres = true;
            vm.Company.EnableInterestCalculation = true;
            vm.ShowStatementsOfAccountsMenu();
            Pump(window);
            var on = SelectableLabels(vm);
            Assert.Contains("Cost Centres", on);
            Assert.Contains("Interest Calculation", on);

            vm.Company.EnableCostCentres = false;
            vm.Company.EnableInterestCalculation = false;
            vm.ShowStatementsOfAccountsMenu();
            Pump(window);
            var off = SelectableLabels(vm);
            Assert.DoesNotContain("Cost Centres", off);
            Assert.DoesNotContain("Interest Calculation", off);
            Assert.Contains("Outstandings", off);
            Assert.Contains("Budgets", off);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>"Enable Bill-wise entry" also gates the VOUCHER LINE's bill-allocation panel</b> — the same
    /// deepest-reach clause as the cost test below. Without it the company feature would only tidy the ledger
    /// master while every party line kept seeding New-Ref rows.
    /// </summary>
    [AvaloniaFact]
    public void Enable_bill_wise_entry_gates_the_voucher_lines_bill_allocation_panel()
    {
        var (window, vm, dir) = Open();
        try
        {
            vm.ShowLedgerMaster();
            var led = vm.LedgerMaster!;
            led.Name = "Acme Traders";
            led.SelectedGroup = led.Groups.First(g => g.Name == "Sundry Debtors");
            Assert.True(led.MaintainBillByBill,
                "Fixture guard: a party ledger defaults to bill-by-bill, or this test asserts nothing.");
            Assert.True(led.Create(), led.Message);
            var acme = vm.Company!.FindLedgerByName("Acme Traders")!;

            Assert.True(vm.Company.EnableBillWiseEntry, "Fixture guard: the feature must start ON.");
            vm.OpenVoucher(Apex.Ledger.Domain.VoucherBaseType.Receipt);
            var on = vm.VoucherEntry!;
            on.ChangeMode();
            on.Lines[0].SelectedLedger = acme;
            on.Lines[0].Side = Apex.Ledger.DrCr.Credit;
            on.Lines[0].AmountText = "5000";
            Assert.True(on.Lines[0].IsBillWise,
                "Fixture guard: with the feature ON a bill-by-bill party line must open the bill panel.");

            vm.Company.EnableBillWiseEntry = false;
            vm.OpenVoucher(Apex.Ledger.Domain.VoucherBaseType.Receipt);
            var off = vm.VoucherEntry!;
            off.ChangeMode();
            off.Lines[0].SelectedLedger = acme;
            off.Lines[0].Side = Apex.Ledger.DrCr.Credit;
            off.Lines[0].AmountText = "5000";
            Assert.False(off.Lines[0].IsBillWise,
                "With the F11 Bill-wise feature OFF the voucher line still opens its bill-allocation panel — " +
                "the company feature only tidies the master screen and leaves entry ungated.");
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>"Enable Cost Centres" also gates the VOUCHER LINE's cost-allocation panel</b> — the deepest place
    /// the feature reaches, and the one a menu-only gate would leave behind (the masters gone from the menu but
    /// the panel still opening on every expense line). The ON half runs first on the same company, so this
    /// cannot pass by the panel simply never opening.
    /// </summary>
    [AvaloniaFact]
    public void Enable_cost_centres_gates_the_voucher_lines_cost_allocation_panel()
    {
        var (window, vm, dir) = Open();
        try
        {
            // A category + a centre, and an expense ledger (cost centres applicable by nature).
            vm.ShowCostCategoryMaster();
            var cat = vm.CostCategoryMaster!;
            cat.Name = "Departments";
            cat.AllocateRevenueItems = true;
            Assert.True(cat.Create(), cat.Message);

            vm.ShowCostCentreMaster();
            var centre = vm.CostCentreMaster!;
            centre.SelectedCategory = centre.Categories.Single(c => c.Name == "Departments");
            centre.Name = "Delhi Branch";
            Assert.True(centre.Create(), centre.Message);

            vm.ShowLedgerMaster();
            var led = vm.LedgerMaster!;
            led.Name = "Salaries";
            led.SelectedGroup = vm.Company!.FindGroupByName("Indirect Expenses");
            Assert.True(led.Create(), led.Message);
            var salaries = vm.Company.FindLedgerByName("Salaries")!;

            Assert.True(vm.Company.EnableCostCentres, "Fixture guard: the feature must start ON.");
            vm.OpenVoucher(Apex.Ledger.Domain.VoucherBaseType.Payment);
            var on = vm.VoucherEntry!;
            on.ChangeMode();
            on.Lines[0].SelectedLedger = salaries;
            on.Lines[0].Side = Apex.Ledger.DrCr.Debit;
            on.Lines[0].AmountText = "60000";
            Assert.True(on.Lines[0].IsCostApplicable,
                "Fixture guard: with the feature ON this expense line must offer the cost panel, or the OFF " +
                "clause below proves nothing.");

            vm.Company.EnableCostCentres = false;
            vm.OpenVoucher(Apex.Ledger.Domain.VoucherBaseType.Payment);
            var off = vm.VoucherEntry!;
            off.ChangeMode();
            off.Lines[0].SelectedLedger = salaries;
            off.Lines[0].Side = Apex.Ledger.DrCr.Debit;
            off.Lines[0].AmountText = "60000";
            Assert.False(off.Lines[0].IsCostApplicable,
                "With the F11 Cost Centres feature OFF the voucher line still opens its cost-allocation panel " +
                "— the gate stops at the menu and the deepest half of the feature is ungated.");
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE SECOND DOOR.</b> The button bar carries a one-click "Cost Centres" and "Interest" straight to
    /// the two reports. A gate that only removed the menu rows would be cosmetic — the operator would switch
    /// the feature off, watch the menu row go, and still reach the report from the bar. Both doors close
    /// together, and the bar is rebuilt live by the F11 page's own onChanged hook.
    /// </summary>
    [AvaloniaFact]
    public void The_cost_centre_and_interest_quick_buttons_close_with_their_company_features()
    {
        var (window, vm, dir) = Open();
        try
        {
            window.KeyPressQwerty(PhysicalKey.F11, RawInputModifiers.None);
            Pump(window);
            var page = vm.GstConfig!;

            bool Enabled(string label) =>
                vm.ButtonBar.Single(b => b.Caption == label).Enabled;

            Assert.True(Enabled("Cost Centres"), "Fixture guard: the quick-button starts enabled.");
            Assert.True(Enabled("Interest"), "Fixture guard: the quick-button starts enabled.");

            page.EnableCostCentres = false;
            page.EnableInterestCalculation = false;
            Pump(window);

            Assert.False(Enabled("Cost Centres"),
                "The Cost Centres quick-button still opens the report after the company feature was switched " +
                "off — the gate removed the menu row and left a second door standing.");
            Assert.False(Enabled("Interest"),
                "The Interest quick-button still opens the report after the company feature was switched off.");

            // And nothing else on the bar was collateral damage.
            Assert.True(Enabled("Balance Sheet"));
            Assert.True(Enabled("Day Book"));
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>The F11 page is the WRITER, not just a display.</b> Ticking the caption on the page must move the
    /// live company — otherwise the three controls proved visible above would be decoration. Driven through
    /// the view-model property the checkbox is two-way bound to, which is the same write the tick performs.
    /// </summary>
    [AvaloniaFact]
    public void Ticking_the_accounting_options_on_the_f11_page_moves_the_live_company()
    {
        var (window, vm, dir) = Open();
        try
        {
            window.KeyPressQwerty(PhysicalKey.F11, RawInputModifiers.None);
            Pump(window);
            var page = vm.GstConfig!;

            // Seeded from the live company, not from a hard-coded default.
            Assert.Equal(vm.Company!.EnableBillWiseEntry, page.EnableBillWiseEntry);
            Assert.Equal(vm.Company.EnableCostCentres, page.EnableCostCentres);
            Assert.Equal(vm.Company.EnableInterestCalculation, page.EnableInterestCalculation);

            page.EnableBillWiseEntry = false;
            page.EnableCostCentres = false;
            page.EnableInterestCalculation = false;
            Assert.False(vm.Company.EnableBillWiseEntry);
            Assert.False(vm.Company.EnableCostCentres);
            Assert.False(vm.Company.EnableInterestCalculation);

            page.EnableCostCentres = true;
            Assert.True(vm.Company.EnableCostCentres);
        }
        finally { Cleanup(window, dir); }
    }
}
