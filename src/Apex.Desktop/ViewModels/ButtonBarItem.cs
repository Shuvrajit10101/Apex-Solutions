using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// One entry in the right-hand vertical button bar: a function-key label, a caption, the action
/// it fires, and whether it is currently enabled (dimmed when not wired for the current screen).
/// </summary>
public sealed class ButtonBarItem
{
    public string Key { get; }
    public string Caption { get; }
    public Action Action { get; }
    public bool Enabled { get; }

    /// <summary>Command the button binds to (wraps <see cref="Action"/>).</summary>
    public ICommand Invoke { get; }

    public ButtonBarItem(string key, string caption, Action action, bool enabled = true)
    {
        Key = key;
        Caption = caption;
        Action = action ?? throw new ArgumentNullException(nameof(action));
        Enabled = enabled;
        Invoke = new RelayCommand(action);
    }
}
