using System;
using System.Collections.Specialized;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Views;

public partial class MainWindow : Window
{
    private INotifyCollectionChanged? _watchedColumns;

    public MainWindow()
    {
        InitializeComponent();
        // Handle keys at the tunnelling stage so arrow/Enter/Esc work regardless of focus.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) => HookCascadeAutoScroll();
        HookCascadeAutoScroll();
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    /// <summary>
    /// Keeps the newly-active (rightmost) cascade column in view: whenever a column is added/removed we
    /// scroll the horizontal cascade viewport to its far right so the focused column is never left
    /// clipped behind the viewport edge (macOS-Finder column-view behaviour).
    /// </summary>
    private void HookCascadeAutoScroll()
    {
        if (_watchedColumns is not null)
            _watchedColumns.CollectionChanged -= OnColumnsChanged;

        _watchedColumns = Vm?.Columns;
        if (_watchedColumns is not null)
            _watchedColumns.CollectionChanged += OnColumnsChanged;
    }

    private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        // Defer to the next layout pass so the new column has a measured width to scroll to, then move
        // the horizontal offset to the far right (the active column is always the rightmost).
        => Dispatcher.UIThread.Post(ScrollCascadeToActiveColumn, DispatcherPriority.Loaded);

    private void ScrollCascadeToActiveColumn()
    {
        var scroller = CascadeScroller;
        if (scroller is null) return;
        var maxX = System.Math.Max(0, scroller.Extent.Width - scroller.Viewport.Width);
        scroller.Offset = new Avalonia.Vector(maxX, scroller.Offset.Y);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm is null) return;

        // RQ-7 keyboard drill (defect-1): Enter must drill the highlighted drillable report/drill row BEFORE
        // the Window's generic Enter handling (which drives cascade navigation via ActivateSelected) consumes
        // it. This tunnel handler is on the Window, so it fires ahead of the report ListBox's own bubble
        // KeyDown; the VM drills the ACTIVE pane's two-way-bound SelectedRow (focus-independent). A no-op on a
        // non-drillable row / non-report screen, so Enter stays a safe no-op there. Double-click still drills.
        if (e.Key == Key.Enter && vm.DrillSelectedRow())
        {
            e.Handled = true;
            return;
        }

        // Ctrl+A saves/accepts (accept shortcut) — apply the F12 report config, else create company /
        // accept voucher / create ledger.
        if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (vm.CurrentScreen == Screen.ReportConfig)
                vm.ApplyReportConfig();
            else if (vm.CurrentScreen == Screen.ReportSortFilter)
                vm.ApplyReportSortFilter();
            else if (vm.CurrentScreen == Screen.AddComparisonColumn)
                vm.ApplyAddComparisonColumn();
            else if (vm.CurrentScreen == Screen.AutoColumns)
                vm.ApplyAutoColumns();
            else if (vm.CurrentScreen == Screen.SaveView)
                vm.ApplySaveView();
            else if (vm.CurrentScreen == Screen.SavedViews)
                vm.OpenSelectedSavedView();
            else if (vm.CurrentScreen == Screen.PrintConfig)
                vm.ApplyPrintConfig();
            else if (vm.CurrentScreen == Screen.Export)
                vm.ApplyExport();
            else if (vm.CurrentScreen == Screen.ExportData)
                vm.ApplyExportData();
            else if (vm.CurrentScreen == Screen.ImportData)
                vm.ApplyImport();
            else if (vm.CurrentScreen == Screen.PrintPreview)
                SavePrintPreviewToDocuments(vm);
            else if (vm.CurrentScreen == Screen.EmailCompose)
                SaveEmailToDocuments(vm);
            else if (vm.CurrentScreen == Screen.SmtpSettings)
                vm.SaveSmtpSettings();
            else
                vm.ActivateSelected();
            e.Handled = true;
            return;
        }

        // Reorder Levels master (RQ-53): Alt+S toggles the reorder level Simple⇄Advanced; Alt+V toggles the
        // minimum-order-qty Simple⇄Advanced. Scoped to that screen so they never collide elsewhere.
        if (vm.CurrentScreen == Screen.ReorderLevelsMaster && e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.S) { vm.ReorderLevels?.ToggleReorderAdvanced(); e.Handled = true; return; }
            if (e.Key == Key.V) { vm.ReorderLevels?.ToggleMinQtyAdvanced(); e.Handled = true; return; }
        }

        // Alt+X cancels the in-progress voucher/ledger without saving (cancel shortcut).
        if (e.Key == Key.X && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            vm.CancelVoucher();
            e.Handled = true;
            return;
        }

        // RQ-4 comparative shortcuts take priority while a report is the active page: Alt+C opens the "Add
        // Comparison Column" panel, Alt+N opens the "Auto Columns" chooser. Checked BEFORE the global Alt+C
        // (Create Ledger) so on a report page Alt+C compares columns rather than creating a ledger. Only fires
        // on a comparative-capable report (TB / BS / P&L / Stock Summary); otherwise it falls through.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && vm.IsReportContext && vm.Reports is { SupportsComparative: true })
        {
            switch (e.Key)
            {
                case Key.C: vm.OpenAddComparisonColumn(); e.Handled = true; return;
                case Key.N: vm.OpenAutoColumns(); e.Handled = true; return;
            }
        }

        // Alt+C opens the Ledger-creation master whenever a company is open.
        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            vm.CreateLedgerShortcut();
            e.Handled = true;
            return;
        }

        // Ctrl+B settles the spacebar-selected bills on the Outstandings page (Bill Settlement).
        if (e.Key == Key.B && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            vm.SettleBills();
            e.Handled = true;
            return;
        }

        // Alt+B (NFR-2 / RQ-3) opens the batch-allocation sub-screen for a batch-tracked inventory-voucher line —
        // the keyboard equivalent of the "⧉" affordance the tooltip advertises. Resolves the focused line from the
        // key source (so Alt+B on a specific row targets it), falling back to the first eligible line on the
        // screen. Placed before the general Alt letter shortcuts so it never falls through; a safe no-op when no
        // line currently qualifies (company flag off / non-batch item / no godown / qty 0).
        if (e.Key == Key.B && e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && vm.CurrentScreen == Screen.InventoryVoucherEntry
            && vm.InventoryVoucherEntry is { } entry)
        {
            var focused = FocusedInventoryLine(e);
            if (focused is not null && entry.LineWantsBatchAllocation(focused))
                entry.RequestBatchAllocation(focused);
            else
                entry.RequestBatchAllocationForFirstEligibleLine();
            e.Handled = true;
            return;
        }

        // Ctrl+T toggles the in-progress voucher as post-dated (post-dated cheque handling).
        if (e.Key == Key.T && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            vm.TogglePostDated();
            e.Handled = true;
            return;
        }

        // Ctrl+L toggles the in-progress voucher as Optional (a provisional, scenario-only entry).
        if (e.Key == Key.L && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            vm.ToggleOptional();
            e.Handled = true;
            return;
        }

        // Ctrl+I toggles a Purchase/Sales voucher between plain accounting and item-invoice ("as invoice") mode.
        if (e.Key == Key.I && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            vm.ToggleItemInvoice();
            e.Handled = true;
            return;
        }

        // Alt+I toggles the in-progress POS bill between Single and Multi tender mode (both ways, RQ-42). Scoped to
        // the POS Billing screen so it never collides elsewhere; the item-invoice toggle stays on Ctrl+I.
        if (e.Key == Key.I && e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && vm.CurrentScreen == Screen.PosBilling)
        {
            vm.TogglePosPaymentMode();
            e.Handled = true;
            return;
        }

        // Alt+A surfaces the POS bill's per-rate tax analysis (RQ-53). Scoped to the POS Billing screen; Ctrl+A
        // (accept) is a separate binding (Control) so this does not shadow it.
        if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && vm.CurrentScreen == Screen.PosBilling)
        {
            vm.ShowPosTaxAnalysis();
            e.Handled = true;
            return;
        }

        // Ctrl+S (RQ-8) opens the "Save View" panel over an open report — name and store the report's current
        // configuration (kind + period/as-of + detail + F12 options + sort/filter + comparative columns). Report
        // context only, so it never fires while a drill column is the active pane. Ctrl+A on the panel saves it.
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control) && vm.IsReportContext)
        {
            vm.OpenSaveView();
            e.Handled = true;
            return;
        }

        // Alt+K (RQ-8) opens the "Saved Views" list — the company's saved report views (open/apply or delete one).
        // Available over any report page; needs a company. Checked before the global Alt shortcuts.
        if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Alt) && vm.IsReportContext)
        {
            vm.OpenSavedViews();
            e.Handled = true;
            return;
        }

        // P / Ctrl+P (RQ-9) opens the Print Preview of the CURRENT report — renders it to a de-branded PDF and
        // shows the paginated layout; "Save PDF" writes the bytes. Report context only (so the bare P never
        // fires while a drill column is active). Checked before the bare-P menu quick-jump (Profit & Loss),
        // which is guarded to menu screens, and before the Ctrl+P falls through to anything else.
        if (e.Key == Key.P && vm.IsPrintablePage && !e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !IsTyping(e))
        {
            vm.OpenPrintPreview();
            e.Handled = true;
            return;
        }

        // E / Alt+E (RQ-14/16) opens the Export panel for the CURRENT report OR master list (Chart of Accounts,
        // ledgers, stock items) — choose CSV/XLSX/PDF, folder, filename and an optional timestamp; applying
        // writes the file via Apex.Ledger.Io. Exportable-page context only (a report or a master list), and not
        // while typing in a field (so a name-entry keystroke on a master screen goes to the field, not the
        // export jump). Accepts both the bare E and Alt+E (the header hint reads "E: Export"). No Ctrl.
        if (e.Key == Key.E && vm.IsExportablePage && !e.KeyModifiers.HasFlag(KeyModifiers.Control) && !IsTyping(e))
        {
            vm.OpenExport();
            e.Handled = true;
            return;
        }

        // O / Alt+O (Gateway → Import; RQ-20..24) opens the "Import" panel: read a canonical JSON/XML backup (or a
        // flat CSV) + choose the duplicate policy, then engine-routed apply into the open company. Only on the bare
        // Gateway cascade (a company is open, no page/voucher/master column on top, not typing) — the header hint
        // reads "O: Import". Accepts the bare O and Alt+O; never fires inside a voucher/ledger field.
        if (e.Key == Key.O && vm.CurrentScreen == Screen.Gateway
            && !e.KeyModifiers.HasFlag(KeyModifiers.Control) && !IsTyping(e))
        {
            vm.OpenImport();
            e.Handled = true;
            return;
        }

        // Y (Gateway → Export Data; RQ-19/DP-4) opens the "Export Data" panel: a canonical JSON/XML backup of the
        // whole company. Same Gateway-root guard as Import — the header hint reads "Y: Data".
        if (e.Key == Key.Y && vm.CurrentScreen == Screen.Gateway
            && !e.KeyModifiers.HasFlag(KeyModifiers.Control) && !IsTyping(e))
        {
            vm.OpenExportData();
            e.Handled = true;
            return;
        }

        // M / Ctrl+M (RQ-25/26) opens the "E-Mail" compose panel for the CURRENT report or the drilled voucher /
        // tax invoice — the attachment defaults to its exported PDF. The hand-off is OFFLINE: Save writes a
        // byte-stable .eml (with the attachment) or a mailto opens the OS mail client — nothing is sent. Printable
        // page context only (a report, or a voucher-detail drill), and not while typing. The header hint reads
        // "M: E-Mail". Accepts the bare M and Ctrl+M.
        if (e.Key == Key.M && vm.IsPrintablePage && !e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !IsTyping(e))
        {
            vm.OpenEmailCompose();
            e.Handled = true;
            return;
        }

        // Spacebar toggles the highlighted bill's multi-select on the Outstandings page (not while typing).
        if (e.Key == Key.Space && vm.IsOutstandingsScreen && !IsTyping(e))
        {
            vm.ToggleOutstandingSelection();
            e.Handled = true;
            return;
        }

        // Inventory/order voucher shortcuts (modifier + F-key). Checked before the plain F-key switch so a
        // modified F-key never falls through to its bare-key report/voucher action. Physical Stock is
        // menu-only (F10 has no standalone modifier hotkey), matching the seed.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            switch (e.Key)
            {
                // Ctrl+F9 on the Reorder Status report raises a Purchase Order pre-filled from the selected row
                // (item + main location + Order-to-be-Placed qty; RQ-53). Checked before the blank-PO shortcut.
                case Key.F9 when vm.IsReorderStatusReport: vm.RaisePurchaseOrderFromReorder(); e.Handled = true; return;
                case Key.F9: vm.OpenInventoryVoucher(Apex.Ledger.Domain.VoucherBaseType.PurchaseOrder); e.Handled = true; return;
                case Key.F8: vm.OpenInventoryVoucher(Apex.Ledger.Domain.VoucherBaseType.SalesOrder); e.Handled = true; return;
                case Key.F6: vm.OpenInventoryVoucher(Apex.Ledger.Domain.VoucherBaseType.RejectionIn); e.Handled = true; return;
                case Key.F5: vm.OpenInventoryVoucher(Apex.Ledger.Domain.VoucherBaseType.RejectionOut); e.Handled = true; return;
            }
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Report parameter shortcuts (RQ-1/RQ-2) take priority while a report is the active page: Alt+F1
            // toggles detailed↔summary, Alt+F2 sets the period window. Checked before the inventory Alt+F
            // shortcuts so they never fire on a report page.
            if (vm.IsReportContext)
            {
                switch (e.Key)
                {
                    case Key.F1: vm.ReportToggleDetailed(); e.Handled = true; return;
                    case Key.F2: vm.ReportSetPeriod(); e.Handled = true; return;
                    // Alt+F12 opens the RQ-3 Sort/Filter panel. Placed with the report shortcuts and before
                    // the inventory Alt+F block so it never collides with the inventory voucher hotkeys.
                    case Key.F12: vm.OpenReportSortFilter(); e.Handled = true; return;
                }
            }

            switch (e.Key)
            {
                case Key.F9: vm.OpenInventoryVoucher(Apex.Ledger.Domain.VoucherBaseType.ReceiptNote); e.Handled = true; return;
                case Key.F8: vm.OpenInventoryVoucher(Apex.Ledger.Domain.VoucherBaseType.DeliveryNote); e.Handled = true; return;
                // Alt+F7 (RQ-53): a Manufacturing Journal is a Stock-Journal-derived type, so once the BOM feature
                // is on (F12 "Set Components (BOM)") Alt+F7 opens the Manufacturing Journal; otherwise it stays the
                // plain Stock Journal, so a non-BOM company is unaffected.
                case Key.F7 when vm.Company is { SetComponentsBom: true }:
                    vm.OpenManufacturingJournal(); e.Handled = true; return;
                case Key.F7: vm.OpenInventoryVoucher(Apex.Ledger.Domain.VoucherBaseType.StockJournal); e.Handled = true; return;
            }
        }

        // F12 on an open voucher/invoice print-preview opens the RQ-12 print-config panel (title override,
        // narration on/off, copy marking). Checked before the report F12 so it never re-opens report config.
        if (e.Key == Key.F12 && vm.CurrentScreen == Screen.PrintPreview
            && !e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            vm.OpenPrintConfig();
            e.Handled = true;
            return;
        }

        // Bare F2 / F12 on a report page act on the report (RQ-1 as-of, RQ-6 configuration) rather than the
        // global button bar. Checked before the general switch. Ctrl+A on the open F12 panel applies it.
        if (vm.IsReportContext && !e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            switch (e.Key)
            {
                case Key.F2: vm.ReportSetAsOf(); e.Handled = true; return;
                case Key.F12: vm.OpenReportConfig(); e.Handled = true; return;
                // F8 on the Reorder Status report toggles the "reorder only" filter (RQ-53). Checked before the
                // bare-F8 global button-bar action so it never falls through on that report.
                case Key.F8 when vm.IsReorderStatusReport: vm.ReportToggleReorderOnly(); e.Handled = true; return;
            }
        }

        switch (e.Key)
        {
            case Key.Up when !IsTyping(e):
                vm.MoveUp();
                e.Handled = true;
                break;
            case Key.Down when !IsTyping(e):
                vm.MoveDown();
                e.Handled = true;
                break;
            // Right / Enter drills into the highlighted item (adds the column to the right and moves
            // focus there). Right is a navigation key only when not editing a text field.
            case Key.Right when !IsTyping(e):
                vm.DrillIn();
                e.Handled = true;
                break;
            case Key.Enter:
                vm.ActivateSelected();
                e.Handled = true;
                break;
            // Left / Esc removes the rightmost column (focus returns to the previous column). Left is a
            // navigation key only when not editing a text field (there it moves the caret).
            case Key.Left when !IsTyping(e):
                vm.Back();
                e.Handled = true;
                break;
            case Key.Escape:
                vm.Back();
                e.Handled = true;
                break;

            // F-key button bar (mirrors the right panel).
            case Key.F1: Fire(vm, "F1"); e.Handled = true; break;
            case Key.F2: Fire(vm, "F2"); e.Handled = true; break;
            case Key.F3: Fire(vm, "F3"); e.Handled = true; break;
            case Key.F4: Fire(vm, "F4"); e.Handled = true; break;
            case Key.F5: Fire(vm, "F5"); e.Handled = true; break;
            case Key.F6: Fire(vm, "F6"); e.Handled = true; break;
            case Key.F7: Fire(vm, "F7"); e.Handled = true; break;
            case Key.F8: Fire(vm, "F8"); e.Handled = true; break;
            case Key.F9: Fire(vm, "F9"); e.Handled = true; break;
            // F10 opens the "Other Vouchers" menu (Transactions → Vouchers → Other Vouchers) — the route to the
            // Job Work In/Out Order + Material In/Out screens (Phase 6 slice 8; RQ-45/RQ-53). Menu context only,
            // never while typing in a field.
            case Key.F10 when vm.Company is not null && !IsTyping(e):
                vm.ShowOtherVouchersMenu(); e.Handled = true; break;
            case Key.F11: Fire(vm, "F11"); e.Handled = true; break;
            case Key.F12: Fire(vm, "F12"); e.Handled = true; break;

            // Report quick letters — only on the menu screens (never while entering a voucher /
            // ledger, where the letter is meant for the field, not a report jump).
            case Key.B when CanQuickJump(vm, e): Fire(vm, "B"); e.Handled = true; break;
            case Key.P when CanQuickJump(vm, e): Fire(vm, "P"); e.Handled = true; break;
            case Key.T when CanQuickJump(vm, e): Fire(vm, "T"); e.Handled = true; break;
            case Key.D when CanQuickJump(vm, e): Fire(vm, "D"); e.Handled = true; break;
        }
    }

    private static bool IsTyping(KeyEventArgs e) => e.Source is TextBox;

    /// <summary>
    /// Resolves the inventory-voucher line the key event originated on by walking the control tree up from the
    /// key source to the first element whose DataContext is an <see cref="InventoryVoucherLineViewModel"/> — so
    /// Alt+B on a specific row targets that row. Returns null when the key came from outside any line row.
    /// </summary>
    private static InventoryVoucherLineViewModel? FocusedInventoryLine(KeyEventArgs e)
    {
        for (var c = e.Source as StyledElement; c is not null; c = c.Parent)
            if (c.DataContext is InventoryVoucherLineViewModel line)
                return line;
        return null;
    }

    /// <summary>Report quick-letters fire only on menu screens and never while typing in a field.</summary>
    private static bool CanQuickJump(MainWindowViewModel vm, KeyEventArgs e)
        => vm.IsMenuScreen && !IsTyping(e);

    private static void Fire(MainWindowViewModel vm, string key)
    {
        foreach (var b in vm.ButtonBar)
            if (b.Key == key)
            {
                if (b.Enabled) b.Action();
                return;
            }
    }

    private void OnCreateCompanyClick(object? sender, RoutedEventArgs e)
        => Vm?.CreateCompany();

    private void OnAcceptVoucherClick(object? sender, RoutedEventArgs e)
        => Vm?.VoucherEntry?.Accept();

    private void OnCancelVoucherClick(object? sender, RoutedEventArgs e)
        => Vm?.CancelVoucher();

    private void OnAddVoucherLineClick(object? sender, RoutedEventArgs e)
        => Vm?.AddVoucherLine();

    private void OnAddItemInvoiceLineClick(object? sender, RoutedEventArgs e)
        => Vm?.AddItemInvoiceLine();

    private void OnAddAdditionalCostClick(object? sender, RoutedEventArgs e)
        => Vm?.VoucherEntry?.AddAdditionalCostRow();

    private void OnAddTransferAdditionalCostClick(object? sender, RoutedEventArgs e)
        => Vm?.InventoryVoucherEntry?.AddAdditionalCostRow();

    private void OnAcceptInventoryVoucherClick(object? sender, RoutedEventArgs e)
        => Vm?.InventoryVoucherEntry?.Accept();

    private void OnCancelInventoryVoucherClick(object? sender, RoutedEventArgs e)
        => Vm?.CancelVoucher();

    private void OnAddInventoryLineClick(object? sender, RoutedEventArgs e)
        => Vm?.AddInventoryLine();

    private void OnAddInventoryDestinationLineClick(object? sender, RoutedEventArgs e)
        => Vm?.AddInventoryDestinationLine();

    private void OnAddBillAllocationClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: VoucherLineViewModel line })
            Vm?.AddBillAllocation(line);
    }

    private void OnAddCostAllocationClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: VoucherLineViewModel line })
            Vm?.AddCostAllocation(line);
    }

    private void OnCreateCostCategoryClick(object? sender, RoutedEventArgs e)
        => Vm?.CostCategoryMaster?.Create();

    private void OnCreateCostCentreClick(object? sender, RoutedEventArgs e)
        => Vm?.CostCentreMaster?.Create();

    private void OnSettleBillsClick(object? sender, RoutedEventArgs e)
        => Vm?.SettleBills();

    private void OnCreateLedgerClick(object? sender, RoutedEventArgs e)
        => Vm?.LedgerMaster?.Create();

    private void OnAddBudgetLineClick(object? sender, RoutedEventArgs e)
        => Vm?.BudgetMaster?.AddLine();

    private void OnCreateBudgetClick(object? sender, RoutedEventArgs e)
        => Vm?.BudgetMaster?.Create();

    private void OnReconcileBankClick(object? sender, RoutedEventArgs e)
        => Vm?.BankReconciliation?.Reconcile();

    private void OnImportBankStatementClick(object? sender, RoutedEventArgs e)
        => Vm?.BankStatementImport?.Import();

    private void OnCreateScenarioClick(object? sender, RoutedEventArgs e)
        => Vm?.ScenarioMaster?.Create();

    private void OnCreateCurrencyClick(object? sender, RoutedEventArgs e)
        => Vm?.CurrencyMaster?.CreateCurrency();

    private void OnCreateExchangeRateClick(object? sender, RoutedEventArgs e)
        => Vm?.CurrencyMaster?.CreateRate();

    private void OnCreateStockGroupClick(object? sender, RoutedEventArgs e)
        => Vm?.StockGroupMaster?.Create();

    private void OnCreateStockCategoryClick(object? sender, RoutedEventArgs e)
        => Vm?.StockCategoryMaster?.Create();

    private void OnCreateUnitClick(object? sender, RoutedEventArgs e)
        => Vm?.UnitMaster?.Create();

    private void OnCreateGodownClick(object? sender, RoutedEventArgs e)
        => Vm?.GodownMaster?.Create();

    private void OnCreateStockItemClick(object? sender, RoutedEventArgs e)
        => Vm?.StockItemMaster?.Create();

    private void OnCreateBatchClick(object? sender, RoutedEventArgs e)
        => Vm?.BatchMaster?.Create();

    private void OnAcceptBatchAllocationClick(object? sender, RoutedEventArgs e)
        => Vm?.AcceptCurrent();

    private void OnCreateBomClick(object? sender, RoutedEventArgs e)
        => Vm?.BomMaster?.Create();

    private void OnCreatePriceLevelClick(object? sender, RoutedEventArgs e)
        => Vm?.PriceLevels?.Create();

    private void OnCreateReorderLevelClick(object? sender, RoutedEventArgs e)
        => Vm?.ReorderLevels?.Create();

    private void OnSavePriceListClick(object? sender, RoutedEventArgs e)
        => Vm?.PriceLists?.Save();

    private void OnAddPriceListSlabClick(object? sender, RoutedEventArgs e)
        => Vm?.PriceLists?.AddSlabRow();

    private void OnAddBomLineClick(object? sender, RoutedEventArgs e)
        => Vm?.BomMaster?.AddBlankLine();

    private void OnAcceptManufacturingJournalClick(object? sender, RoutedEventArgs e)
        => Vm?.ManufacturingJournalEntry?.Accept();

    private void OnCancelManufacturingJournalClick(object? sender, RoutedEventArgs e)
        => Vm?.CancelVoucher();

    private void OnAddManufacturingCostClick(object? sender, RoutedEventArgs e)
        => Vm?.ManufacturingJournalEntry?.AddBlankAdditionalCost();

    // POS Billing (Phase 6 slice 7; RQ-38..RQ-44) — accept / cancel / add line / toggle payment mode / tax analysis.
    private void OnAcceptPosClick(object? sender, RoutedEventArgs e)
        => Vm?.PosBilling?.Accept();

    private void OnCancelPosClick(object? sender, RoutedEventArgs e)
        => Vm?.CancelVoucher();

    private void OnAddPosItemLineClick(object? sender, RoutedEventArgs e)
        => Vm?.PosBilling?.AddItemLine();

    private void OnTogglePosPaymentModeClick(object? sender, RoutedEventArgs e)
        => Vm?.PosBilling?.TogglePaymentMode();

    private void OnShowPosTaxAnalysisClick(object? sender, RoutedEventArgs e)
        => Vm?.PosBilling?.ShowTaxAnalysis();

    // Job Work In/Out Order (Phase 6 slice 8; RQ-47) — accept / cancel / add component line.
    private void OnAcceptJobWorkOrderClick(object? sender, RoutedEventArgs e)
        => Vm?.JobWorkOrderEntry?.Accept();

    private void OnCancelJobWorkOrderClick(object? sender, RoutedEventArgs e)
        => Vm?.CancelVoucher();

    private void OnAddJobWorkLineClick(object? sender, RoutedEventArgs e)
        => Vm?.JobWorkOrderEntry?.AddBlankLine();

    // Material In/Out movement (Phase 6 slice 8; RQ-48) — accept / cancel / add source & destination lines.
    private void OnAcceptMaterialClick(object? sender, RoutedEventArgs e)
        => Vm?.MaterialMovementEntry?.Accept();

    private void OnCancelMaterialClick(object? sender, RoutedEventArgs e)
        => Vm?.CancelVoucher();

    private void OnAddMaterialSourceLineClick(object? sender, RoutedEventArgs e)
        => Vm?.MaterialMovementEntry?.AddSourceLine();

    private void OnAddMaterialDestinationLineClick(object? sender, RoutedEventArgs e)
        => Vm?.MaterialMovementEntry?.AddDestinationLine();

    /// <summary>Opens the batch-allocation sub-screen (RQ-3) for the inventory-voucher line the button sits on.</summary>
    private void OnOpenBatchAllocationClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ViewModels.InventoryVoucherLineViewModel line })
            Vm?.InventoryVoucherEntry?.RequestBatchAllocation(line);
    }

    private void OnApplyGstClick(object? sender, RoutedEventArgs e)
        => Vm?.GstConfig?.Apply();

    private void OnApplyReportConfigClick(object? sender, RoutedEventArgs e)
        => Vm?.ApplyReportConfig();

    private void OnApplyPrintConfigClick(object? sender, RoutedEventArgs e)
        => Vm?.ApplyPrintConfig();

    private void OnApplyExportClick(object? sender, RoutedEventArgs e)
        => Vm?.ApplyExport();

    private void OnApplyExportDataClick(object? sender, RoutedEventArgs e)
        => Vm?.ApplyExportData();

    private void OnApplyImportDataClick(object? sender, RoutedEventArgs e)
        => Vm?.ApplyImport();

    private void OnApplyReportSortFilterClick(object? sender, RoutedEventArgs e)
        => Vm?.ApplyReportSortFilter();

    private void OnClearReportSortFilterClick(object? sender, RoutedEventArgs e)
        => Vm?.ClearReportSortFilter();

    private void OnApplyAddComparisonColumnClick(object? sender, RoutedEventArgs e)
        => Vm?.ApplyAddComparisonColumn();

    private void OnApplyAutoColumnsClick(object? sender, RoutedEventArgs e)
        => Vm?.ApplyAutoColumns();

    private void OnClearComparativeClick(object? sender, RoutedEventArgs e)
        => Vm?.ClearComparative();

    private void OnApplySaveViewClick(object? sender, RoutedEventArgs e)
        => Vm?.ApplySaveView();

    private void OnOpenSavedViewClick(object? sender, RoutedEventArgs e)
        => Vm?.OpenSelectedSavedView();

    /// <summary>
    /// "Save PDF" on the Print-Preview panel: writes the rendered bytes to a file. The renderer is disk-free;
    /// this thin layer just picks a path (the user's Documents folder with a report-derived file name) and calls
    /// the VM, which writes the stream. A full save-file dialog can replace the path choice in a later slice.
    /// </summary>
    private void OnSavePrintPreviewClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) SavePrintPreviewToDocuments(vm);
    }

    /// <summary>Picks a Documents-folder path from the report title and asks the VM to write the rendered PDF bytes.</summary>
    private static void SavePrintPreviewToDocuments(MainWindowViewModel vm)
    {
        if (vm.PrintPreview is not { } preview) return;
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var name = SafeFileName(preview.ReportTitle) + ".pdf";
        vm.SavePrintPreview(Path.Combine(dir, name));
    }

    /// <summary>
    /// "Save .eml" on the E-Mail compose panel: writes the byte-stable message (with the exported-PDF attachment)
    /// to a Documents-folder path derived from the document title. The composer is disk-free; this thin layer just
    /// picks the path and calls the VM. A full save-file dialog can replace the path choice in a later slice.
    /// Nothing is sent — the .eml is handed to the OS mail client by the user.
    /// </summary>
    private static void SaveEmailToDocuments(MainWindowViewModel vm)
    {
        if (vm.EmailCompose is not { } compose) return;
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var name = SafeFileName(compose.DocumentTitle) + ".eml";
        vm.SaveEmail(Path.Combine(dir, name));
    }

    /// <summary>Turns a report title into a safe file-name stem (invalid path chars → '_'; blank → "Report").</summary>
    private static string SafeFileName(string title)
    {
        var stem = string.IsNullOrWhiteSpace(title) ? "Report" : title.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            stem = stem.Replace(c, '_');
        return stem;
    }

    private void OnDeleteSavedViewClick(object? sender, RoutedEventArgs e)
        => Vm?.DeleteSelectedSavedView();

    private void OnUnitSimpleClick(object? sender, RoutedEventArgs e)
    {
        if (Vm?.UnitMaster is { } m) m.IsCompound = false;
    }

    private void OnUnitCompoundClick(object? sender, RoutedEventArgs e)
    {
        if (Vm?.UnitMaster is { } m) m.IsCompound = true;
    }

    private void OnRecomputeForexClick(object? sender, RoutedEventArgs e)
        => Vm?.ForexReport?.Recompute();

    private void OnBookForexAdjustmentClick(object? sender, RoutedEventArgs e)
        => Vm?.ForexReport?.BookAdjustment();

    // ---------------------------------------------------------------- Stock-Summary drill → Stock Item Movement

    /// <summary>Double-click a Stock-Summary item row → open that item's Stock Item Movement report.</summary>
    private void OnStockSummaryDrill(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: ReportRow row })
            Vm?.DrillReport(row);
    }

    /// <summary>
    /// Enter on the highlighted Stock-Summary row drills into that item's Stock Item Movement report
    /// (keyboard-first). Handled here (and marked handled) so it does not bubble to the cascade driver.
    /// </summary>
    private void OnStockSummaryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (sender is ListBox { SelectedItem: ReportRow row } && row.CanDrill)
        {
            Vm?.DrillReport(row);
            e.Handled = true;
        }
    }

    // ---------------------------------------------------------------- RQ-7 accounting-report drill (TB/BS/P&L/Day Book)

    /// <summary>Double-click an accounting-report row → drill (TB/BS/P&amp;L ledger → its vouchers; Day Book → the voucher).</summary>
    private void OnAccountingReportDrill(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: ReportRow row })
            Vm?.DrillReport(row);
    }

    /// <summary>
    /// Enter on the highlighted accounting-report row drills into the report's per-kind target (keyboard-first).
    /// A no-op on a non-drillable row. Marked handled so it does not bubble to the cascade driver.
    /// </summary>
    private void OnAccountingReportKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (sender is ListBox { SelectedItem: ReportRow row } && row.CanDrill)
        {
            Vm?.DrillReport(row);
            e.Handled = true;
        }
    }
}
