using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A price-level row for the existing-levels list on the master screen.
///
/// <para>W33 C3 (census 3.10) — carries <see cref="IMasterListRow"/> so the ONE shared
/// <see cref="IMasterListScreen"/> arm can walk it with the arrows and delete it with Alt+D.</para>
/// </summary>
public sealed partial class PriceLevelListRow : ObservableObject, IMasterListRow
{
    public string Name { get; init; } = string.Empty;

    /// <summary>How many dated price-list versions reference this level (for the operator's context).</summary>
    public string Lists { get; init; } = string.Empty;

    /// <inheritdoc/>
    public Guid MasterId { get; init; }

    /// <inheritdoc/>
    public string MasterName => Name;

    /// <inheritdoc/>
    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>
/// The <b>Price Level</b> creation master ("Masters → Create → Inventory Masters → Price Level"; Phase 6 slice 5;
/// RQ-26; Book p.33): a bare named per-company master (Wholesale, Retail…). Creates the level via the
/// <see cref="PriceListService"/> (non-blank, unique-per-company case-insensitive) and persists.
///
/// <para>Gated by <see cref="Company.EnableMultiplePriceLevels"/> — the screen is only reachable while the F11
/// flag is on (RQ-52), so a non-price-level company never sees it (ER-13). Mirrors
/// <see cref="StockCategoryMasterViewModel"/>.</para>
///
/// <para>MVVM boundary: references the domain + persistence but no Avalonia/UI types, so it is headlessly
/// unit-testable.</para>
/// </summary>
public sealed partial class PriceLevelsViewModel : ViewModelBase, IMasterListExportSource, IMasterListScreen
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    // ------------------------------------------------- W33 C3 (census 3.10): the shared master-list arm

    /// <inheritdoc/>
    public string MasterKindLabel => "price level";

    /// <summary>The price level this screen was opened over for ALTERATION, or <see cref="Guid.Empty"/> when it is
    /// creating. Set only by <see cref="ForAlter"/>.</summary>
    private Guid _editingId = Guid.Empty;

    /// <inheritdoc/>
    /// <remarks>🔴 Was the constant <c>false</c> until W33 C3 wired <see cref="ForAlter"/>, with a remark saying
    /// so. It is now derived, and the derivation matters to the shell in two separate places: Alt+D is refused
    /// while it is true (deleting the level you are part-way through renaming is never what was meant), and
    /// <c>ActivateSelected</c> reads it to decide whether Ctrl+A means <see cref="Create"/> or
    /// <see cref="Alter"/>.</remarks>
    public bool IsAltering => _editingId != Guid.Empty;

    /// <summary>The column caption — "Price Level Alteration" while altering, else "Price Level Creation".</summary>
    public string Caption => IsAltering ? "Price Level Alteration" : "Price Level Creation";

    /// <summary>
    /// Opens this screen over an EXISTING price level, for a rename (census 3.10, W33 C3). Returns <c>null</c> if
    /// the id does not resolve. Mirrors <c>StockCategoryMasterViewModel.ForAlter</c> verbatim.
    ///
    /// <para><b>FIDELITY (R7): VENDOR-ATTESTED.</b> TallyPrime's own instruction for a price level is
    /// <i>"Change the names of the Price Levels and press Ctrl+A to save"</i>, reached through <i>Alt+G &gt; Alter
    /// Master &gt; Price levels</i> [help.tallysolutions.com/selling-buying-prices/, read 2026-09-25]. Here the
    /// route is the shared one — arrows to the level, Ctrl+Enter to open it — and Ctrl+A is the same accept.</para>
    ///
    /// <para>🔴 <b>Why a rename needs no guard when a DELETE of the same master does.</b> Every referent stores the
    /// <see cref="PriceLevel.Id"/>, so a renamed level keeps its price lists and its party defaults; a DELETED one
    /// would orphan them, which is why <c>PriceListService.DeleteLevel</c> refuses exactly those two cases. Before
    /// this factory, a level whose name was mistyped and which had since acquired a price list could not be
    /// corrected by ANY sequence of keys — the delete was (correctly) refused and there was no alter.</para>
    /// </summary>
    public static PriceLevelsViewModel? ForAlter(
        Company company, CompanyStorage storage, Guid levelId, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (company.FindPriceLevel(levelId) is not { } level) return null;

        var vm = new PriceLevelsViewModel(company, storage, onChanged);
        vm._editingId = levelId;
        vm.Name = level.Name;
        vm.OnPropertyChanged(nameof(IsAltering));
        vm.OnPropertyChanged(nameof(Caption));
        return vm;
    }

    /// <summary>
    /// Ctrl+A <b>alter</b>: renames the price level this screen was opened over, via
    /// <see cref="PriceListService.AlterLevel"/> (non-blank, unique excluding itself). Any domain refusal is
    /// surfaced to <see cref="Message"/> without crashing the UI, exactly as <see cref="Create"/> does.
    /// </summary>
    public bool Alter()
    {
        Message = null;
        if (_editingId == Guid.Empty)
        {
            Message = "This screen is not altering an existing price level.";
            return false;
        }

        var name = (Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "A price-level name is required.";
            return false;
        }

        try
        {
            var altered = new PriceListService(_company).AlterLevel(_editingId, name);
            _storage.Save(_company);
            Message = $"Price level '{altered.Name}' altered.";
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
            return false;
        }

        // 🔴 The NAME box is deliberately NOT cleared here, unlike Create. The screen is still sitting over the
        // same level; blanking the field would make the form contradict the list row it just renamed.
        RefreshList();
        _onChanged();
        return true;
    }

    /// <inheritdoc/>
    public IMasterListRow? HighlightedMasterRow => HighlightedRow;

    /// <inheritdoc/>
    public void ReloadExisting() => RefreshList();

    /// <inheritdoc/>
    /// <remarks>Engine-only — the shell saves and reloads after this returns. The refusal is
    /// <c>PriceListService.DeleteLevel</c>'s own message (a level with price lists, or a party default), which
    /// the shell surfaces on the notice bar.</remarks>
    public void DeleteMaster(Guid id) => new PriceListService(_company).DeleteLevel(id);

    private PayrollMasterHighlight<PriceLevelListRow>? _highlight;

    private PayrollMasterHighlight<PriceLevelListRow> Highlight =>
        _highlight ??= new PayrollMasterHighlight<PriceLevelListRow>(
            Existing, () => OnPropertyChanged(nameof(HighlightedRow)));

    /// <summary>The arrow-highlighted existing price level, or null.</summary>
    public PriceLevelListRow? HighlightedRow => Highlight.Row;

    /// <inheritdoc/>
    public void MoveHighlight(int direction) => Highlight.Move(direction);

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Price Levels",
        new[] { MasterListColumn.Text("Name"), MasterListColumn.Text("Price Lists") },
        Existing.Select(r => (IReadOnlyList<string>)new[] { r.Name, r.Lists }).ToList());

    /// <summary>The existing price levels, refreshed after each create.</summary>
    public ObservableCollection<PriceLevelListRow> Existing { get; } = new();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string? _message;

    public PriceLevelsViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        RefreshList();
    }

    /// <summary>
    /// Ctrl+A create: validates the name is non-empty, then creates the price level via the engine and persists.
    /// The engine enforces non-blank + case-insensitive uniqueness; any domain error is surfaced to
    /// <see cref="Message"/> without crashing the UI.
    /// </summary>
    public bool Create()
    {
        Message = null;
        var name = (Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "A price-level name is required.";
            return false;
        }

        try
        {
            var service = new PriceListService(_company);
            service.CreateLevel(name);
            _storage.Save(_company);
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
            return false;
        }

        RefreshList();
        Message = $"Price level '{name}' created.";
        Name = string.Empty;
        _onChanged();
        return true;
    }

    private void RefreshList()
    {
        // By ID, not by index — see PayrollMasterHighlight.RestoreTo. Re-sorting after a create would otherwise
        // slide the highlight onto a NEIGHBOURING level, which the next Alt+D would delete.
        var previouslyHighlighted = Highlight.IdBeforeRebuild();

        Existing.Clear();
        foreach (var level in _company.PriceLevels.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
        {
            var count = _company.PriceLists.Count(pl => pl.PriceLevelId == level.Id);
            Existing.Add(new PriceLevelListRow
            {
                MasterId = level.Id,
                Name = level.Name,
                Lists = count == 0 ? "—" : count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        }

        Highlight.RestoreTo(previouslyHighlighted);
    }
}
