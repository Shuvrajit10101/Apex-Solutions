using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Handle keys at the tunnelling stage so arrow/Enter/Esc work regardless of focus.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm is null) return;

        // Ctrl+A saves/accepts (Tally accept shortcut) — on the create screen, create the company.
        if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            vm.ActivateSelected();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
                vm.MoveUp();
                e.Handled = true;
                break;
            case Key.Down:
                vm.MoveDown();
                e.Handled = true;
                break;
            case Key.Enter:
                vm.ActivateSelected();
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

            // Report quick letters — only when not typing in the name box.
            case Key.B when !IsTyping(e): Fire(vm, "B"); e.Handled = true; break;
            case Key.P when !IsTyping(e): Fire(vm, "P"); e.Handled = true; break;
            case Key.T when !IsTyping(e): Fire(vm, "T"); e.Handled = true; break;
            case Key.D when !IsTyping(e): Fire(vm, "D"); e.Handled = true; break;
        }
    }

    private static bool IsTyping(KeyEventArgs e) => e.Source is TextBox;

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
}
