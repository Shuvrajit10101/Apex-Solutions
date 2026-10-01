using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// One selectable voucher-type row on the Scenario master: the type's name, base kind, whether it is a
/// provisional (Optional-capable / Memorandum / Reversing) kind, and a two-way <see cref="Include"/>
/// checkbox. The scenario surfaces the provisional vouchers of every included type (and an Optional
/// voucher of an included real type). Rebuilt per scenario; no engine state until <see cref="ScenarioMasterViewModel.Create"/>.
/// </summary>
public sealed partial class ScenarioTypeRow : ViewModelBase
{
    public Guid TypeId { get; }
    public string Name { get; }
    public string Kind { get; }

    [ObservableProperty] private bool _include;

    public ScenarioTypeRow(Guid typeId, string name, string kind, bool include = false)
    {
        TypeId = typeId;
        Name = name;
        Kind = kind;
        _include = include;
    }
}

/// <summary>A row in the existing-scenarios list on the Scenario master screen.</summary>
public sealed partial class ScenarioListRow : ObservableObject, IMasterListRow
{
    public string Name { get; init; } = string.Empty;
    public string Actuals { get; init; } = string.Empty;
    public string Includes { get; init; } = string.Empty;

    /// <inheritdoc/>
    public Guid MasterId { get; init; }

    /// <inheritdoc/>
    public string MasterName => Name;

    /// <inheritdoc/>
    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>
/// The Scenario-creation master ("Masters → Create → Scenario", catalog §7; plan.md §5): pick a
/// <b>Name</b>, an <b>Include Actuals?</b> flag, and tick which voucher types the scenario surfaces —
/// with the three provisional kinds (<b>Optional</b>-capable, <b>Reversing Journal</b>, <b>Memorandum</b>)
/// offered first as quick presets. Create persists the whole scenario to the company's <c>.db</c> via
/// <see cref="CompanyStorage.Save"/>. Existing scenarios are listed below. No Avalonia types ⇒ headlessly
/// testable; mirrors <see cref="BudgetMasterViewModel"/>.
/// </summary>
public sealed partial class ScenarioMasterViewModel : ViewModelBase, IMasterListExportSource, IMasterListScreen
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    // ------------------------------------------------- W33 C3 (census 2.10): the shared master-list arm

    /// <inheritdoc/>
    public string MasterKindLabel => "scenario";

    /// <inheritdoc/>
    /// <remarks>Create-only screen — no <c>ForAlter</c> factory exists for a scenario — so it is never
    /// mid-alteration.</remarks>
    public bool IsAltering => false;

    /// <inheritdoc/>
    public IMasterListRow? HighlightedMasterRow => HighlightedRow;

    /// <inheritdoc/>
    public void ReloadExisting() => RefreshList();

    /// <inheritdoc/>
    /// <remarks>
    /// <para>🔴 <b>NO REFERENTIAL GUARD, MEASURED — see the W33 block in <c>MasterDeletionRules</c>.</b> The only
    /// foreign key into <c>scenarios(id)</c> is <c>scenario_voucher_types.scenario_id</c>, written from
    /// <c>Scenario.IncludedTypeIds</c> — the parent's own graph — so it leaves with the parent on the next
    /// delete-all + re-insert and cannot orphan. Note the direction that matters: a scenario points AT voucher
    /// types, nothing points at a scenario.</para>
    /// <para>Engine-only — the shell saves and reloads after this returns. A scenario has no service of its own
    /// (create goes straight to <c>Company.AddScenario</c>), so this is the matching direct call.</para></remarks>
    public void DeleteMaster(Guid id)
    {
        var scenario = _company.Scenarios.FirstOrDefault(s => s.Id == id)
            ?? throw new InvalidOperationException($"Scenario {id} not found.");
        _company.RemoveScenario(scenario);
    }

    private PayrollMasterHighlight<ScenarioListRow>? _highlight;

    private PayrollMasterHighlight<ScenarioListRow> Highlight =>
        _highlight ??= new PayrollMasterHighlight<ScenarioListRow>(
            Existing, () => OnPropertyChanged(nameof(HighlightedRow)));

    /// <summary>The arrow-highlighted existing scenario, or null.</summary>
    public ScenarioListRow? HighlightedRow => Highlight.Row;

    /// <inheritdoc/>
    public void MoveHighlight(int direction) => Highlight.Move(direction);

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Scenarios",
        new[] { MasterListColumn.Text("Name"), MasterListColumn.Text("Actuals"), MasterListColumn.Text("Includes") },
        Existing.Select(r => (IReadOnlyList<string>)new[] { r.Name, r.Actuals, r.Includes }).ToList());

    /// <summary>Every voucher type in the company, each with an Include checkbox (provisional kinds first).</summary>
    public ObservableCollection<ScenarioTypeRow> Types { get; } = new();

    /// <summary>The existing scenarios, refreshed after each create.</summary>
    public ObservableCollection<ScenarioListRow> Existing { get; } = new();

    // ---- scenario header ----
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private bool _includeActuals = true;

    [ObservableProperty] private string? _message;

    public ScenarioMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        BuildTypes();
        RefreshList();
    }

    /// <summary>
    /// Fills the voucher-type picker: the provisional kinds a scenario is normally built around
    /// (Journal — so an Optional accrual counts, Reversing Journal, Memorandum) first, then the rest,
    /// each name-sorted. The three defaults are pre-ticked so a fresh scenario shows provisional entries.
    /// </summary>
    private void BuildTypes()
    {
        Types.Clear();
        var active = _company.VoucherTypes.Where(t => t.IsActive).ToList();

        static int Rank(VoucherType t) => t.BaseType switch
        {
            VoucherBaseType.Journal => 0,
            VoucherBaseType.ReversingJournal => 1,
            VoucherBaseType.Memorandum => 2,
            _ => 3,
        };

        foreach (var t in active
                     .OrderBy(Rank)
                     .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            var provisional = t.BaseType is VoucherBaseType.ReversingJournal or VoucherBaseType.Memorandum;
            // Pre-tick the three kinds a scenario is usually built around.
            var preset = t.BaseType is VoucherBaseType.Journal
                or VoucherBaseType.ReversingJournal or VoucherBaseType.Memorandum;
            Types.Add(new ScenarioTypeRow(t.Id, t.Name, KindLabel(t.BaseType), include: preset));
        }
    }

    /// <summary>
    /// Ctrl+A create: validates the name (non-empty + unique) and that at least one voucher type is ticked,
    /// then builds the scenario, adds it to the company and persists. Resets the form.
    /// </summary>
    public bool Create()
    {
        Message = null;
        var name = (Name ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "A scenario name is required.";
            return false;
        }
        if (_company.FindScenarioByName(name) is not null)
        {
            Message = $"A scenario named '{name}' already exists.";
            return false;
        }

        var included = Types.Where(r => r.Include).Select(r => r.TypeId).ToList();
        if (included.Count == 0)
        {
            Message = "Tick at least one voucher type for the scenario to surface.";
            return false;
        }

        var scenario = new Scenario(Guid.NewGuid(), name, IncludeActuals, includedTypeIds: included);
        _company.AddScenario(scenario);
        _storage.Save(_company);

        RefreshList();
        Message = $"Scenario '{name}' created ({included.Count} type(s), " +
                  $"actuals {(IncludeActuals ? "included" : "excluded")}).";
        Name = string.Empty;
        _onChanged();
        return true;
    }

    private void RefreshList()
    {
        // By ID, not by index — see PayrollMasterHighlight.RestoreTo.
        var previouslyHighlighted = Highlight.IdBeforeRebuild();

        Existing.Clear();
        foreach (var s in _company.Scenarios.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            Existing.Add(new ScenarioListRow
            {
                MasterId = s.Id,
                Name = s.Name,
                Actuals = s.IncludeActuals ? "Yes" : "No",
                Includes = DescribeIncludes(s),
            });

        Highlight.RestoreTo(previouslyHighlighted);
    }

    /// <summary>Comma-joined names of a scenario's included voucher types (for the list column).</summary>
    private string DescribeIncludes(Scenario s)
    {
        var names = s.IncludedTypeIds
            .Select(id => _company.FindVoucherType(id)?.Name)
            .Where(n => n is not null)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return names.Count == 0 ? "—" : string.Join(", ", names);
    }

    /// <summary>The human label for a voucher base type shown against each row.</summary>
    public static string KindLabel(VoucherBaseType baseType) => baseType switch
    {
        VoucherBaseType.ReversingJournal => "Reversing Journal",
        VoucherBaseType.Memorandum => "Memorandum",
        VoucherBaseType.Journal => "Journal (Optional)",
        _ => baseType.ToString(),
    };
}
