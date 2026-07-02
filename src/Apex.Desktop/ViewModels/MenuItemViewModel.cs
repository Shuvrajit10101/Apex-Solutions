using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// One selectable line in a Tally menu (Gateway of Tally, company list, report list).
/// Carries the display label, an optional right-aligned hint (shortcut/detail), the action to
/// run on Enter, and a <see cref="IsSelected"/> flag the amber highlight binds to.
/// </summary>
public sealed partial class MenuItemViewModel : ViewModelBase
{
    [ObservableProperty] private bool _isSelected;

    public string Label { get; }
    public string Hint { get; }
    public Action Activate { get; }

    public MenuItemViewModel(string label, Action activate, string hint = "")
    {
        Label = label;
        Activate = activate ?? throw new ArgumentNullException(nameof(activate));
        Hint = hint;
    }
}
