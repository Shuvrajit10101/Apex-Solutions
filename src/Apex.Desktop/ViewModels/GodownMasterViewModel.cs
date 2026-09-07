using System;
using System.Collections.Generic;
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

    /// <summary>The job/project this godown is designated for (census 9.6), or empty. Its own column rather
    /// than a suffix on <see cref="Kind"/>: a godown can be third-party AND a job at once, and folding the two
    /// into one cell makes a designated job invisible on every third-party site.</summary>
    public string JobProject { get; init; } = string.Empty;
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
/// One entry in the godown master's <b>"Set job/project for job costing"</b> picker (census 9.6):
/// "Not a job/project" (<see cref="CostCentre"/> null) or any existing cost centre.
/// <para>The option list is of COST CENTRES because a job/project IS a cost centre — the vendor's own model,
/// and the reason this feature adds no cost dimension of its own. See <see cref="Godown.JobCostCentreId"/>.</para>
/// </summary>
public sealed class JobCostCentreOption
{
    public CostCentre? CostCentre { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsNone => CostCentre is null;
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
public sealed partial class GodownMasterViewModel : ViewModelBase, IMasterListExportSource
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Godowns",
        new[]
        {
            MasterListColumn.Text("Name"), MasterListColumn.Text("Under"), MasterListColumn.Text("Kind"),
            // census 9.6 — exported too, not just drawn. A master list that shows a column on screen and drops
            // it from the export is the shape that makes an exported book quietly wrong.
            MasterListColumn.Text("Job/Project"),
        },
        Existing.Select(r => (IReadOnlyList<string>)new[] { r.Name, r.Under, r.Kind, r.JobProject }).ToList());

    /// <summary>The parent options: "Primary" plus every existing godown (Main Location included).</summary>
    public ObservableCollection<ParentGodownOption> ParentOptions { get; } = new();

    /// <summary>The existing godowns, refreshed after each create (seeded Main Location included).</summary>
    public ObservableCollection<GodownListRow> Existing { get; } = new();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _alias = string.Empty;
    [ObservableProperty] private ParentGodownOption? _selectedParent;
    [ObservableProperty] private bool _thirdParty;
    [ObservableProperty] private string? _message;

    /// <summary>
    /// The vendor's <b>"Set job/project for job costing"</b> picker (census 9.6) — the cost centre this godown
    /// is, as a job/project. The first option is "◦ Not a job/project", so clearing the link is reachable from
    /// the keyboard like every other field; selecting it leaves <see cref="Godown.JobCostCentreId"/> null.
    /// </summary>
    public ObservableCollection<JobCostCentreOption> JobCostCentreOptions { get; } = new();

    [ObservableProperty] private JobCostCentreOption? _selectedJobCostCentre;

    /// <summary>
    /// True when the job/project field is shown — the F11 <see cref="Company.EnableJobCosting"/> gate (census
    /// 9.6). With Job Costing off the vendor hides the field, and so does this master.
    /// <para>🔴 <b>The gate also decides whether the value is WRITTEN</b>, not merely whether it is drawn: see
    /// <see cref="Create"/>. A hidden control that still contributes a value is the shape that puts data in a
    /// book the operator never agreed to.</para>
    /// </summary>
    public bool ShowJobCostCentre => _company.EnableJobCosting;

    /// <summary>True when Job Costing is on but the book has no cost centre to point at — the operator needs
    /// telling that the prerequisite the vendor names ("Enabling Job Costing and Cost Centres are
    /// prerequisites") is not met, rather than being shown an empty picker.</summary>
    public bool JobCostingNeedsCostCentres => _company.EnableJobCosting && _company.CostCentres.Count == 0;

    public GodownMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        RefreshParentOptions();
        RefreshJobCostCentreOptions();
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

        // 🔴 The F11 gate decides whether the value is WRITTEN, not just whether the control is drawn. With Job
        // Costing off the field is hidden, and a stale SelectedJobCostCentre from a session where it was on must
        // not silently designate this godown a job/project the operator never saw a control for.
        var jobCostCentreId = _company.EnableJobCosting ? SelectedJobCostCentre?.CostCentre?.Id : null;

        try
        {
            var service = new InventoryService(_company);
            service.CreateGodown(name, parentId, alias, ThirdParty, jobCostCentreId);
            _storage.Save(_company);
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
            return false;
        }

        var underLabel = SelectedParent is { IsPrimary: false } p ? p.Godown!.Name : "Primary";
        RefreshParentOptions();
        RefreshJobCostCentreOptions();
        RefreshList();
        Message = $"Godown '{name}' created under {underLabel}.";
        Name = string.Empty;
        Alias = string.Empty;
        ThirdParty = false;
        // Reset to "not a job/project" so the NEXT godown does not inherit this one's job by accident — the
        // same reason Name/Alias/ThirdParty are cleared.
        SelectedJobCostCentre = JobCostCentreOptions.FirstOrDefault();
        _onChanged();
        return true;
    }

    private void RefreshJobCostCentreOptions()
    {
        var previousId = SelectedJobCostCentre?.CostCentre?.Id;
        JobCostCentreOptions.Clear();
        JobCostCentreOptions.Add(new JobCostCentreOption { CostCentre = null, Display = "◦ Not a job/project" });
        // OrdinalIgnoreCase, not the current culture — the same ordering must come out on the ubuntu, macos and
        // windows legs of the gate, and a picker whose order depends on the machine cannot be asserted on.
        foreach (var c in _company.CostCentres.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            JobCostCentreOptions.Add(new JobCostCentreOption { CostCentre = c, Display = c.Name });

        SelectedJobCostCentre = JobCostCentreOptions.FirstOrDefault(o => o.CostCentre?.Id == previousId)
                                ?? JobCostCentreOptions.FirstOrDefault();
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
            // census 9.6: the job/project, resolved to the cost centre's NAME. A link whose centre has since
            // been deleted shows "(unknown)" rather than an empty cell — silently blanking it would hide a
            // godown that is still designated a job and would still produce Job Work Analysis rows.
            var job = g.JobCostCentreId is { } centreId
                ? _company.FindCostCentre(centreId)?.Name ?? "(unknown)"
                : string.Empty;
            Existing.Add(new GodownListRow { Name = g.Name, Under = under, Kind = kind, JobProject = job });
        }
    }
}
