using Avalonia;
using Avalonia.Controls;

namespace Apex.Desktop.Views;

/// <summary>
/// The single reusable empty-state control (UI-defect C7). Drop it into a report or master body,
/// span it across the whole body, and bind its <see cref="IsVisible"/> to the emptiness of the backing
/// collection (e.g. <c>Rows.Count == 0</c>). It replaces the ad-hoc empty-state rows that earlier screens
/// improvised by writing a message into a single (often starved) grid column.
/// </summary>
public partial class EmptyState : UserControl
{
    /// <summary>The one-line message shown when the screen has no data (e.g. "No entries in this period.").</summary>
    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(Message), "No data to display.");

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public EmptyState() => InitializeComponent();
}
