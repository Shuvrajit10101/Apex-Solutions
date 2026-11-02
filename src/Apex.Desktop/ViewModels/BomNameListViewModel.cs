using System;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>One existing BOM name on the <see cref="BomNameListViewModel"/> (M BOM) screen.</summary>
public sealed partial class BomNameRow : ObservableObject
{
    /// <summary>The BOM this row opens for alteration.</summary>
    public Guid BomId { get; init; }

    /// <summary>The BOM's name, e.g. "Standard".</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>A one-line summary of the BOM's components, so two names can be told apart before opening.</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>The keyboard highlight (arrows move it, Enter opens the highlighted BOM).</summary>
    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>What Enter on the M BOM screen resolved to: alter an existing BOM, or create a new one under a name.</summary>
public abstract record BomNameChoice
{
    /// <summary>Open the components screen over this EXISTING BOM, for an in-place alteration.</summary>
    public sealed record AlterExisting(Guid BomId) : BomNameChoice;

    /// <summary>Open the components screen to CREATE a new BOM of the item under this name.</summary>
    public sealed record CreateNew(string Name) : BomNameChoice;
}

/// <summary>
/// The <b>Stock Item Multi Components (M BOM)</b> screen — census 3.9. It sits between the
/// <i>Alter Components (BOM)</i> option on Stock Item Alteration and the components screen, and lists the item's
/// BOM names so that EVERY BOM of the item — not only the first — can be reached, and a further BOM can be added.
///
/// <para><b>FIDELITY (R7, ruling 14).</b> help.tallysolutions.com/tally-prime/inventory-in-manufacturing/manage-inventory-in-manufacturing-tally/,
/// opened BY CONTENT 2026-10-26: <i>"In the Stock Item Alteration screen, enable the option Alter Components
/// (BOM) to add a new BOM"</i>, then <i>"In the Stock Item Multi Components (M BOM) screen, enter the name of the
/// new BOM and press Enter."</i>, and on creation <i>"If you want to add one more BOM for this item, enter the new
/// BOM name under the Standard BoM and press Enter."</i> So the screen shows the existing names with the new name
/// typed beneath them, and Enter moves on to the components screen.</para>
///
/// <para><b>OURS (IV-225).</b> The page documents only ADDING a name on this screen. Opening an EXISTING name's
/// components for an in-place alteration (arrows + Enter on a listed name, or typing an existing name) is ours:
/// the page is silent on it, and without it an item's second and later BOMs could not be corrected anywhere.</para>
///
/// <para>MVVM boundary: domain only, no Avalonia types; the shell turns a <see cref="BomNameChoice"/> into the
/// components screen.</para>
/// </summary>
public sealed partial class BomNameListViewModel : ViewModelBase
{
    private readonly Company _company;

    /// <summary>The finished-good item whose BOMs are listed.</summary>
    public Guid StockItemId { get; }

    /// <summary>The item's name, shown in the caption.</summary>
    public string ItemName { get; }

    /// <summary>The page caption.</summary>
    public string Caption => "Stock Item Multi Components (M BOM)";

    /// <summary>The item's existing BOMs, in the book's stored order.</summary>
    public ObservableCollection<BomNameRow> Rows { get; } = new();

    /// <summary>The new BOM name typed beneath the existing ones. When non-blank it wins over the highlight.</summary>
    [ObservableProperty] private string _newName = string.Empty;

    /// <summary>The refusal or hint shown on the screen, or null.</summary>
    [ObservableProperty] private string? _message;

    /// <summary>True when the item has no BOM yet — only a new name can be entered.</summary>
    public bool HasNoBoms => Rows.Count == 0;

    public BomNameListViewModel(Company company, Guid stockItemId)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        var item = company.FindStockItem(stockItemId)
            ?? throw new ArgumentException($"Stock item {stockItemId} not found.", nameof(stockItemId));
        StockItemId = item.Id;
        ItemName = item.Name;
        Fill(highlightId: null);
    }

    private void Fill(Guid? highlightId)
    {
        Rows.Clear();
        foreach (var bom in _company.BomsFor(StockItemId))
        {
            var parts = bom.ComponentLines
                .Select(l => _company.FindStockItem(l.ComponentStockItemId)?.Name ?? "?")
                .ToList();
            Rows.Add(new BomNameRow
            {
                BomId = bom.Id,
                Name = bom.Name,
                Detail = parts.Count > 0 ? string.Join(", ", parts) : "—",
            });
        }
        if (Rows.Count > 0) (Rows.FirstOrDefault(r => r.BomId == highlightId) ?? Rows[0]).IsHighlighted = true;
        OnPropertyChanged(nameof(HighlightedRow));
        OnPropertyChanged(nameof(HasNoBoms));
    }

    /// <summary>
    /// Census 3.9 (IV-227) — the shell calls this when Escape returns from the components screen to this list. The
    /// names are re-read from the book, so a BOM just created is listed, a rename shows, and the highlight stays on
    /// the BOM it was on (by id). The typed new name is cleared: it has been used, and leaving it would make the
    /// next Enter re-open that BOM rather than the highlighted one.
    /// </summary>
    public void Reload()
    {
        var keep = HighlightedRow?.BomId;
        var typed = (NewName ?? string.Empty).Trim();
        if (typed.Length > 0 && _company.FindBomByName(StockItemId, typed) is { } made) keep = made.Id;
        NewName = string.Empty;
        Message = null;
        Fill(keep);
    }

    /// <summary>The highlighted existing BOM, or null when the item has none.</summary>
    public BomNameRow? HighlightedRow => Rows.FirstOrDefault(r => r.IsHighlighted);

    /// <summary>Moves the highlight up (-1) or down (+1) through the existing names; wraps.</summary>
    public void MoveHighlight(int direction)
    {
        if (Rows.Count == 0) return;
        var at = Rows.IndexOf(HighlightedRow!);
        if (at < 0) at = 0;
        var next = ((at + Math.Sign(direction)) % Rows.Count + Rows.Count) % Rows.Count;
        foreach (var r in Rows) r.IsHighlighted = false;
        Rows[next].IsHighlighted = true;
        OnPropertyChanged(nameof(HighlightedRow));
    }

    /// <summary>
    /// Enter: a typed name wins — an existing name of THIS item (case-insensitive, the uniqueness rule the
    /// schema enforces) opens that BOM for alteration rather than refusing as a duplicate, any other name
    /// creates a new BOM under it. With nothing typed, the highlighted BOM opens for alteration. Returns null
    /// (with <see cref="Message"/> set) when there is nothing to open.
    /// </summary>
    public BomNameChoice? Choose()
    {
        Message = null;
        var typed = (NewName ?? string.Empty).Trim();
        if (typed.Length > 0)
        {
            return _company.FindBomByName(StockItemId, typed) is { } existing
                ? new BomNameChoice.AlterExisting(existing.Id)
                : new BomNameChoice.CreateNew(typed);
        }
        if (HighlightedRow is { } row) return new BomNameChoice.AlterExisting(row.BomId);

        Message = "Enter the name of the new BOM.";
        return null;
    }
}
