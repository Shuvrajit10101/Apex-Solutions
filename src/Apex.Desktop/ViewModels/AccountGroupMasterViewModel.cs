using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>An accounting-group row for the existing-groups list on the Group-master screen.</summary>
public sealed class AccountGroupListRow
{
    public string Name { get; init; } = string.Empty;
    public string Under { get; init; } = string.Empty;
    public string Nature { get; init; } = string.Empty;
}

/// <summary>
/// One entry in the <b>Under</b> picker for an accounting group: <i>Primary</i> (a new top-level head, no parent)
/// or any existing group. <see cref="Group"/> is <c>null</c> for the Primary option.
///
/// <para>🔴 <b>THIS TYPE IS THE FIX FOR DEFECT T1-31, AND ITS ABSENCE IS WHAT BLOCKED CENSUS ROW 2.2 BY
/// CONSTRUCTION.</b> The picker previously held only existing <see cref="Group"/> instances, so <i>Primary</i> was
/// not offerable and a new primary group could not be created at all — while this product's own Stock Group and
/// Godown masters had offered Primary all along. Two of the vendor's Group fields (<i>Nature of Group</i> and
/// <i>Does it affect Gross Profits</i>) appear ONLY under a primary group, so they were unreachable until this
/// existed. Mirrors <see cref="ParentStockGroupOption"/> deliberately: the two pickers now behave the same.</para>
/// </summary>
public sealed class ParentAccountGroupOption
{
    public Group? Group { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsPrimary => Group is null;
}

/// <summary>One option in the <i>Nature of Group</i> picker (vendor: Assets / Liabilities / Expenses / Income).</summary>
public sealed class GroupNatureChoice
{
    public GroupNature Value { get; }
    public string Display { get; }
    public GroupNatureChoice(GroupNature value, string display) { Value = value; Display = display; }
}

/// <summary>One option in the vendor's <i>"Method to allocate when used in purchase invoice"</i> picker.</summary>
public sealed class GroupAllocationMethodChoice
{
    public MethodOfAppropriation? Value { get; }
    public string Display { get; }
    public GroupAllocationMethodChoice(MethodOfAppropriation? value, string display)
    { Value = value; Display = display; }
}

/// <summary>
/// The accounting-Group creation master ("Masters → Create → Group", Gateway → Create → Group; catalog §3; WI-7):
/// a name, an optional alias, and an <b>Under</b> parent picked from the 28 predefined groups (Current Assets /
/// Current Liabilities / … ) or any custom group. The <b>Nature is shown READ-ONLY and DERIVED from the parent</b>
/// (<see cref="DerivedNature"/>) — the user never types or chooses a nature, exactly as Tally derives Asset /
/// Liability / Income / Expense from the parent's primary ancestor. Creates the group via
/// <see cref="GroupService.CreateGroup"/> (unique name, existing parent, derived nature) and persists.
///
/// <para>This is what point 9's first half needs: a "Salary Payable" group under Current Liabilities holding one
/// ledger per employee — a payable that prints on the Balance-Sheet liabilities side. Schema, Io and report
/// classification already handle custom groups, so this is a pure UI + engine-service slice.</para>
///
/// <para>MVVM boundary: references the domain + persistence but no Avalonia/UI types, so it is headlessly
/// unit-testable. Mirrors <see cref="StockGroupMasterViewModel"/>.</para>
/// </summary>
public sealed partial class AccountGroupMasterViewModel : ViewModelBase, IMasterListExportSource
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Groups",
        new[] { MasterListColumn.Text("Name"), MasterListColumn.Text("Under"), MasterListColumn.Text("Nature") },
        Existing.Select(r => (IReadOnlyList<string>)new[] { r.Name, r.Under, r.Nature }).ToList());

    /// <summary>The parent options: <b>Primary</b> (T1-31) plus every existing group (28 predefined roots + any
    /// custom), name-sorted.</summary>
    public ObservableCollection<ParentAccountGroupOption> ParentOptions { get; } = new();

    /// <summary>The existing groups, refreshed after each create.</summary>
    public ObservableCollection<AccountGroupListRow> Existing { get; } = new();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _alias = string.Empty;
    [ObservableProperty] private ParentAccountGroupOption? _selectedParent;
    [ObservableProperty] private string? _message;

    // ------------------------------------------------------- census 2.2: the vendor's Group behavioural fields

    /// <summary>The four <i>Nature of Group</i> options. Shown — and read — only under <b>Primary</b>.</summary>
    public IReadOnlyList<GroupNatureChoice> NatureChoices { get; } = new[]
    {
        new GroupNatureChoice(GroupNature.Asset, "Assets"),
        new GroupNatureChoice(GroupNature.Liability, "Liabilities"),
        new GroupNatureChoice(GroupNature.Income, "Income"),
        new GroupNatureChoice(GroupNature.Expense, "Expenses"),
    };

    /// <summary>The vendor's three <i>Method to allocate when used in purchase invoice</i> options.</summary>
    public IReadOnlyList<GroupAllocationMethodChoice> AllocationMethodChoices { get; } = new[]
    {
        new GroupAllocationMethodChoice(null, "Not Applicable"),
        new GroupAllocationMethodChoice(MethodOfAppropriation.ByQuantity, "Appropriate by Qty"),
        new GroupAllocationMethodChoice(MethodOfAppropriation.ByValue, "Appropriate by Value"),
    };

    [ObservableProperty] private GroupNatureChoice? _selectedNature;
    [ObservableProperty] private bool _behavesLikeSubLedger;
    [ObservableProperty] private bool _nettBalancesForReporting;
    [ObservableProperty] private bool _usedForCalculation;
    [ObservableProperty] private bool _affectsGrossProfits;
    [ObservableProperty] private GroupAllocationMethodChoice? _selectedAllocationMethod;

    /// <summary>
    /// Census 3.13 — the accounting Group's <i>Set/Alter GST Details</i> block, the rung
    /// <c>MasterAncestry.NearestGroupGst</c> already walks and which no screen could write until now.
    /// </summary>
    public MasterGstBlockEditor Gst { get; } = new();

    /// <summary>
    /// True iff the chosen <b>Under</b> is <i>Primary</i>. The vendor shows <i>Nature of Group</i> and <i>Does it
    /// affect Gross Profits</i> ONLY on that screen, so both fields are bound to this and the engine refuses them
    /// off it — the visibility and the guard say the same thing, in two places, on purpose.
    /// </summary>
    public bool IsPrimaryGroup => SelectedParent is { IsPrimary: true };

    /// <summary>The inverse, for the derived-nature caption that a child group shows instead of the picker.</summary>
    public bool IsChildGroup => !IsPrimaryGroup;

    // --------------------------------------------------------------- alteration state (WI-3)

    /// <summary>The id of the group being ALTERED, or <see cref="Guid.Empty"/> in Create mode. The alteration saves
    /// against this stable Guid, so a rename mutates the group in place and every child group, ledger and report
    /// that resolves it by id follows the rename retroactively.</summary>
    private Guid _editingId = Guid.Empty;

    /// <summary>True iff this screen is altering an existing group rather than creating one.</summary>
    public bool IsAltering => _editingId != Guid.Empty;

    /// <summary>
    /// Opens this master in <b>Alter</b> mode over an existing group (WI-3) — the same form, pre-filled.
    /// Returns <c>null</c> if the id does not resolve.
    /// </summary>
    public static AccountGroupMasterViewModel? ForAlter(
        Company company, CompanyStorage storage, Guid groupId, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (company.FindGroup(groupId) is not { } group) return null;

        var vm = new AccountGroupMasterViewModel(company, storage, onChanged);
        vm._editingId = groupId;
        vm.LoadFrom(group);
        vm.OnPropertyChanged(nameof(IsAltering));
        return vm;
    }

    /// <summary>
    /// Loads an existing group's values into the form — every field the screen offers, so an Alter that changes
    /// one thing writes back the other seven unchanged rather than resetting them to the form's defaults. (That
    /// is not hypothetical: <see cref="Alter"/> passes the WHOLE <see cref="GroupBehaviour"/>, so a field this
    /// method forgot to read would be silently cleared on the next accept.)
    /// </summary>
    public void LoadFrom(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);
        Name = group.Name;
        Alias = group.Alias ?? string.Empty;
        // A group may not be its own parent; the picker still lists every group, and the engine's cycle guard
        // rejects a descendant, so the message is precise rather than the option being silently missing.
        SelectedParent = ParentOptions.FirstOrDefault(o => o.Group?.Id == group.ParentId)
            ?? (group.ParentId is null ? ParentOptions.FirstOrDefault(o => o.IsPrimary) : SelectedParent)
            ?? SelectedParent;

        // census 2.2 — the five behavioural fields plus the primary group's own nature.
        SelectedNature = NatureChoices.FirstOrDefault(n => n.Value == group.Nature) ?? NatureChoices[0];
        BehavesLikeSubLedger = group.BehavesLikeSubLedger;
        NettBalancesForReporting = group.NettBalancesForReporting;
        UsedForCalculation = group.UsedForCalculation;
        AffectsGrossProfits = group.AffectsGrossProfits;
        SelectedAllocationMethod =
            AllocationMethodChoices.FirstOrDefault(m => m.Value == group.PurchaseAllocationMethod)
            ?? AllocationMethodChoices[0];

        // census 3.13 — the group-level GST block.
        Gst.LoadFrom(group.Gst);
    }

    /// <summary>The behavioural fields exactly as the form currently reads them, built once so
    /// <see cref="Create"/> and <see cref="Alter"/> cannot send different subsets.</summary>
    private GroupBehaviour CurrentBehaviour() => new(
        BehavesLikeSubLedger,
        NettBalancesForReporting,
        UsedForCalculation,
        // The vendor shows this only under Primary; sending it from a child screen would be refused by the engine,
        // so the form does not send it. The guard still exists in the engine for the import and test paths.
        IsPrimaryGroup && AffectsGrossProfits,
        SelectedAllocationMethod?.Value);

    /// <summary>The nature to send: the operator's pick for a PRIMARY group, and nothing at all for a child
    /// (whose nature the engine derives from its parent and would refuse to be told).</summary>
    private GroupNature? PrimaryNatureOrNull()
        => IsPrimaryGroup ? SelectedNature?.Value ?? GroupNature.Asset : null;

    /// <summary>
    /// Ctrl+A <b>alter</b> (WI-3): renames / re-aliases / re-parents the group this screen was opened over, via
    /// <see cref="GroupService.AlterGroup"/> — which enforces except-self name uniqueness, blocks altering a
    /// predefined group, rejects a cyclic parent, and <b>re-derives the nature and cascades it to every
    /// descendant</b> so a moved sub-tree cannot keep its old ancestry's Balance-Sheet side.
    /// </summary>
    public bool Alter()
    {
        Message = null;
        if (_editingId == Guid.Empty)
        {
            Message = "This screen is not altering an existing group.";
            return false;
        }
        if (SelectedParent is null)
        {
            Message = "Pick an Under — Primary for a new top-level head, or a parent group to nest under.";
            return false;
        }
        if (!Gst.TryBuild(out var gstBlock, out var gstError))
        {
            Message = gstError;
            return false;
        }

        var alias = string.IsNullOrWhiteSpace(Alias) ? null : Alias.Trim();
        var underLabel = SelectedParent.IsPrimary ? "Primary" : SelectedParent.Group!.Name;
        try
        {
            var service = new GroupService(_company);
            var altered = service.AlterGroup(
                _editingId, Name, SelectedParent.Group?.Id, alias, PrimaryNatureOrNull(), CurrentBehaviour());
            // census 3.13 — the form owns the block outright, so switching "Set/Alter GST Details" off clears it.
            altered.Gst = gstBlock;
            _storage.Save(_company);
            Message = $"Group '{altered.Name}' altered — under {underLabel} ({altered.Nature}).";
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
            return false;
        }

        RefreshParentOptions();
        RefreshList();
        OnPropertyChanged(nameof(DerivedNature));
        _onChanged();
        return true;
    }

    public AccountGroupMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        RefreshParentOptions();
        RefreshList();

        SelectedNature = NatureChoices[0];
        SelectedAllocationMethod = AllocationMethodChoices[0];

        // Default the Under to Current Liabilities (the WI-7 driving example — a Salary Payable head), else the
        // first option. The picker holds the option wrappers, so this reference-matches the ComboBox item.
        SelectedParent =
            ParentOptions.FirstOrDefault(o => o.Group is { } g
                && string.Equals(g.Name, "Current Liabilities", StringComparison.OrdinalIgnoreCase))
            ?? ParentOptions.FirstOrDefault();
    }

    /// <summary>
    /// The nature a CHILD group WILL inherit from the chosen parent, shown READ-ONLY: Tally derives Asset /
    /// Liability / Income / Expense from the parent's primary ancestor and the user never picks one. Reads "—" for
    /// a primary group, where the nature is not derived at all but chosen in <see cref="SelectedNature"/>.
    /// </summary>
    public string DerivedNature => SelectedParent?.Group is { } p
        ? GroupService.DeriveNature(p, _company).ToString()
        : "—";

    partial void OnSelectedParentChanged(ParentAccountGroupOption? value)
    {
        OnPropertyChanged(nameof(DerivedNature));
        OnPropertyChanged(nameof(IsPrimaryGroup));
        OnPropertyChanged(nameof(IsChildGroup));

        // Moving back under a parent takes the primary-only flag with it, so a value the engine would refuse can
        // never be left sitting in the form waiting to fail an otherwise valid accept.
        if (value is { IsPrimary: false }) AffectsGrossProfits = false;
    }

    /// <summary>
    /// Ctrl+A create: validates a non-empty name and a chosen parent, then creates the group under that parent via
    /// the engine (which derives the nature, enforces a unique name and an existing parent) and persists. Any
    /// domain error is surfaced to <see cref="Message"/> without crashing the UI.
    /// </summary>
    public bool Create()
    {
        Message = null;
        var name = (Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "A group name is required.";
            return false;
        }
        if (SelectedParent is null)
        {
            Message = "Pick an Under — Primary for a new top-level head, or a parent group to nest under.";
            return false;
        }
        if (!Gst.TryBuild(out var gstBlock, out var gstError))
        {
            Message = gstError;
            return false;
        }

        var alias = string.IsNullOrWhiteSpace(Alias) ? null : Alias.Trim();
        var parentName = SelectedParent.IsPrimary ? "Primary" : SelectedParent.Group!.Name;

        Group created;
        try
        {
            var service = new GroupService(_company);
            created = service.CreateGroup(
                name, SelectedParent.Group?.Id, alias, PrimaryNatureOrNull(), CurrentBehaviour());
            created.Gst = gstBlock;   // census 3.13
            _storage.Save(_company);
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
            return false;
        }

        RefreshParentOptions();   // the new group is now selectable as a parent
        RefreshList();
        Message = $"Group '{name}' created under {parentName} ({created.Nature}).";
        Name = string.Empty;
        Alias = string.Empty;
        Gst.LoadFrom(null);
        _onChanged();
        return true;
    }

    private void RefreshParentOptions()
    {
        var previousId = SelectedParent?.Group?.Id;
        var wasPrimary = SelectedParent is { IsPrimary: true };
        ParentOptions.Clear();
        // T1-31: Primary comes FIRST, exactly as it does on the Stock Group and Godown masters.
        ParentOptions.Add(new ParentAccountGroupOption { Group = null, Display = "◦ Primary (top-level)" });
        foreach (var g in _company.Groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            ParentOptions.Add(new ParentAccountGroupOption { Group = g, Display = g.Name });

        // Keep the chosen parent selected across a refresh (so the user can add several groups under one head).
        SelectedParent = wasPrimary
            ? ParentOptions[0]
            : ParentOptions.FirstOrDefault(o => o.Group?.Id == previousId) ?? SelectedParent;
    }

    private void RefreshList()
    {
        Existing.Clear();
        foreach (var g in _company.Groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
        {
            var under = g.ParentId is { } pid ? _company.FindGroup(pid)?.Name ?? "—" : "Primary";
            Existing.Add(new AccountGroupListRow
            {
                Name = g.Name,
                Under = under,
                // The classification the reports use — the group's primary-ancestor nature.
                Nature = ClassificationRules.PrimaryNatureOf(g, _company).ToString(),
            });
        }
    }
}
