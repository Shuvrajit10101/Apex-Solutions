using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// One row in a menu (Gateway of Apex Solutions, company list, report list). A row is either a
/// non-selectable <b>section header</b> (dim, uppercase — e.g. MASTERS / TRANSACTIONS / REPORTS)
/// or a selectable <b>item</b> carrying the action to run on Enter. Arrow-key navigation skips
/// headers; Enter only ever fires on items. Selectable items carry a display label, an optional
/// right-aligned hint (shortcut/detail), the action, and a <see cref="IsSelected"/> flag the amber
/// highlight binds to. <see cref="IsSubItem"/> indents an item one level (e.g. a submenu child).
/// </summary>
public sealed partial class MenuItemViewModel : ViewModelBase
{
    private static void NoOp() { }

    [ObservableProperty] private bool _isSelected;

    public string Label { get; }
    public string Hint { get; }
    public Action Activate { get; }

    /// <summary>True for a non-selectable section header (rendered dim/uppercase, skipped by arrows).</summary>
    public bool IsHeader { get; }

    /// <summary>True for a selectable item (the complement of <see cref="IsHeader"/>).</summary>
    public bool IsSelectable => !IsHeader;

    /// <summary>True to indent this item one level under its section header.</summary>
    public bool IsSubItem { get; }

    public MenuItemViewModel(string label, Action activate, string hint = "", bool isSubItem = false)
    {
        Label = label;
        Activate = activate ?? throw new ArgumentNullException(nameof(activate));
        Hint = hint;
        IsHeader = false;
        IsSubItem = isSubItem;
    }

    private MenuItemViewModel(string label)
    {
        Label = label;
        Activate = NoOp;
        Hint = string.Empty;
        IsHeader = true;
        IsSubItem = false;
    }

    /// <summary>Builds a non-selectable section header (rendered dim/uppercase; arrows skip it).</summary>
    public static MenuItemViewModel Header(string label) => new(label);
}
