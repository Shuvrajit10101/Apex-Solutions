using System;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>One option on the <see cref="CopyBomDetailsViewModel"/> list panel (a stock item, or one of its BOMs).</summary>
public sealed partial class CopyBomOption : ObservableObject
{
    /// <summary>The stock item id (Stock Item field) or the BOM id (Component of BOM field).</summary>
    public Guid Id { get; init; }

    /// <summary>The name shown on the list panel.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The keyboard highlight.</summary>
    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>
/// The <b>Copy BoM Details</b> sub-screen — census 3.9, Ctrl+C on the Stock Item Components (BOM) screen.
///
/// <para><b>FIDELITY (R7, ruling 14).</b> help.tallysolutions.com/tally-prime/inventory-in-manufacturing/manage-inventory-in-manufacturing-tally/,
/// opened BY CONTENT 2026-10-29: <i>"In the Stock Item Components (BOM) screen, press Ctrl+C (Copy BoM)."</i>, then
/// <i>"In the Copy BoM Details screen, select the stock item and component of BoM."</i> The two fields are described
/// on the same page: <i>"Stock Item: Select the Stock Item from which the BOM is to be copied, You can select a
/// different stock item from the list of Items, in which BOM is defined."</i> and <i>"Component of BOM: Select the
/// BOM from which the components are to be copied for the selected Item."</i> The page's screenshot
/// (<c>copy-of-bom-selecting-another-bom-tally.gif</c>) shows the panel captioned <i>Copy From</i> with the Stock
/// Item pre-set to the item being edited and a <i>List of BoM</i> beside it.</para>
///
/// <para><b>Keyboard shape (ours, IV-266).</b> Two fields, walked as a TallyPrime form is: Up/Down change the
/// ACTIVE field's value through the list panel, Enter on Stock Item moves to Component of BOM, Enter on Component
/// of BOM copies and returns to the components screen. Escape leaves and copies nothing.</para>
///
/// <para>MVVM boundary: domain only; the shell applies the chosen BOM to the components screen.</para>
/// </summary>
public sealed partial class CopyBomDetailsViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly Guid _excludeBomId;

    /// <summary>The column title.</summary>
    public string Title => "Copy BoM Details";

    /// <summary>Items that have at least one BOM which can be copied (the vendor's "list of Items, in which BOM is
    /// defined"), in name order. The BOM being altered is not a source for itself.</summary>
    public ObservableCollection<CopyBomOption> Items { get; } = new();

    /// <summary>The selected item's BOMs (the vendor's "List of BoM"), in stored order.</summary>
    public ObservableCollection<CopyBomOption> Boms { get; } = new();

    /// <summary>0 = the Stock Item field is active, 1 = the Component of BOM field is active.</summary>
    [ObservableProperty] private int _activeField;

    /// <summary>The refusal or hint shown on the screen, or null.</summary>
    [ObservableProperty] private string? _message;

    /// <param name="company">The open company.</param>
    /// <param name="excludeBomId">The BOM being altered (not offered as its own source), or <see cref="Guid.Empty"/>.</param>
    /// <param name="preferredItemId">The item the components screen is for; pre-selected when it has a source BOM.</param>
    public CopyBomDetailsViewModel(Company company, Guid excludeBomId, Guid? preferredItemId)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _excludeBomId = excludeBomId;

        foreach (var item in company.StockItems.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!SourcesOf(item.Id).Any()) continue;
            Items.Add(new CopyBomOption { Id = item.Id, Name = item.Name });
        }

        var start = Items.FirstOrDefault(i => i.Id == preferredItemId) ?? Items.FirstOrDefault();
        if (start is not null) start.IsHighlighted = true;
        RebuildBoms();
        if (Items.Count == 0) Message = "No other BOM is defined to copy from.";
    }

    private System.Collections.Generic.IEnumerable<BillOfMaterials> SourcesOf(Guid itemId) =>
        _company.BomsFor(itemId).Where(b => b.Id != _excludeBomId);

    /// <summary>The highlighted stock item, or null when nothing can be copied.</summary>
    public CopyBomOption? SelectedItem => Items.FirstOrDefault(i => i.IsHighlighted);

    /// <summary>The highlighted BOM of <see cref="SelectedItem"/>, or null.</summary>
    public CopyBomOption? SelectedBom => Boms.FirstOrDefault(b => b.IsHighlighted);

    /// <summary>The selected item's name, shown in the Stock Item field.</summary>
    public string SelectedItemName => SelectedItem?.Name ?? string.Empty;

    /// <summary>The selected BOM's name, shown in the Component of BOM field.</summary>
    public string SelectedBomName => SelectedBom?.Name ?? string.Empty;

    /// <summary>True while the Stock Item field is the active one (its list shows on the panel).</summary>
    public bool IsItemFieldActive => ActiveField == 0;

    /// <summary>True while the Component of BOM field is the active one.</summary>
    public bool IsBomFieldActive => ActiveField == 1;

    /// <summary>The list panel caption for the active field.</summary>
    public string ListCaption => IsItemFieldActive ? "List of Stock Items" : "List of BoM";

    /// <summary>The options the list panel shows for the active field.</summary>
    public ObservableCollection<CopyBomOption> ActiveOptions => IsItemFieldActive ? Items : Boms;

    partial void OnActiveFieldChanged(int value)
    {
        OnPropertyChanged(nameof(IsItemFieldActive));
        OnPropertyChanged(nameof(IsBomFieldActive));
        OnPropertyChanged(nameof(ListCaption));
        OnPropertyChanged(nameof(ActiveOptions));
    }

    private void RebuildBoms()
    {
        Boms.Clear();
        if (SelectedItem is { } item)
            foreach (var bom in SourcesOf(item.Id))
                Boms.Add(new CopyBomOption { Id = bom.Id, Name = bom.Name });
        if (Boms.Count > 0) Boms[0].IsHighlighted = true;
        OnPropertyChanged(nameof(SelectedItem));
        OnPropertyChanged(nameof(SelectedItemName));
        OnPropertyChanged(nameof(SelectedBom));
        OnPropertyChanged(nameof(SelectedBomName));
    }

    /// <summary>Up (-1) / Down (+1): moves the active field's value through its list; wraps. Changing the item
    /// re-lists its BOMs and selects the first.</summary>
    public void Move(int direction)
    {
        var list = IsItemFieldActive ? Items : Boms;
        if (list.Count == 0) return;
        var at = list.IndexOf(list.FirstOrDefault(o => o.IsHighlighted)!);
        if (at < 0) at = 0;
        var next = ((at + Math.Sign(direction)) % list.Count + list.Count) % list.Count;
        foreach (var o in list) o.IsHighlighted = false;
        list[next].IsHighlighted = true;
        if (IsItemFieldActive) RebuildBoms();
        else
        {
            OnPropertyChanged(nameof(SelectedBom));
            OnPropertyChanged(nameof(SelectedBomName));
        }
    }

    /// <summary>
    /// Enter: on the Stock Item field, moves on to Component of BOM and returns null; on Component of BOM, returns
    /// the BOM to copy from. Returns null with <see cref="Message"/> set when there is nothing to copy.
    /// </summary>
    public BillOfMaterials? Accept()
    {
        Message = null;
        if (Items.Count == 0)
        {
            Message = "No other BOM is defined to copy from.";
            return null;
        }
        if (IsItemFieldActive)
        {
            ActiveField = 1;
            return null;
        }
        if (SelectedBom is not { } chosen || _company.FindBillOfMaterials(chosen.Id) is not { } bom)
        {
            Message = "Select the BOM to copy from.";
            return null;
        }
        return bom;
    }

    /// <summary>Ctrl+A: accepts the screen from either field — the BOM currently shown is the one copied.</summary>
    public BillOfMaterials? AcceptAsIs()
    {
        ActiveField = 1;
        return Accept();
    }
}
