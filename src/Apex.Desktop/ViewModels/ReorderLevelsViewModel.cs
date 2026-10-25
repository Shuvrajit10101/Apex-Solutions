using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A reorder-definition row for the existing-definitions list on the master screen.
///
/// <para>W33 C3 (census 3.12) — carries <see cref="IMasterListRow"/> so the ONE shared
/// <see cref="IMasterListScreen"/> arm can walk it with the arrows and delete it with Alt+D.</para>
/// </summary>
public sealed partial class ReorderLevelListRow : ObservableObject, IMasterListRow
{
    public string Scope { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Reorder { get; init; } = string.Empty;
    public string MinQty { get; init; } = string.Empty;
    public string Period { get; init; } = string.Empty;
    public string Criteria { get; init; } = string.Empty;

    /// <inheritdoc/>
    public Guid MasterId { get; init; }

    /// <summary><inheritdoc/>
    /// <para>🔴 <b>"Item 'Widget'", not "Widget".</b> A reorder definition has no name of its own — it is
    /// identified by its SCOPE plus its TARGET, and the same target name can legitimately appear under two
    /// scopes. A confirmation reading <i>Delete reorder level 'Widget'?</i> would not tell the operator which
    /// of the two rows they are about to remove; this one does.</para></summary>
    public string MasterName => $"{Scope} '{Target}'";

    /// <inheritdoc/>
    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>A reorder-scope picker option (Item / Group / Category).</summary>
public sealed class ReorderScopeOption
{
    public ReorderScope Scope { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>A reorder-criterion picker option (Higher / Lower).</summary>
public sealed class ReorderCriteriaOption
{
    public ReorderCriteria Criteria { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>A reorder-target picker option (a stock item / group / category, per the selected scope).</summary>
public sealed class ReorderTargetOption
{
    public Guid Id { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>
/// The <b>Reorder Levels</b> master screen ("Masters → Create → Inventory Masters → Reorder Levels"; Phase 6
/// slice 6; requirements RQ-32..RQ-35/RQ-53/RQ-54; Tally-Book pp.158–162). A reorder definition is created per
/// <b>Stock Item</b>, <b>Stock Group</b> or <b>Stock Category</b> (the <b>Scope</b> picker), carrying two figures
/// the Reorder-Status report resolves — the <b>reorder level</b> and the <b>minimum order quantity</b>. Each figure
/// is independently <b>Simple</b> (a fixed typed quantity) or <b>Advanced</b> (reconciled Higher/Lower against the
/// item's consumption over a rolling period) via the screen's two toggles:
/// <list type="bullet">
///   <item><b>Alt+S</b> toggles the reorder level Simple⇄Advanced (<see cref="ReorderAdvanced"/>);</item>
///   <item><b>Alt+V</b> toggles the min-order-qty Simple⇄Advanced (<see cref="MinQtyAdvanced"/>).</item>
/// </list>
/// A single shared consumption <b>Period</b> (count + unit) and <b>Criteria</b> (Higher/Lower) govern both Advanced
/// figures (DD-1). At most one definition per (scope, target); creating for an existing (scope, target)
/// <b>replaces</b> it (upsert — RQ-32). Persists via <see cref="ReorderLevelsService"/>.
///
/// <para>MVVM boundary: references the domain + persistence but no Avalonia/UI types, so it is headlessly
/// unit-testable. Mirrors <see cref="BatchMasterViewModel"/> / <see cref="PriceLevelsViewModel"/>.</para>
/// </summary>
public sealed partial class ReorderLevelsViewModel : ViewModelBase, IMasterListExportSource, IMasterListScreen
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    // ------------------------------------------------- W33 C3 (census 3.12): the shared master-list arm

    /// <inheritdoc/>
    public string MasterKindLabel => "reorder level for";

    /// <summary>The reorder definition this screen was opened over for ALTERATION, or <see cref="Guid.Empty"/>
    /// when it is creating. Set only by <see cref="ForAlter"/>.</summary>
    private Guid _editingId = Guid.Empty;

    /// <inheritdoc/>
    /// <remarks>🔴 Was the CONSTANT <c>false</c> until census 3.12's alter slice, with a remark saying the screen
    /// was create-only because an existing scope+target is replaced by the <c>CreateOrUpdate</c> upsert. That
    /// remark described the only route that existed; it was never a statement that the vendor has no alter route,
    /// and the vendor does — see <see cref="ForAlter"/>. It is now derived, and the derivation matters to the
    /// shell in two separate places: Alt+D is refused while it is true (deleting the definition you are part-way
    /// through correcting is never what was meant), and <c>AcceptCurrentPage</c> reads it to decide whether Ctrl+A
    /// means <see cref="Create"/> or <see cref="Alter"/>.</remarks>
    public bool IsAltering => _editingId != Guid.Empty;

    /// <summary>The column caption — "Reorder Level Alteration" while altering, else "Reorder Level Creation".</summary>
    public string Caption => IsAltering ? "Reorder Level Alteration" : "Reorder Level Creation";

    /// <summary>
    /// Opens this screen over an EXISTING reorder definition, for a correction (census 3.12). Returns
    /// <c>null</c> if the id does not resolve. Mirrors <c>PriceLevelsViewModel.ForAlter</c>.
    ///
    /// <para><b>FIDELITY (R7, ruling 14): VENDOR-ATTESTED.</b> TallyPrime's own route to the reorder level IS an
    /// alteration route — <i>"Press Alt+G (Go To) &gt; Alter Master &gt; type or select Reorder Level (Stock
    /// Item) and press Enter."</i> — and the fields it then shows are the ones this factory loads back:
    /// <i>"Period: Select the consumption for various periods – Days, Weeks, Months, or Years."</i>, a
    /// <i>Criteria</i> of Higher or Lower, and <i>"Minimum Order Quantity: Enter the minimum quantity you want to
    /// order."</i> The same page puts the Advanced toggles on that screen as <b>Alt+S</b> and <b>Alt+V</b>, which
    /// is what this screen already binds. [<c>help.tallysolutions.com/tally-prime/inventory/reorder-stock-items-reorder-status-and-reorder-quantity/</c>,
    /// opened and read BY CONTENT 2026-10-23.] 🔴 The ROUTE here — arrows over the existing-definitions list,
    /// Ctrl+Enter to open the highlighted one — is <b>OURS</b>, exactly as it is for every other arm of
    /// <c>MainWindowViewModel.AlterHighlightedMasterListRow</c>; only the SCREEN and its fields are attested.
    ///
    /// <para>🔴 <b>WHY THE UPSERT WAS NOT ALREADY AN ALTER, measured rather than assumed.</b> Nothing on this
    /// screen ever LOADED an existing definition. Picking a target that already had one left every field blank,
    /// and <see cref="Create"/> reads a blank quantity as <i>unset</i> (null) — so an operator who opened the
    /// screen to raise one figure and pressed Ctrl+A silently WIPED the other figure, the consumption period and
    /// the criterion from a definition the Reorder Status report consumes. Loading the row back is the half of
    /// this that fixes a data loss; keeping the id (<see cref="ReorderLevelsService.Alter"/>) is the half that
    /// stops the correction becoming a different record.</para>
    /// </para>
    /// </summary>
    public static ReorderLevelsViewModel? ForAlter(
        Company company, CompanyStorage storage, Guid definitionId, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (company.FindReorderDefinition(definitionId) is not { } definition) return null;

        var vm = new ReorderLevelsViewModel(company, storage, onChanged);
        vm._editingId = definitionId;

        // Scope first — it is what rebuilds Targets, so the target pick below has something to land on.
        vm.SelectedScope = vm.Scopes.FirstOrDefault(s => s.Scope == definition.Scope) ?? vm.SelectedScope;
        vm.SelectedTarget = vm.Targets.FirstOrDefault(t => t.Id == definition.TargetId) ?? vm.SelectedTarget;

        vm.ReorderAdvanced = definition.ReorderAdvanced;
        vm.ReorderQuantityText = QuantityText(definition.ReorderQuantity);
        vm.MinQtyAdvanced = definition.MinQtyAdvanced;
        vm.MinOrderQtyText = QuantityText(definition.MinOrderQuantity);

        vm.PeriodCountText = definition.PeriodCount is { } pc
            ? pc.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
        if (definition.PeriodUnit is { } pu)
            vm.SelectedPeriodUnit = vm.PeriodUnits.FirstOrDefault(u => u.Unit == pu) ?? vm.SelectedPeriodUnit;
        if (definition.Criteria is { } cr)
            vm.SelectedCriteria = vm.Criteria.FirstOrDefault(c => c.Criteria == cr) ?? vm.SelectedCriteria;

        vm.OnPropertyChanged(nameof(IsAltering));
        vm.OnPropertyChanged(nameof(Caption));
        return vm;
    }

    /// <summary>
    /// Ctrl+A <b>alter</b>: re-validates exactly as <see cref="Create"/> does, then rewrites the definition this
    /// screen was opened over THROUGH ITS OWN ID via <see cref="ReorderLevelsService.Alter"/>. Any domain refusal
    /// is surfaced to <see cref="Message"/> without crashing the UI.
    /// </summary>
    public bool Alter()
    {
        Message = null;
        if (_editingId == Guid.Empty)
        {
            Message = "This screen is not altering an existing reorder level.";
            return false;
        }

        if (!TryReadForm(out var scope, out var target, out var reorderQty, out var minQty,
                out var periodCount, out var periodUnit, out var criteria))
            return false;

        ReorderDefinition altered;
        try
        {
            var service = new ReorderLevelsService(_company);
            altered = service.Alter(_editingId, scope.Scope, target.Id, ReorderAdvanced, reorderQty,
                MinQtyAdvanced, minQty, periodCount, periodUnit, criteria);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        // 🔴 The quantity boxes are deliberately NOT cleared here, unlike Create. The screen is still sitting over
        // the same definition; blanking the fields would make the form contradict the list row it just corrected
        // — and on THIS screen a blank box means "unset", so a cleared form reads as a definition with no figures.
        RefreshList();
        Message = $"Reorder level altered for {ScopeLabel(altered.Scope).ToLowerInvariant()} '{target.Display}'.";
        _onChanged();
        return true;
    }

    /// <summary>Renders a stored quantity back into the box it was typed in, exactly and invariantly — "100", not
    /// "100.000000" and not a culture's "100,0". Blank for an unset figure, which is how <see cref="Create"/>
    /// and <see cref="Alter"/> both read "no figure".</summary>
    private static string QuantityText(decimal? value) =>
        value is { } q ? q.ToString("0.######", CultureInfo.InvariantCulture) : string.Empty;

    /// <inheritdoc/>
    public IMasterListRow? HighlightedMasterRow => HighlightedRow;

    /// <inheritdoc/>
    public void ReloadExisting() => RefreshList();

    /// <inheritdoc/>
    /// <remarks>Engine-only — the shell saves and reloads after this returns.
    /// <para>🔴 <b>NO REFERENTIAL GUARD, AND THAT IS MEASURED RATHER THAN FORGOTTEN.</b> Nothing in the schema
    /// declares a foreign key into <c>reorder_definitions(id)</c> — it is a pure configuration row read by the
    /// Reorder Status report, which recomputes from the definitions that exist. There is no rule to enforce, and
    /// inventing a refusal with nothing behind it is the defect <c>MasterDeletionRules</c> names on the
    /// inventory-voucher arm.</para></remarks>
    public void DeleteMaster(Guid id) => new ReorderLevelsService(_company).Delete(id);

    private PayrollMasterHighlight<ReorderLevelListRow>? _highlight;

    private PayrollMasterHighlight<ReorderLevelListRow> Highlight =>
        _highlight ??= new PayrollMasterHighlight<ReorderLevelListRow>(
            Existing, () => OnPropertyChanged(nameof(HighlightedRow)));

    /// <summary>The arrow-highlighted existing reorder definition, or null.</summary>
    public ReorderLevelListRow? HighlightedRow => Highlight.Row;

    /// <inheritdoc/>
    public void MoveHighlight(int direction) => Highlight.Move(direction);

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Reorder Levels",
        new[]
        {
            MasterListColumn.Text("Scope"),
            MasterListColumn.Text("Target"),
            MasterListColumn.Text("Reorder Level"),
            MasterListColumn.Text("Min Order Qty"),
            MasterListColumn.Text("Period"),
            MasterListColumn.Text("Criteria"),
        },
        Existing.Select(r => (IReadOnlyList<string>)new[]
        {
            r.Scope, r.Target, r.Reorder, r.MinQty, r.Period, r.Criteria,
        }).ToList());

    /// <summary>The scope options (Item / Group / Category).</summary>
    public ObservableCollection<ReorderScopeOption> Scopes { get; } = new();

    /// <summary>The target options for the selected scope (the stock items / groups / categories).</summary>
    public ObservableCollection<ReorderTargetOption> Targets { get; } = new();

    /// <summary>The consumption-period unit options (Days / Weeks / Months / Years) — reused from the batch master.</summary>
    public ObservableCollection<ExpiryPeriodUnitOption> PeriodUnits { get; } = new();

    /// <summary>The Higher / Lower criterion options (Advanced reconciliation).</summary>
    public ObservableCollection<ReorderCriteriaOption> Criteria { get; } = new();

    /// <summary>The existing reorder definitions, refreshed after each create.</summary>
    public ObservableCollection<ReorderLevelListRow> Existing { get; } = new();

    [ObservableProperty] private ReorderScopeOption? _selectedScope;
    [ObservableProperty] private ReorderTargetOption? _selectedTarget;

    /// <summary>Alt+S: reorder level Simple (false) ⇄ Advanced (true).</summary>
    [ObservableProperty] private bool _reorderAdvanced;
    [ObservableProperty] private string _reorderQuantityText = string.Empty;

    /// <summary>Alt+V: min order qty Simple (false) ⇄ Advanced (true).</summary>
    [ObservableProperty] private bool _minQtyAdvanced;
    [ObservableProperty] private string _minOrderQtyText = string.Empty;

    [ObservableProperty] private string _periodCountText = string.Empty;
    [ObservableProperty] private ExpiryPeriodUnitOption? _selectedPeriodUnit;
    [ObservableProperty] private ReorderCriteriaOption? _selectedCriteria;

    [ObservableProperty] private string? _message;

    public ReorderLevelsViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        Scopes.Add(new ReorderScopeOption { Scope = ReorderScope.Item, Display = "Stock Item" });
        Scopes.Add(new ReorderScopeOption { Scope = ReorderScope.Group, Display = "Stock Group" });
        Scopes.Add(new ReorderScopeOption { Scope = ReorderScope.Category, Display = "Stock Category" });

        PeriodUnits.Add(new ExpiryPeriodUnitOption { Unit = ExpiryPeriodUnit.Days, Display = "Days" });
        PeriodUnits.Add(new ExpiryPeriodUnitOption { Unit = ExpiryPeriodUnit.Weeks, Display = "Weeks" });
        PeriodUnits.Add(new ExpiryPeriodUnitOption { Unit = ExpiryPeriodUnit.Months, Display = "Months" });
        PeriodUnits.Add(new ExpiryPeriodUnitOption { Unit = ExpiryPeriodUnit.Years, Display = "Years" });
        SelectedPeriodUnit = PeriodUnits.First(u => u.Unit == ExpiryPeriodUnit.Months);

        Criteria.Add(new ReorderCriteriaOption { Criteria = ReorderCriteria.Higher, Display = "Higher (order more)" });
        Criteria.Add(new ReorderCriteriaOption { Criteria = ReorderCriteria.Lower, Display = "Lower" });
        SelectedCriteria = Criteria.First();

        SelectedScope = Scopes.First();   // triggers RefreshTargets via OnSelectedScopeChanged
        RefreshList();
    }

    /// <summary>True when the selected scope has at least one target to define a level against.</summary>
    public bool CanCreate => Targets.Count > 0;

    /// <summary>True while either figure is Advanced — drives the visibility of the Period / Criteria fields.</summary>
    public bool IsAdvanced => ReorderAdvanced || MinQtyAdvanced;

    partial void OnSelectedScopeChanged(ReorderScopeOption? value) => RefreshTargets();
    partial void OnReorderAdvancedChanged(bool value) => OnPropertyChanged(nameof(IsAdvanced));
    partial void OnMinQtyAdvancedChanged(bool value) => OnPropertyChanged(nameof(IsAdvanced));

    /// <summary>Alt+S: flips the reorder level between Simple and Advanced.</summary>
    public void ToggleReorderAdvanced() => ReorderAdvanced = !ReorderAdvanced;

    /// <summary>Alt+V: flips the min order qty between Simple and Advanced.</summary>
    public void ToggleMinQtyAdvanced() => MinQtyAdvanced = !MinQtyAdvanced;

    /// <summary>
    /// Ctrl+A create/upsert: validates the target + quantities (6 dp) and — when either figure is Advanced — the
    /// consumption period (count &gt; 0) and criterion, then creates (or replaces) the definition via
    /// <see cref="ReorderLevelsService.CreateOrUpdate"/> and persists. Any domain error is surfaced to
    /// <see cref="Message"/> without crashing the UI. Blank quantity fields are left unset (null).
    /// </summary>
    public bool Create()
    {
        Message = null;

        if (!TryReadForm(out var scope, out var target, out var reorderQty, out var minQty,
                out var periodCount, out var periodUnit, out var criteria))
            return false;

        try
        {
            var service = new ReorderLevelsService(_company);
            service.CreateOrUpdate(scope.Scope, target.Id, ReorderAdvanced, reorderQty,
                MinQtyAdvanced, minQty, periodCount, periodUnit, criteria);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        RefreshList();
        Message = $"Reorder level saved for {scope.Display.ToLowerInvariant()} '{target.Display}'.";
        ReorderQuantityText = string.Empty;
        MinOrderQtyText = string.Empty;
        PeriodCountText = string.Empty;
        _onChanged();
        return true;
    }

    /// <summary>
    /// Reads and validates the whole form once — the scope, the target, both optional quantities and (only when
    /// either figure is Advanced) the consumption period and criterion. Returns false with
    /// <see cref="Message"/> set on the FIRST violation, leaving the company untouched.
    ///
    /// <para>🔴 <b>Shared by <see cref="Create"/> and <see cref="Alter"/> deliberately.</b> Two copies of this
    /// block is how one verb silently ends up validating differently from the other — and here the asymmetry
    /// would be a wrong-data path, since a blank quantity box means "unset" and an Alter that skipped the
    /// Advanced period check could write a definition the Reorder Status report cannot resolve.</para>
    /// </summary>
    private bool TryReadForm(
        out ReorderScopeOption scope,
        out ReorderTargetOption target,
        out decimal? reorderQty,
        out decimal? minQty,
        out int? periodCount,
        out ExpiryPeriodUnit? periodUnit,
        out ReorderCriteria? criteria)
    {
        scope = null!;
        target = null!;
        reorderQty = null;
        minQty = null;
        periodCount = null;
        periodUnit = null;
        criteria = null;

        if (SelectedScope is not { } pickedScope)
        {
            Message = "Pick a scope (Item / Group / Category).";
            return false;
        }
        scope = pickedScope;

        if (SelectedTarget is not { } pickedTarget)
        {
            Message = $"Pick a {scope.Display.ToLowerInvariant()} to define a reorder level for.";
            return false;
        }
        target = pickedTarget;

        if (!TryParseOptionalQuantity(ReorderQuantityText, "Reorder level", out reorderQty)) return false;
        if (!TryParseOptionalQuantity(MinOrderQtyText, "Minimum order quantity", out minQty)) return false;

        if (ReorderAdvanced || MinQtyAdvanced)
        {
            if (!int.TryParse((PeriodCountText ?? string.Empty).Trim(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var count) || count <= 0)
            {
                Message = "An Advanced figure needs a consumption period count (a whole number greater than zero).";
                return false;
            }
            periodCount = count;
            periodUnit = (SelectedPeriodUnit ?? PeriodUnits.First()).Unit;
            criteria = (SelectedCriteria ?? Criteria.First()).Criteria;
        }

        return true;
    }

    private bool TryParseOptionalQuantity(string? text, string label, out decimal? value)
    {
        value = null;
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0) return true;   // blank ⇒ unset
        if (!decimal.TryParse(trimmed,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var q))
        {
            Message = $"{label} must be a number.";
            return false;
        }
        if (q < 0m)
        {
            Message = $"{label} must be ≥ 0.";
            return false;
        }
        if (!Quantities.IsWithinPrecision(q))
        {
            Message = $"{label} {q} must be to {Quantities.DecimalPlaces} decimal places.";
            return false;
        }
        value = q;
        return true;
    }

    private void RefreshTargets()
    {
        var priorId = SelectedTarget?.Id;
        Targets.Clear();
        var scope = SelectedScope?.Scope ?? ReorderScope.Item;
        switch (scope)
        {
            case ReorderScope.Item:
                foreach (var i in _company.StockItems.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
                    Targets.Add(new ReorderTargetOption { Id = i.Id, Display = i.Name });
                break;
            case ReorderScope.Group:
                foreach (var g in _company.StockGroups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
                    Targets.Add(new ReorderTargetOption { Id = g.Id, Display = g.Name });
                break;
            case ReorderScope.Category:
                foreach (var c in _company.StockCategories.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
                    Targets.Add(new ReorderTargetOption { Id = c.Id, Display = c.Name });
                break;
        }
        SelectedTarget = Targets.FirstOrDefault(t => t.Id == priorId) ?? Targets.FirstOrDefault();
        OnPropertyChanged(nameof(CanCreate));
    }

    private void RefreshList()
    {
        RefreshTargets();
        // By ID, not by index — see PayrollMasterHighlight.RestoreTo.
        var previouslyHighlighted = Highlight.IdBeforeRebuild();

        Existing.Clear();
        foreach (var d in _company.ReorderDefinitions
                     .OrderBy(d => d.Scope)
                     .ThenBy(d => TargetName(d.Scope, d.TargetId), StringComparer.OrdinalIgnoreCase))
        {
            Existing.Add(new ReorderLevelListRow
            {
                MasterId = d.Id,
                Scope = ScopeLabel(d.Scope),
                Target = TargetName(d.Scope, d.TargetId),
                Reorder = FigureLabel(d.ReorderAdvanced, d.ReorderQuantity),
                MinQty = FigureLabel(d.MinQtyAdvanced, d.MinOrderQuantity),
                Period = d.PeriodCount is { } pc && d.PeriodUnit is { } pu
                    ? $"{pc} {pu}"
                    : "—",
                Criteria = d.Criteria is { } c ? c.ToString() : "—",
            });
        }

        Highlight.RestoreTo(previouslyHighlighted);
    }

    private static string ScopeLabel(ReorderScope scope) => scope switch
    {
        ReorderScope.Item => "Item",
        ReorderScope.Group => "Group",
        ReorderScope.Category => "Category",
        _ => scope.ToString(),
    };

    private static string FigureLabel(bool advanced, decimal? qty)
    {
        var baseText = qty is { } q ? IndianFormat.Quantity(q) : "—";
        return advanced ? $"{baseText} (Adv)" : baseText;
    }

    private string TargetName(ReorderScope scope, Guid targetId) => scope switch
    {
        ReorderScope.Item => _company.FindStockItem(targetId)?.Name ?? "—",
        ReorderScope.Group => _company.FindStockGroup(targetId)?.Name ?? "—",
        ReorderScope.Category => _company.FindStockCategory(targetId)?.Name ?? "—",
        _ => "—",
    };
}
