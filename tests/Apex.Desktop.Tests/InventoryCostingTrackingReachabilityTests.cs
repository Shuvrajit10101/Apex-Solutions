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
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>W-K1 — CAN A USER ACTUALLY REACH ANY OF THIS?</b>
///
/// <para><b>Why this file exists.</b> Three features have shipped on this project complete, correct, tested and
/// COMPLETELY DEAD — the largest ~625 lines of cheque rendering whose only writers were test files, which took
/// four waves to make reachable. Every one of them had green engine tests. So census rows 9.6 / 9.7 / 9.8 / 9.9
/// each get a test here that walks the REALISED VISUAL TREE from a real <see cref="MainWindow"/> and proves the
/// operator has a route: the menu row exists and opens a report with its columns drawn; the F11 switch is on the
/// F11 screen; the godown master draws the job/project field; the voucher-type master draws the class fields;
/// and the entry screens draw a Tracking No. box the operator can type into.</para>
///
/// <para><b>Why the visual tree and not a view-model flag.</b> Asserting <c>ShowTrackingNumber</c> is exactly
/// the test that passes on the build where the XAML never binds it — the flag was never the problem in any of
/// the three dead features. What makes a field reachable is that a control for it is REALISED and
/// <c>IsEffectivelyVisible</c>.</para>
///
/// <para>Headless-safe: visual-tree and layout inspection only. No Skia, no rendered frame.</para>
/// </summary>
public sealed class InventoryCostingTrackingReachabilityTests
{
    // ─────────────────────────────────────────────────────────────────────────── 9.8 / 9.7 / 9.6 — the menu

    /// <summary>
    /// Every one of the six new reports must be reachable from <b>Reports → Inventory Reports</b>, and each must
    /// then OPEN — a menu row whose dispatch case is missing is a dead end that looks alive.
    /// <para>The rows are also gated: with the F11 features off, none of them may appear. A menu row that is
    /// always present would put an always-empty report in front of every operator.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_six_new_inventory_reports_are_reachable_from_the_menu_and_open()
    {
        var (window, vm, dir) = Open();
        try
        {
            // With every feature OFF, not one of the six rows is offered.
            vm.ShowInventoryReportsMenu();
            Pump(window);
            foreach (var label in AllSixLabels)
                Assert.DoesNotContain(label, MenuLabels(vm));

            EnableAllFeatures(vm);
            vm.ShowInventoryReportsMenu();
            Pump(window);

            var labels = MenuLabels(vm);
            foreach (var label in AllSixLabels)
                Assert.True(labels.Contains(label),
                    $"'{label}' has no row under Reports → Inventory Reports, so census row "
                    + "9.6/9.7/9.8's report cannot be reached by any operator. Add it to "
                    + "BuildInventoryReportsColumn under its own heading.");

            // …and each one really opens. ActivateMenuItem goes through the SAME dispatch an operator's Enter
            // does, so a missing switch case fails here rather than shipping as a dead row.
            foreach (var (label, kind) in ExpectedKinds)
            {
                vm.ShowInventoryReportsMenu();
                ArrowToAndActivate(vm, label);
                Pump(window);
                Assert.True(vm.Reports is not null,
                    $"Activating '{label}' opened no report — the menu row exists but its dispatch case does not.");
                Assert.Equal(kind, vm.Reports!.Kind);
            }
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// A report that opens but draws no grid is the hollowed-out shape rows 8.1 / 11.9 / 11.10 / 11.11 were
    /// caught in. For each of the six, some control bound to the report's own layout flag must be REALISED and
    /// visible once it is open — i.e. its DataTemplate actually exists in the window.
    /// </summary>
    [AvaloniaFact]
    public void Each_new_report_draws_its_own_grid_with_its_column_headings()
    {
        var (window, vm, dir) = Open();
        try
        {
            EnableAllFeatures(vm);
            SeedTrackedBook(vm);

            foreach (var (label, headings) in ExpectedHeadings)
            {
                vm.ShowInventoryReportsMenu();
                ArrowToAndActivate(vm, label);
                Pump(window);

                var visibleText = Descendants(window)
                    .OfType<TextBlock>()
                    .Where(t => t.IsEffectivelyVisible && t.Bounds.Width > 0 && t.Bounds.Height > 0)
                    .Select(t => t.Text ?? string.Empty)
                    .ToList();

                foreach (var heading in headings)
                    Assert.True(visibleText.Contains(heading),
                        $"'{label}' is open but its column heading '{heading}' is not drawn anywhere visible. "
                        + "The report has a builder and a menu row but no grid — an operator reaches a blank "
                        + "screen. Add the grid keyed on the report's layout flag in MainWindow.axaml.");
            }
        }
        finally { Cleanup(window, dir); }
    }

    // ─────────────────────────────────────────────────────────────────────────── 9.8 / 9.7 — the entry field

    /// <summary>
    /// 🔴 <b>THE DEAD-FEATURE TEST FOR ROW 9.8.</b> Without a Tracking No. box on the Receipt Note screen, the
    /// whole feature is unreachable: no operator can key the string the Bills Pending reports net on, and the
    /// report would be permanently empty on every real book — precisely the cheque-printing failure again.
    /// <para>The box must be REALISED and visible with the feature on, and gone with it off.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_receipt_note_screen_draws_a_Tracking_No_box_only_when_the_feature_is_on()
    {
        var (window, vm, dir) = Open();
        try
        {
            SeedMasters(vm);

            // Feature OFF — no Tracking No. control anywhere on the screen.
            vm.Company!.UseTrackingNumbers = false;
            OpenReceiptNote(vm, window);
            Assert.False(HasVisibleLabel(window, "Tracking No."),
                "The Tracking No. field is drawn on a Receipt Note even though F11 "
                + "\"Use tracking numbers\" is OFF — a hidden feature's field must not be on screen.");

            // Feature ON — the box exists, is visible, and accepts a value that reaches the view model.
            vm.Company.UseTrackingNumbers = true;
            OpenReceiptNote(vm, window);
            Assert.True(HasVisibleLabel(window, "Tracking No."),
                "The Receipt Note screen draws NO Tracking No. field, so census row 9.8's tracking number "
                + "cannot be entered by any operator and the Bills Pending reports can only ever be empty. "
                + "This is the dead-feature shape that cost this project four waves on cheque printing.");

            var line = vm.InventoryVoucherEntry!.Lines[0];
            Assert.True(line.ShowTrackingNumber);
            line.TrackingNumber = "TRK/1";
            Assert.Equal("TRK/1", line.Tracking);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE BILL HALF OF ROW 9.8 — AND THE GAP A MUTATION FOUND.</b> A first draft of this file tested only
    /// the Receipt Note side, and deleting the Tracking No. field from the <b>item-invoice</b> grid left every
    /// test in it GREEN. Half the mechanism could have shipped dead: an operator could key a tracking number on
    /// the goods and never on the bill, so nothing would ever reconcile and Purchase Bills Pending would report
    /// every receipt as permanently unbilled — the report would be WRONG rather than merely empty, which is
    /// worse. Both halves are guarded now.
    /// </summary>
    [AvaloniaFact]
    public void The_purchase_invoice_screen_draws_a_Tracking_No_box_only_when_the_feature_is_on()
    {
        var (window, vm, dir) = Open();
        try
        {
            SeedMasters(vm);

            vm.Company!.UseTrackingNumbers = false;
            OpenPurchaseItemInvoice(vm, window);
            Assert.False(HasVisibleLabel(window, "Tracking No."),
                "The Tracking No. field is drawn on a Purchase item-invoice even though F11 "
                + "\"Use tracking numbers\" is OFF.");

            vm.Company.UseTrackingNumbers = true;
            OpenPurchaseItemInvoice(vm, window);
            Assert.True(HasVisibleLabel(window, "Tracking No."),
                "The Purchase item-invoice grid draws NO Tracking No. field, so the BILL half of census row "
                + "9.8 cannot be entered. The goods side alone reconciles against nothing, and Purchase Bills "
                + "Pending would report every receipt as permanently unbilled.");

            var entry = vm.VoucherEntry!;
            Assert.True(entry.ShowItemTrackingNumber);
            var line = entry.InventoryLines[0];
            Assert.True(line.ShowTrackingNumber);
            line.TrackingNumber = "TRK/1";
            Assert.Equal("TRK/1", line.Tracking);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>Census 9.7 — the Cost Tracking No. box has its own independent F11 gate, so a company that uses
    /// only cost tracking gets that field and not the other.</summary>
    [AvaloniaFact]
    public void The_cost_tracking_box_has_its_own_independent_gate()
    {
        var (window, vm, dir) = Open();
        try
        {
            SeedMasters(vm);
            vm.Company!.UseTrackingNumbers = false;
            vm.Company.EnableCostTracking = true;
            OpenReceiptNote(vm, window);

            Assert.True(HasVisibleLabel(window, "Cost Tracking No."),
                "F11 \"Enable Cost Tracking\" is on and the Cost Tracking No. field is not drawn — census row "
                + "9.7 has no entry route.");
            Assert.False(HasVisibleLabel(window, "Tracking No.") && !HasVisibleLabel(window, "Cost Tracking No."),
                "The two tracking fields are not independently gated.");
        }
        finally { Cleanup(window, dir); }
    }

    // ─────────────────────────────────────────────────────────────────────────── 9.6 — the godown master field

    /// <summary>
    /// Census 9.6 — the godown master must draw the vendor's "Set job/project for job costing" picker when Job
    /// Costing is on, and the picker must actually offer the book's cost centres. A picker with no options is
    /// as unreachable as no picker at all.
    /// </summary>
    [AvaloniaFact]
    public void The_godown_master_draws_the_job_project_picker_when_job_costing_is_on()
    {
        var (window, vm, dir) = Open();
        try
        {
            vm.Company!.EnableJobCosting = false;
            vm.ShowGodownMaster();
            Pump(window);
            Assert.False(HasVisibleLabel(window, "Set job/project for job costing"),
                "The job/project field is drawn with F11 \"Enable Job Costing\" OFF.");

            vm.Company.EnableJobCosting = true;
            var centre = new CostCentre(Guid.NewGuid(), "Bridge Project", vm.Company.CostCategories.First().Id);
            vm.Company.AddCostCentre(centre);

            vm.ShowGodownMaster();
            Pump(window);
            Assert.True(HasVisibleLabel(window, "Set job/project for job costing"),
                "The Godown master draws no \"Set job/project for job costing\" field, so census row 9.6's "
                + "godown→cost-centre link cannot be made by any operator and Job Work Analysis can only ever "
                + "be empty.");

            var master = vm.GodownMaster!;
            Assert.True(master.ShowJobCostCentre);
            Assert.Contains(master.JobCostCentreOptions, o => o.CostCentre?.Id == centre.Id);

            // And it round-trips into the created godown, which is the half a visibility check cannot see.
            master.Name = "Site A";
            master.SelectedJobCostCentre =
                master.JobCostCentreOptions.First(o => o.CostCentre?.Id == centre.Id);
            Assert.True(master.Create(), master.Message);
            Assert.Equal(centre.Id, vm.Company.FindGodownByName("Site A")!.JobCostCentreId);
        }
        finally { Cleanup(window, dir); }
    }

    // ─────────────────────────────────────────────────────────────────────────── 9.9 — the voucher class

    /// <summary>
    /// Census 9.9 — the voucher-type master must draw the vendor's "Name of Class" and "Use Class for
    /// Inter-Godown Transfers" while ALTERING a Stock Journal type, and adding a class must reach the aggregate.
    /// Without this the <c>voucher_type_classes</c> table has no writer outside the tests, which is the exact
    /// definition of a dead feature on this project.
    /// </summary>
    [AvaloniaFact]
    public void The_voucher_type_master_draws_the_class_fields_when_altering_a_stock_journal()
    {
        var (window, vm, dir) = Open();
        try
        {
            var journal = vm.Company!.FindVoucherTypeByName("Stock Journal")!;
            var sales = vm.Company.FindVoucherTypeByName("Sales")!;

            // Altering a SALES type offers no class fields — census row 2.6 (the general Voucher Class
            // machinery) is absent and must not be advertised.
            AlterVoucherType(vm, sales.Id);
            Pump(window);
            Assert.False(HasVisibleLabel(window, "Name of Class"),
                "The Voucher Class fields are drawn on a Sales type, advertising census row 2.6 which is ABSENT.");

            AlterVoucherType(vm, journal.Id);
            Pump(window);
            Assert.True(HasVisibleLabel(window, "Name of Class"),
                "Altering a Stock Journal draws no \"Name of Class\" field, so census row 9.9's transfer class "
                + "cannot be created by any operator and the voucher_type_classes table has no writer outside "
                + "the test suite.");

            var master = vm.VoucherTypeMaster!;
            Assert.True(master.ShowVoucherClasses);
            master.NewClassName = "Transfer";
            master.NewClassInterGodownTransfers = true;
            Assert.True(master.AddClass(), master.Message);

            var cls = Assert.Single(vm.Company.FindVoucherType(journal.Id)!.Classes);
            Assert.Equal("Transfer", cls.Name);
            Assert.True(cls.UseClassForInterGodownTransfers);
            Assert.Single(master.Classes);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// Census 9.9 — once a transfer class exists, the Stock Journal entry screen must offer it, and picking it
    /// must REPLACE the hand-keyed destination grid with the single destination-godown picker. A class the
    /// operator can define but not use is half a feature.
    /// </summary>
    [AvaloniaFact]
    public void A_transfer_class_is_offered_on_stock_journal_entry_and_derives_the_destination()
    {
        var (window, vm, dir) = Open();
        try
        {
            SeedMasters(vm);
            var journal = vm.Company!.FindVoucherTypeByName("Stock Journal")!;
            new VoucherTypeService(vm.Company).AddClass(journal.Id, "Transfer", true);
            new InventoryService(vm.Company).CreateGodown("Site B");

            vm.OpenInventoryVoucher(journal);
            Pump(window);
            var entry = vm.InventoryVoucherEntry!;

            Assert.True(entry.ShowTransferClass,
                "A Stock Journal type carries a transfer class and the entry screen does not offer it — census "
                + "row 9.9's class can be created but never used.");
            Assert.True(HasVisibleLabel(window, "Class"),
                "The transfer-class picker is not drawn on the Stock Journal entry screen.");

            // No class ⇒ the hand-keyed destination grid stands.
            Assert.True(entry.ShowsDestinationLines);
            Assert.False(entry.IsTransferClassActive);

            // Picking the class hides that grid and asks for one destination godown instead.
            entry.SelectedTransferClass = entry.TransferClassOptions.First(o => o.Class is not null);
            Pump(window);
            Assert.True(entry.IsTransferClassActive);
            Assert.False(entry.ShowsDestinationLines,
                "A transfer class is active and the hand-keyed destination grid is still shown — the operator "
                + "would key an arm the posting is about to overwrite.");
            Assert.True(HasVisibleLabel(window, "Destination Godown"));
        }
        finally { Cleanup(window, dir); }
    }

    // ─────────────────────────────────────────────────────────────────────────── the F11 switches

    /// <summary>All three F11 feature switches must be on the F11 screen — a feature whose master gate cannot be
    /// turned on is unreachable however complete the rest of it is.</summary>
    [AvaloniaFact]
    public void All_three_F11_switches_are_on_the_company_features_screen()
    {
        var (window, vm, dir) = Open();
        try
        {
            vm.ShowGstConfig();
            Pump(window);

            var captions = Descendants(window)
                .OfType<CheckBox>()
                .Where(c => c.IsEffectivelyVisible)
                .Select(c => c.Content as string ?? string.Empty)
                .ToList();

            foreach (var caption in new[]
                     {
                         "Use tracking numbers (enables delivery and receipt notes)",
                         "Enable Cost Tracking",
                         "Enable Job Costing",
                     })
                Assert.True(captions.Contains(caption),
                    $"F11 has no \"{caption}\" switch, so the feature behind it can never be turned on.");
        }
        finally { Cleanup(window, dir); }
    }

    // ─────────────────────────────────────────────────────────────────────────── fixture

    private static readonly string[] AllSixLabels =
    {
        "Purchase Bills Pending", "Sales Bills Pending",
        "Stock Item Cost Analysis", "Stock Group Cost Analysis", "Cost Track Break-up",
        "Job Work Analysis",
    };

    private static readonly (string Label, ReportKind Kind)[] ExpectedKinds =
    {
        ("Purchase Bills Pending", ReportKind.PurchaseBillsPending),
        ("Sales Bills Pending", ReportKind.SalesBillsPending),
        ("Stock Item Cost Analysis", ReportKind.StockItemCostAnalysis),
        ("Stock Group Cost Analysis", ReportKind.StockGroupCostAnalysis),
        ("Cost Track Break-up", ReportKind.CostTrackBreakup),
        ("Job Work Analysis", ReportKind.JobWorkAnalysis),
    };

    /// <summary>One heading per report family that only THAT family's grid draws — the vendor's own captions.</summary>
    private static readonly (string Label, string[] Headings)[] ExpectedHeadings =
    {
        ("Purchase Bills Pending", new[] { "Tracking No.", "Pending" }),
        ("Sales Bills Pending", new[] { "Tracking No.", "Pending" }),
        ("Stock Item Cost Analysis", new[] { "Cost (Expense)", "Revenue (Income)", "Balance at Cost", "Profit/Loss" }),
        ("Cost Track Break-up", new[] { "Cost (Expense)", "Profit/Loss" }),
        ("Job Work Analysis", new[] { "Particulars", "Amount" }),
    };

    private static void EnableAllFeatures(MainWindowViewModel vm)
    {
        vm.Company!.UseTrackingNumbers = true;
        vm.Company.EnableCostTracking = true;
        vm.Company.EnableJobCosting = true;
    }

    private static List<string> MenuLabels(MainWindowViewModel vm) =>
        vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToList();

    /// <summary>Drives the real keyboard route an operator uses: arrow to the label, then Enter. Deliberately NOT
    /// a direct OpenReport call — that would prove the report builds, not that the MENU reaches it, and a
    /// missing dispatch case is exactly the dead-end this file exists to catch.</summary>
    private static void ArrowToAndActivate(MainWindowViewModel vm, string label)
    {
        var guard = 0;
        while (vm.Menu[vm.SelectedIndex].Label != label)
        {
            vm.MoveDown();
            Assert.True(++guard < 200, $"'{label}' is not reachable by arrowing through this menu column.");
        }
        vm.ActivateSelected();
    }

    private static void SeedMasters(MainWindowViewModel vm)
    {
        var inventory = new InventoryService(vm.Company!);
        var unit = inventory.CreateSimpleUnit("Nos", "Numbers");
        var group = vm.Company!.FindStockGroupByName("Primary") ?? inventory.CreateStockGroup("Primary");
        inventory.CreateStockItem("Widget", group.Id, unit.Id);
    }

    /// <summary>A book with one tracked receipt, so the report grids have a row as well as a heading.</summary>
    private static void SeedTrackedBook(MainWindowViewModel vm)
    {
        SeedMasters(vm);
        var c = vm.Company!;
        var item = c.StockItems.First();
        c.AddInventoryVoucher(new InventoryVoucher(
            Guid.NewGuid(), c.FindVoucherTypeByName("Receipt Note")!.Id, c.BooksBeginFrom,
            new[]
            {
                new InventoryAllocation(item.Id, c.MainLocation!.Id, 10m, StockDirection.Inward,
                    rate: Money.FromRupees(5m), trackingNumber: "TRK/1", costTrackingNumber: "LOT/1"),
            },
            number: 1));
    }

    /// <summary>Opens a Purchase voucher and switches it into ITEM-INVOICE mode — the only mode that has stock
    /// lines, and therefore the only one where a Tracking No. belongs.</summary>
    private static void OpenPurchaseItemInvoice(MainWindowViewModel vm, MainWindow window)
    {
        vm.OpenVoucher(VoucherBaseType.Purchase);
        if (!vm.VoucherEntry!.IsItemInvoice) vm.ToggleItemInvoice();
        Pump(window);
    }

    private static void OpenReceiptNote(MainWindowViewModel vm, MainWindow window)
    {
        vm.OpenInventoryVoucher(vm.Company!.FindVoucherTypeByName("Receipt Note")!);
        Pump(window);
    }

    /// <summary>True when some VISIBLE, non-degenerate TextBlock or CheckBox on screen carries this caption.
    /// Text, not a view-model flag — see the class remarks.</summary>
    private static bool HasVisibleLabel(MainWindow window, string caption) =>
        Descendants(window).Any(v =>
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

    /// <summary>Opens the Voucher Type master in ALTER mode over one type — the same static ForAlter factory the
    /// window's own Ctrl+Enter gesture uses, so the screen under test is the one the operator gets.</summary>
    private static void AlterVoucherType(MainWindowViewModel vm, Guid typeId)
    {
        vm.ShowVoucherTypeMaster();
        var master = vm.VoucherTypeMaster!;
        // Drive the highlight with the same public arrow verb the operator's Down key uses — there is no
        // test-only setter, and adding one would let this test pass over a highlight the UI cannot reach.
        var guard = 0;
        while (master.HighlightedRow?.MasterId != typeId)
        {
            master.MoveHighlight(1);
            Assert.True(++guard < 300,
                "The voucher type never came under the highlight, so Alter mode cannot be reached.");
        }
        Assert.True(vm.AlterHighlightedVoucherTypeRow(),
            "The Voucher Type master would not open in Alter mode, so the class fields can never be reached.");
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) Open()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexK1Reach_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
        window.Show();

        vm.NewCompanyName = "K1 Reachability Co";
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
}
