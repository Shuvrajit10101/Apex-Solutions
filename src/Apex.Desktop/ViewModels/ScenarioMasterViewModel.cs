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

    /// <summary>
    /// W46 AF (census 2.10) — the vendor's second tick list: <i>"Select the voucher types to Exclude"</i>, whose
    /// purpose the same page states as <i>"allows you to exclude some of the voucher types selected under the
    /// Include field"</i> (help.tallysolutions.com/tally-prime/financial-controlling/scenarios-tally/, opened by
    /// content 2026-10-11). Writes <c>Scenario.ExcludedTypeIds</c>, which had NO Desktop caller before this —
    /// an exclusion could previously arrive only through import.
    /// </summary>
    [ObservableProperty] private bool _exclude;

    public ScenarioTypeRow(Guid typeId, string name, string kind, bool include = false, bool exclude = false)
    {
        TypeId = typeId;
        Name = name;
        Kind = kind;
        _include = include;
        _exclude = exclude;
    }
}

/// <summary>A row in the existing-scenarios list on the Scenario master screen.</summary>
public sealed partial class ScenarioListRow : ObservableObject, IMasterListRow
{
    public string Name { get; init; } = string.Empty;
    public string Actuals { get; init; } = string.Empty;
    public string Includes { get; init; } = string.Empty;

    /// <summary>W46 AF (census 2.10) — the excluded voucher types, so an exclusion is VISIBLE on the list and
    /// in the CSV export rather than existing only inside the aggregate.</summary>
    public string Excludes { get; init; } = string.Empty;

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

    // ------------------------------------------ W46 AF (census 2.10): the ALTER verb and the EXCLUDE half
    //
    // 🔴 BOTH ARRIVE TOGETHER BECAUSE THE SAME VENDOR PAGE ATTESTS BOTH, AND BECAUSE THE GAP WAS A GROUNDING
    // GAP. The W33 C3 block in MainWindowViewModel.AlterHighlightedMasterListRow withheld this verb on the
    // stated ground that no vendor page said what altering a scenario means. One does, and it was opened and
    // read BY CONTENT on 2026-10-11 —
    // help.tallysolutions.com/tally-prime/financial-controlling/scenarios-tally/ — verbatim:
    //   · "Press Alt+G (Go To) > Alter Master > Scenario > and press Enter."
    //   · "Select the Scenario you want to alter from the List of Scenarios."
    //   · "From the Include and Exclude sections, select the scenarios to alter from the List of Voucher Types."
    //   · "Modify the details as required and, as always, accept the Scenario Alteration screen."
    //   · "Select the voucher types to Exclude."
    //   · "This is a unique option ensured by TallyPrime which allows you to exclude some of the voucher types
    //      selected under the Include field, in case you don't want those transactions to be reflected in your
    //      financial reports."
    // The LAST clause is why Exclude is a THIRD state on each row rather than a second independent tick list:
    // the vendor excludes FROM the included set. `Scenario.ExcludedTypeIds` and the `is_included` column on
    // `scenario_voucher_types` already existed with no Desktop caller at all — this wires the caller, and
    // needs NO migration.
    //
    // 🔴 THREE THINGS THIS DOES NOT CLOSE, SAID HERE SO NOBODY READS IT AS COMPLETING ROW 2.10.
    //   (a) The same page attests two switches we have no field for — "Exclude forex gain/loss calculations"
    //       and "Exclude inventory tracking calculations". Both need new `scenarios` columns, i.e. a MIGRATION;
    //       v66 ownership is an open user decision (T2-125) and this track did not take it.
    //   (b) The census cell names "no period" as a gap. 🔴 THAT GAP IS MIS-SPECIFIED AND NOTHING WAS BUILT FOR
    //       IT: the TallyPrime Scenario Creation screen has NO period/From/To/date field at all (its field list
    //       is Name, Include actuals, the two Exclude switches above, Include, Exclude — checked twice by
    //       content on the page above). A scenario period is a Tally.ERP 9 artefact, which R7 does not make
    //       ground truth; building one would repeat census 2.3's alias fault exactly.
    //   (c) Our DELETE verb is OURS. The vendor page documents no scenario deletion. The cell's "no Delete"
    //       is stale — DeleteMaster below has shipped since W33 C3 — but it is a divergence, not fidelity.

    /// <summary>The id of the scenario being ALTERED, or <see cref="Guid.Empty"/> in Create mode.</summary>
    private Guid _editingId = Guid.Empty;

    /// <inheritdoc/>
    public bool IsAltering => _editingId != Guid.Empty;

    /// <summary>The screen heading — it names the VERB that is running, because the form is identical in both
    /// modes. The altering caption is the vendor's own screen name, <i>"Scenario Alteration"</i>.</summary>
    public string Caption => IsAltering ? "Scenario Alteration" : "Scenario Creation";

    /// <summary>The commit button's label, matching the verb <see cref="Caption"/> names.</summary>
    public string CommitLabel => IsAltering ? "Save (Ctrl+A)" : "Create (Ctrl+A)";

    /// <summary>
    /// Opens this master in <b>Alter</b> mode over an existing scenario — the same form, pre-filled, with each
    /// voucher type's row already on Include, Exclude or neither. Returns <c>null</c> if the id does not resolve.
    /// </summary>
    public static ScenarioMasterViewModel? ForAlter(
        Company company, CompanyStorage storage, Guid scenarioId, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (company.FindScenario(scenarioId) is not { } scenario) return null;

        var vm = new ScenarioMasterViewModel(company, storage, onChanged);
        vm._editingId = scenarioId;
        vm.LoadFrom(scenario);
        vm.OnPropertyChanged(nameof(IsAltering));
        vm.OnPropertyChanged(nameof(Caption));
        vm.OnPropertyChanged(nameof(CommitLabel));
        return vm;
    }

    /// <summary>
    /// Loads an existing scenario into the form — name, Include-Actuals, and every voucher type's Include /
    /// Exclude state. Types the scenario names neither way are cleared, so the pre-ticked create defaults
    /// (<see cref="BuildTypes"/>) never leak into an alteration and quietly widen a saved scenario.
    /// </summary>
    public void LoadFrom(Scenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        Name = scenario.Name;
        IncludeActuals = scenario.IncludeActuals;

        foreach (var row in Types)
        {
            row.Include = scenario.Includes(row.TypeId);
            row.Exclude = scenario.Excludes(row.TypeId);
        }
    }

    /// <summary>
    /// Ctrl+A <b>alter</b>: re-validates the name (unique <i>excluding this scenario</i>) and the tick lists,
    /// then writes the name, the actuals flag and both voucher-type sets back onto the existing scenario.
    /// </summary>
    /// <remarks>
    /// 🔴 The name check excludes this scenario's own row. <see cref="Create"/>'s check is
    /// <c>FindScenarioByName(name) is not null</c>, which on an alteration screen refuses the scenario's OWN
    /// unchanged name — the shape that got census rows 3.2 and 3.5 demoted.
    /// </remarks>
    public bool Alter()
    {
        Message = null;

        if (_editingId == Guid.Empty)
        {
            Message = "This screen is not altering an existing scenario.";
            return false;
        }
        if (_company.FindScenario(_editingId) is not { } scenario)
        {
            Message = "The scenario being altered no longer exists.";
            return false;
        }

        var name = (Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "A scenario name is required.";
            return false;
        }
        if (_company.Scenarios.Any(s => s.Id != _editingId
                                        && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            Message = $"A scenario named '{name}' already exists.";
            return false;
        }

        if (ExclusionConflict() is { } conflict)
        {
            Message = conflict;
            return false;
        }

        var included = Types.Where(r => r.Include).Select(r => r.TypeId).ToList();
        var excluded = Types.Where(r => r.Exclude).Select(r => r.TypeId).ToList();
        if (included.Count == 0)
        {
            Message = "Tick at least one voucher type for the scenario to surface.";
            return false;
        }

        var beforeName = scenario.Name;
        var beforeActuals = scenario.IncludeActuals;
        var beforeIncluded = scenario.IncludedTypeIds.ToList();
        var beforeExcluded = scenario.ExcludedTypeIds.ToList();

        scenario.Name = name;
        scenario.IncludeActuals = IncludeActuals;
        scenario.ReplaceTypes(included, excluded);
        try
        {
            _storage.Save(_company);
        }
        catch (Exception ex)
        {
            scenario.Name = beforeName;
            scenario.IncludeActuals = beforeActuals;
            scenario.ReplaceTypes(beforeIncluded, beforeExcluded);
            if (!SaveFailure.IsReportable(ex)) throw;
            Message = ex.Message;
            return false;
        }

        RefreshList();
        Message = $"Scenario '{name}' altered ({included.Count} included, {excluded.Count} excluded).";
        _onChanged();
        return true;
    }

    /// <summary>
    /// The one refusal both verbs share: a voucher type may not be ticked Include <b>and</b> Exclude at once.
    /// Returns the message, or <c>null</c> when the tick lists are coherent.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>THIS IS A REFUSAL, NOT A SILENT PRECEDENCE RULE, AND THE DISTINCTION IS DELIBERATE.</b>
    /// <see cref="Scenario"/>'s own documented semantics give Exclude precedence over Include, so a row ticked
    /// both ways WOULD resolve — to "excluded". Resolving it silently is the defect: the operator asked for two
    /// contradictory things and the screen would honour one without saying which. Naming the type is what makes
    /// the refusal actionable.
    /// </remarks>
    private string? ExclusionConflict()
    {
        var both = Types.FirstOrDefault(r => r.Include && r.Exclude);
        return both is null
            ? null
            : $"'{both.Name}' is ticked both Include and Exclude — pick one.";
    }

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
        new[]
        {
            MasterListColumn.Text("Name"), MasterListColumn.Text("Actuals"),
            MasterListColumn.Text("Includes"), MasterListColumn.Text("Excludes"),
        },
        Existing.Select(r => (IReadOnlyList<string>)new[] { r.Name, r.Actuals, r.Includes, r.Excludes }).ToList());

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

        if (ExclusionConflict() is { } conflict)
        {
            Message = conflict;
            return false;
        }

        var included = Types.Where(r => r.Include).Select(r => r.TypeId).ToList();
        var excluded = Types.Where(r => r.Exclude).Select(r => r.TypeId).ToList();
        if (included.Count == 0)
        {
            Message = "Tick at least one voucher type for the scenario to surface.";
            return false;
        }

        var scenario = new Scenario(
            Guid.NewGuid(), name, IncludeActuals,
            includedTypeIds: included,
            excludedTypeIds: excluded);
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
                Includes = DescribeTypes(s.IncludedTypeIds),
                Excludes = DescribeTypes(s.ExcludedTypeIds),
            });

        Highlight.RestoreTo(previouslyHighlighted);
    }

    /// <summary>Comma-joined names of a set of voucher types (for the Includes / Excludes list columns).</summary>
    private string DescribeTypes(IReadOnlyCollection<Guid> typeIds)
    {
        var names = typeIds
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
