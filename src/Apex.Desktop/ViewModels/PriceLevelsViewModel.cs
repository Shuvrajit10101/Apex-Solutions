using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A price-level row for the existing-levels list on the master screen.</summary>
public sealed class PriceLevelListRow
{
    public string Name { get; init; } = string.Empty;

    /// <summary>How many dated price-list versions reference this level (for the operator's context).</summary>
    public string Lists { get; init; } = string.Empty;
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
public sealed partial class PriceLevelsViewModel : ViewModelBase, IMasterListExportSource
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

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
        Existing.Clear();
        foreach (var level in _company.PriceLevels.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
        {
            var count = _company.PriceLists.Count(pl => pl.PriceLevelId == level.Id);
            Existing.Add(new PriceLevelListRow
            {
                Name = level.Name,
                Lists = count == 0 ? "—" : count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        }
    }
}
