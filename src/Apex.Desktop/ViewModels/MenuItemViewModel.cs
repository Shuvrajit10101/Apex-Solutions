using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// What a selectable menu row does when it becomes the highlighted item in a cascading column:
/// <list type="bullet">
/// <item><see cref="Group"/> — drilling in opens a <b>submenu column</b> to the right (its children).</item>
/// <item><see cref="Page"/> — drilling in opens a <b>page column</b> to the right (a report / voucher /
/// ledger / chart screen), leaving every menu column visible.</item>
/// <item><see cref="Action"/> — a plain action (e.g. change company) that does not add a column.</item>
/// </list>
/// </summary>
public enum MenuItemKind
{
    Action,
    Group,
    Page,
}

/// <summary>
/// One row in a cascading menu column (Gateway of Apex Solutions, company list, a submenu). A row is
/// either a non-selectable <b>section header</b> (dim, uppercase — e.g. MASTERS / TRANSACTIONS /
/// REPORTS) or a selectable <b>item</b> carrying the action to run on drill-in. Arrow-key navigation
/// skips headers; Enter/Right only ever fires on items. Selectable items carry a display label, an
/// optional right-aligned hint (shortcut/detail), a <see cref="Kind"/> that tells the cascade whether
/// drilling in opens a submenu column, a page column, or just runs an action, the action itself, and
/// an <see cref="IsSelected"/> flag the highlight binds to. <see cref="IsSubItem"/> indents an item one
/// level (e.g. a submenu child). <see cref="IsActiveColumn"/> is set by the shell on the rows of the
/// currently focused column so only that column shows the bright amber highlight — an earlier column's
/// chosen row renders in a dimmer "selected-but-inactive" style.
/// </summary>
public sealed partial class MenuItemViewModel : ViewModelBase
{
    private static void NoOp() { }

    [ObservableProperty] private bool _isSelected;

    /// <summary>
    /// True when this row's column is the currently focused (active) column. Only the active column
    /// paints the bright amber highlight; an earlier column's selected row paints the dim "inactive"
    /// highlight. Set by the shell as the cascade grows/shrinks.
    /// </summary>
    [ObservableProperty] private bool _isActiveColumn = true;

    public string Label { get; }
    public string Hint { get; }
    public Action Activate { get; }

    /// <summary>What drilling into this item does (open submenu column / page column / run an action).</summary>
    public MenuItemKind Kind { get; }

    /// <summary>True for a non-selectable section header (rendered dim/uppercase, skipped by arrows).</summary>
    public bool IsHeader { get; }

    /// <summary>True for a selectable item (the complement of <see cref="IsHeader"/>).</summary>
    public bool IsSelectable => !IsHeader;

    /// <summary>True when drilling into this item opens a submenu column (a group of children).</summary>
    public bool IsGroup => Kind == MenuItemKind.Group;

    /// <summary>True when drilling into this item opens a page column (a report/voucher/ledger/chart).</summary>
    public bool IsPage => Kind == MenuItemKind.Page;

    /// <summary>True to indent this item one level under its section header.</summary>
    public bool IsSubItem { get; }

    public MenuItemViewModel(
        string label,
        Action activate,
        string hint = "",
        bool isSubItem = false,
        MenuItemKind kind = MenuItemKind.Action)
    {
        Label = label;
        Activate = activate ?? throw new ArgumentNullException(nameof(activate));
        Hint = hint;
        IsHeader = false;
        IsSubItem = isSubItem;
        Kind = kind;
    }

    private MenuItemViewModel(string label)
    {
        Label = label;
        Activate = NoOp;
        Hint = string.Empty;
        IsHeader = true;
        IsSubItem = false;
        Kind = MenuItemKind.Action;
    }

    /// <summary>Builds a non-selectable section header (rendered dim/uppercase; arrows skip it).</summary>
    public static MenuItemViewModel Header(string label) => new(label);
}
