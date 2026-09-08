using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A stock-group row for the existing-groups list on the master screen.</summary>
public sealed partial class StockGroupListRow : ObservableObject
{
    public string Name { get; init; } = string.Empty;
    public string Under { get; init; } = string.Empty;
    public string Quantities { get; init; } = string.Empty;

    /// <summary>
    /// The <b>stable identity</b> of the stock group this row displays. Before census 3.13 the list was ID-less,
    /// which is the same gap that once left <c>StockItemMasterViewModel.ForAlter</c> with no production caller:
    /// the operator could SEE every group but no row resolved back to the master it displayed, so there was
    /// nothing for a drill key to open — and the Stock Group master had no Alter verb at all.
    /// </summary>
    public Guid StockGroupId { get; init; }

    /// <summary>The GST rung this group declares, or "—". Shown so an operator can see AT A GLANCE which groups
    /// carry a block the rate walk will stop at, rather than having to open each one.</summary>
    public string Gst { get; init; } = "—";

    /// <summary>True while this row carries the keyboard highlight (Up/Down move it, Ctrl+Enter alters it).</summary>
    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>
/// One entry in the "Under" parent picker for a stock group: "Primary" (top-level, no parent) or any
/// existing stock group. <see cref="Group"/> is null for the Primary option.
/// </summary>
public sealed class ParentStockGroupOption
{
    public StockGroup? Group { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsPrimary => Group is null;
}

/// <summary>
/// The Stock-Group creation master ("Masters → Create → Inventory Masters → Stock Group", catalog §9;
/// RQ-1): a name, an optional alias, an optional <b>Under</b> parent (Primary ⇒ top-level, or nest under an
/// existing group), and the <b>"Should quantities be added?"</b> flag (default yes). Creates the group via
/// the <see cref="InventoryService"/> (which enforces unique name + valid, non-cyclic parent) and persists.
///
/// <para>MVVM boundary: references the domain + persistence but no Avalonia/UI types, so it is headlessly
/// unit-testable. Mirrors <see cref="CostCentreMasterViewModel"/>.</para>
/// </summary>
public sealed partial class StockGroupMasterViewModel : ViewModelBase, IMasterListExportSource
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Stock Groups",
        new[]
        {
            MasterListColumn.Text("Name"), MasterListColumn.Text("Under"),
            MasterListColumn.Text("Quantities"), MasterListColumn.Text("GST"),
        },
        Existing.Select(r => (IReadOnlyList<string>)new[] { r.Name, r.Under, r.Quantities, r.Gst }).ToList());

    /// <summary>The parent options: "Primary" plus every existing stock group.</summary>
    public ObservableCollection<ParentStockGroupOption> ParentOptions { get; } = new();

    /// <summary>The existing stock groups, refreshed after each create.</summary>
    public ObservableCollection<StockGroupListRow> Existing { get; } = new();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _alias = string.Empty;
    [ObservableProperty] private ParentStockGroupOption? _selectedParent;
    [ObservableProperty] private bool _addQuantities = true;
    [ObservableProperty] private string? _message;

    /// <summary>
    /// Census 3.13 — the Stock Group's <i>Set/Alter GST Details</i> block: the rung
    /// <c>MasterAncestry.NearestStockGroupGst</c> already walks and which, until now, only the canonical importer
    /// could write. The vendor's own Stock Group Creation step is <i>"Set/Alter GST Details: 'Yes' for setting
    /// fixed GST Rate, which will be applicable for all items under this group"</i>.
    /// </summary>
    public MasterGstBlockEditor Gst { get; } = new();

    // --------------------------------------------------------------- alteration state (census 3.13)

    /// <summary>The id of the stock group being ALTERED, or <see cref="Guid.Empty"/> in Create mode.</summary>
    private Guid _editingId = Guid.Empty;

    /// <summary>True iff this screen is altering an existing stock group rather than creating one.</summary>
    public bool IsAltering => _editingId != Guid.Empty;

    /// <summary>The screen heading. It says which VERB is running, because the form is identical in both modes
    /// and a heading reading "Creation" over an alteration is how an operator mistakes one for the other.</summary>
    public string Caption => IsAltering ? "Stock Group Alteration" : "Stock Group Creation";

    /// <summary>
    /// Opens this master in <b>Alter</b> mode over an existing stock group — the same form, pre-filled. Returns
    /// <c>null</c> if the id does not resolve. Mirrors <see cref="AccountGroupMasterViewModel.ForAlter"/>.
    /// </summary>
    public static StockGroupMasterViewModel? ForAlter(
        Company company, CompanyStorage storage, Guid groupId, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (company.FindStockGroup(groupId) is not { } group) return null;

        var vm = new StockGroupMasterViewModel(company, storage, onChanged);
        vm._editingId = groupId;
        vm.LoadFrom(group);
        vm.OnPropertyChanged(nameof(IsAltering));
        vm.OnPropertyChanged(nameof(Caption));
        return vm;
    }

    /// <summary>Loads an existing stock group's values into the form — every field the screen offers, so an Alter
    /// that changes one thing writes the rest back unchanged instead of resetting them to the form defaults.</summary>
    public void LoadFrom(StockGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        Name = group.Name;
        Alias = group.Alias ?? string.Empty;
        AddQuantities = group.AddQuantities;
        SelectedParent = ParentOptions.FirstOrDefault(o => o.Group?.Id == group.ParentId)
            ?? ParentOptions.FirstOrDefault(o => o.IsPrimary);
        Gst.LoadFrom(group.Gst);
    }

    /// <summary>
    /// Ctrl+A <b>alter</b>: renames / re-aliases / re-parents the stock group this screen was opened over and
    /// rewrites its GST block, via <see cref="InventoryService.AlterStockGroup"/> (unique name excluding itself,
    /// existing and non-cyclic parent).
    /// </summary>
    public bool Alter()
    {
        Message = null;
        if (_editingId == Guid.Empty)
        {
            Message = "This screen is not altering an existing stock group.";
            return false;
        }
        if (!Gst.TryBuild(out var gstBlock, out var gstError))
        {
            Message = gstError;
            return false;
        }

        var alias = string.IsNullOrWhiteSpace(Alias) ? null : Alias.Trim();
        try
        {
            var service = new InventoryService(_company);
            var altered = service.AlterStockGroup(
                _editingId, Name, SelectedParent?.Group?.Id, alias, AddQuantities);
            // The form owns the block outright, so an alter that switches GST off clears it.
            altered.Gst = gstBlock;
            _storage.Save(_company);
            var underLabel = SelectedParent is { IsPrimary: false } p ? p.Group!.Name : "Primary";
            Message = $"Stock group '{altered.Name}' altered — under {underLabel}.";
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
            return false;
        }

        RefreshParentOptions();
        RefreshList();
        _onChanged();
        return true;
    }

    public StockGroupMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        RefreshParentOptions();
        RefreshList();
    }

    /// <summary>
    /// Ctrl+A create: validates the name is non-empty, then creates the stock group under the chosen parent
    /// (Primary ⇒ top-level) via the engine and persists. The engine also enforces uniqueness + a valid,
    /// non-cyclic parent; any domain error is surfaced to <see cref="Message"/> without crashing the UI.
    /// </summary>
    public bool Create()
    {
        Message = null;
        var name = (Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "A stock group name is required.";
            return false;
        }

        var parentId = SelectedParent?.Group?.Id;
        var alias = string.IsNullOrWhiteSpace(Alias) ? null : Alias.Trim();

        if (!Gst.TryBuild(out var gstBlock, out var gstError))
        {
            Message = gstError;
            return false;
        }

        try
        {
            var service = new InventoryService(_company);
            var created = service.CreateStockGroup(name, parentId, alias, AddQuantities);
            created.Gst = gstBlock;   // census 3.13
            _storage.Save(_company);
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
            return false;
        }

        var underLabel = SelectedParent is { IsPrimary: false } p ? p.Group!.Name : "Primary";
        RefreshParentOptions();
        RefreshList();
        Message = $"Stock group '{name}' created under {underLabel}.";
        Name = string.Empty;
        Alias = string.Empty;
        AddQuantities = true;
        Gst.LoadFrom(null);
        _onChanged();
        return true;
    }

    private void RefreshParentOptions()
    {
        var previousId = SelectedParent?.Group?.Id;
        ParentOptions.Clear();
        ParentOptions.Add(new ParentStockGroupOption { Group = null, Display = "◦ Primary (top-level)" });
        foreach (var g in _company.StockGroups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            ParentOptions.Add(new ParentStockGroupOption { Group = g, Display = g.Name });

        SelectedParent = ParentOptions.FirstOrDefault(o => o.Group?.Id == previousId)
                         ?? ParentOptions.FirstOrDefault();
    }

    private void RefreshList()
    {
        // Keep the highlight on the SAME GROUP across a rebuild (a save re-renders the list): re-finding it by id
        // rather than by index means an alter that renames or re-parents a group does not silently move the
        // highlight onto a neighbouring master that the next Ctrl+Enter would then open.
        var previouslyHighlighted = HighlightedRow?.StockGroupId;

        Existing.Clear();
        foreach (var g in _company.StockGroups)
        {
            var under = g.ParentId is { } pid
                ? _company.FindStockGroup(pid)?.Name ?? "—"
                : "Primary";
            Existing.Add(new StockGroupListRow
            {
                StockGroupId = g.Id,
                Name = g.Name,
                Under = under,
                Quantities = g.AddQuantities ? "Added" : "Not added",
                Gst = DescribeGst(g.Gst),
            });
        }

        var restored = previouslyHighlighted is { } id
            ? Existing.ToList().FindIndex(r => r.StockGroupId == id)
            : -1;
        SetHighlight(restored);
    }

    /// <summary>The one-line summary of a group's GST rung for the list column — invariant-culture so the gate's
    /// ubuntu and macos legs render the same string this build's Windows leg does.</summary>
    private static string DescribeGst(MasterGstDetails? gst)
    {
        if (gst is null) return "—";
        if (gst.RateBasisPoints is not { } bp) return gst.Taxability.ToString();
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{gst.Taxability} {(bp / 100m).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}%");
    }

    // --------------------------------------------------------------- keyboard selection + drill to Alter

    /// <summary>The index of the highlighted existing-group row, or -1 when nothing is highlighted.</summary>
    [ObservableProperty] private int _highlightedIndex = -1;

    /// <summary>The highlighted existing-group row, or <c>null</c>. Ctrl+Enter on it opens Stock Group
    /// Alteration — the same chord, on the same kind of surface, as the Stock Item master's list.</summary>
    public StockGroupListRow? HighlightedRow =>
        HighlightedIndex >= 0 && HighlightedIndex < Existing.Count ? Existing[HighlightedIndex] : null;

    /// <summary>
    /// Moves the existing-groups highlight by <paramref name="direction"/> (Up/Down), wrapping. The FIRST press on
    /// an untouched screen lands on row 0 rather than jumping to the end, so Down reads as "enter the list".
    /// A no-op when no groups exist.
    /// </summary>
    public void MoveHighlight(int direction)
    {
        if (Existing.Count == 0) { SetHighlight(-1); return; }
        if (HighlightedIndex < 0) { SetHighlight(direction >= 0 ? 0 : Existing.Count - 1); return; }

        var next = (HighlightedIndex + direction + Existing.Count) % Existing.Count;
        SetHighlight(next);
    }

    private void SetHighlight(int index)
    {
        for (var i = 0; i < Existing.Count; i++) Existing[i].IsHighlighted = i == index;
        HighlightedIndex = index >= 0 && index < Existing.Count ? index : -1;
        OnPropertyChanged(nameof(HighlightedRow));
    }
}
