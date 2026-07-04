using System;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A godown row for the existing-godowns list on the master screen.</summary>
public sealed class GodownListRow
{
    public string Name { get; init; } = string.Empty;
    public string Under { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
}

/// <summary>
/// One entry in the "Under" parent picker for a godown: "Primary" (top-level) or any existing godown.
/// <see cref="Godown"/> is null for the Primary option.
/// </summary>
public sealed class ParentGodownOption
{
    public Godown? Godown { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsPrimary => Godown is null;
}

/// <summary>
/// The Godown / Location creation master ("Masters → Create → Inventory Masters → Godown", catalog §9;
/// RQ-5): a name, an optional alias, an optional <b>Under</b> parent (Primary ⇒ top-level, hierarchical),
/// and the <b>"Third-party (our stock with others)"</b> job-work flag (captured now, inert). The seeded
/// "Main Location" is listed and offered as a parent. Creates the godown via the
/// <see cref="InventoryService"/> (unique name + valid, non-cyclic parent) and persists.
///
/// <para>MVVM boundary: references the domain + persistence but no Avalonia/UI types, so it is headlessly
/// unit-testable. Mirrors <see cref="CostCentreMasterViewModel"/>.</para>
/// </summary>
public sealed partial class GodownMasterViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <summary>The parent options: "Primary" plus every existing godown (Main Location included).</summary>
    public ObservableCollection<ParentGodownOption> ParentOptions { get; } = new();

    /// <summary>The existing godowns, refreshed after each create (seeded Main Location included).</summary>
    public ObservableCollection<GodownListRow> Existing { get; } = new();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _alias = string.Empty;
    [ObservableProperty] private ParentGodownOption? _selectedParent;
    [ObservableProperty] private bool _thirdParty;
    [ObservableProperty] private string? _message;

    public GodownMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        RefreshParentOptions();
        RefreshList();
    }

    /// <summary>
    /// Ctrl+A create: validates the name is non-empty, then creates the godown under the chosen parent
    /// (Primary ⇒ top-level) via the engine and persists. The engine enforces uniqueness + a valid,
    /// non-cyclic parent; any domain error is surfaced to <see cref="Message"/> without crashing the UI.
    /// </summary>
    public bool Create()
    {
        Message = null;
        var name = (Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "A godown name is required.";
            return false;
        }

        var parentId = SelectedParent?.Godown?.Id;
        var alias = string.IsNullOrWhiteSpace(Alias) ? null : Alias.Trim();

        try
        {
            var service = new InventoryService(_company);
            service.CreateGodown(name, parentId, alias, ThirdParty);
            _storage.Save(_company);
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
            return false;
        }

        var underLabel = SelectedParent is { IsPrimary: false } p ? p.Godown!.Name : "Primary";
        RefreshParentOptions();
        RefreshList();
        Message = $"Godown '{name}' created under {underLabel}.";
        Name = string.Empty;
        Alias = string.Empty;
        ThirdParty = false;
        _onChanged();
        return true;
    }

    private void RefreshParentOptions()
    {
        var previousId = SelectedParent?.Godown?.Id;
        ParentOptions.Clear();
        ParentOptions.Add(new ParentGodownOption { Godown = null, Display = "◦ Primary (top-level)" });
        foreach (var g in _company.Godowns.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            ParentOptions.Add(new ParentGodownOption { Godown = g, Display = g.Name });

        SelectedParent = ParentOptions.FirstOrDefault(o => o.Godown?.Id == previousId)
                         ?? ParentOptions.FirstOrDefault();
    }

    private void RefreshList()
    {
        Existing.Clear();
        foreach (var g in _company.Godowns)
        {
            var under = g.ParentId is { } pid
                ? _company.FindGodown(pid)?.Name ?? "—"
                : "Primary";
            var kind = g.IsMainLocation ? "Main Location" : g.ThirdParty ? "Third-party" : "Own";
            Existing.Add(new GodownListRow { Name = g.Name, Under = under, Kind = kind });
        }
    }
}
