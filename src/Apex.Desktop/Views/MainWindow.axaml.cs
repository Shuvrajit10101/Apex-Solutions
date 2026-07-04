using System.Collections.Specialized;
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

        // Ctrl+A saves/accepts (accept shortcut) — create company, accept voucher, or create ledger.
        if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            vm.ActivateSelected();
            e.Handled = true;
            return;
        }

        // Alt+X cancels the in-progress voucher/ledger without saving (cancel shortcut).
        if (e.Key == Key.X && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            vm.CancelVoucher();
            e.Handled = true;
            return;
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
                case Key.F9: vm.OpenInventoryVoucher(Apex.Ledger.Domain.VoucherBaseType.PurchaseOrder); e.Handled = true; return;
                case Key.F8: vm.OpenInventoryVoucher(Apex.Ledger.Domain.VoucherBaseType.SalesOrder); e.Handled = true; return;
                case Key.F6: vm.OpenInventoryVoucher(Apex.Ledger.Domain.VoucherBaseType.RejectionIn); e.Handled = true; return;
                case Key.F5: vm.OpenInventoryVoucher(Apex.Ledger.Domain.VoucherBaseType.RejectionOut); e.Handled = true; return;
            }
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            switch (e.Key)
            {
                case Key.F9: vm.OpenInventoryVoucher(Apex.Ledger.Domain.VoucherBaseType.ReceiptNote); e.Handled = true; return;
                case Key.F8: vm.OpenInventoryVoucher(Apex.Ledger.Domain.VoucherBaseType.DeliveryNote); e.Handled = true; return;
                case Key.F7: vm.OpenInventoryVoucher(Apex.Ledger.Domain.VoucherBaseType.StockJournal); e.Handled = true; return;
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
}
