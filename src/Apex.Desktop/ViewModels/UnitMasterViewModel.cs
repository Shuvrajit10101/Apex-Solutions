using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A unit-of-measure row for the existing-units list on the master screen.
///
/// <para>🔴 <b>W28 C3 — THIS IS THE ROW CENSUS 3.5 NAMES.</b> "The list row type carries no Guid, so no row can
/// address a unit" was recorded against this exact class, and it is why the Unit master had Create only and why
/// <c>InventoryService.DeleteUnit</c> had zero callers. <see cref="IMasterListRow"/> ends it.</para>
/// </summary>
public sealed partial class UnitListRow : ObservableObject, IMasterListRow
{
    public string Symbol { get; init; } = string.Empty;
    public string FormalName { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;

    /// <inheritdoc/>
    public Guid MasterId { get; init; }

    /// <inheritdoc/>
    /// <remarks>The SYMBOL, not the formal name — "Nos", not "Numbers". The delete confirmation reads
    /// <i>Delete unit 'Nos'?</i>, and the symbol is what the operator sees on every voucher line, so it is what
    /// identifies the master to them.</remarks>
    public string MasterName => Symbol;

    /// <inheritdoc/>
    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>
/// The Unit-of-Measure creation master ("Masters → Create → Inventory Masters → Unit", catalog §9;
/// RQ-3/RQ-4). A <b>Simple/Compound</b> toggle switches the form:
/// <list type="bullet">
///   <item><b>Simple</b> — Symbol, Formal Name, optional UQC (GST placeholder), and Decimal places
///     (0–4); quantities of that unit round to those decimals.</item>
///   <item><b>Compound</b> — a First (base) simple unit × a Conversion factor + a Tail simple unit (e.g.
///     Dozen = 12 Nos). Both components come from existing simple units; base must differ from tail and the
///     factor must be &gt; 0.</item>
/// </list>
/// Creates via the <see cref="InventoryService"/> (unique symbol; compound components must be simple) and
/// persists. Pre-validates decimals 0–4 and factor &gt; 0 before calling the engine, and surfaces any engine
/// error to <see cref="Message"/> so nothing crashes the UI.
///
/// <para>MVVM boundary: references the domain + persistence but no Avalonia/UI types, so it is headlessly
/// unit-testable.</para>
/// </summary>
public sealed partial class UnitMasterViewModel : ViewModelBase, IMasterListExportSource, IMasterListScreen
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    // --------------------------------------------- W28 C3: alteration state (census 3.5)

    /// <summary>The id of the unit being ALTERED, or <see cref="Guid.Empty"/> in Create mode.</summary>
    private Guid _editingId = Guid.Empty;

    /// <inheritdoc/>
    public bool IsAltering => _editingId != Guid.Empty;

    /// <summary>The screen heading — it says which VERB is running, because the form is identical in both
    /// modes.</summary>
    public string Caption => IsAltering ? "Unit Alteration" : "Unit Creation";

    /// <summary>
    /// True while the Simple/Compound toggle and the compound composition fields must be treated as READ-ONLY —
    /// i.e. whenever this screen is altering.
    ///
    /// <para>🔴 <b>The composition of a compound unit is not alterable, and this flag is how the screen says so
    /// instead of pretending otherwise.</b> <c>Unit.FirstUnitId</c>, <c>TailUnitId</c> and the conversion
    /// numerator/denominator are get-only on the domain type, because the conversion is the arithmetic every
    /// quantity already posted in that unit was stored against: re-pointing a Dozen from 12 Nos to 10 Nos would
    /// silently restate every posted line. <c>InventoryService.AlterUnit</c> therefore changes symbol, formal
    /// name, UQC and decimals only, and the form must not offer fields it will discard.</para>
    /// </summary>
    public bool IsCompositionReadOnly => IsAltering;

    /// <summary>
    /// Opens this master in <b>Alter</b> mode over an existing unit — the same form, pre-filled. Returns
    /// <c>null</c> if the id does not resolve.
    /// </summary>
    public static UnitMasterViewModel? ForAlter(
        Company company, CompanyStorage storage, Guid unitId, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (company.FindUnit(unitId) is not { } unit) return null;

        var vm = new UnitMasterViewModel(company, storage, onChanged);
        vm._editingId = unitId;
        vm.LoadFrom(unit);
        vm.OnPropertyChanged(nameof(IsAltering));
        vm.OnPropertyChanged(nameof(Caption));
        vm.OnPropertyChanged(nameof(IsCompositionReadOnly));
        return vm;
    }

    /// <summary>Loads an existing unit's values into the form. The Simple/Compound toggle is set from the unit
    /// so the correct half of the form is shown, and the compound components are loaded for DISPLAY — they are
    /// not writable (see <see cref="IsCompositionReadOnly"/>).</summary>
    public void LoadFrom(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        IsCompound = unit.IsCompound;
        Symbol = unit.Symbol;
        FormalName = unit.FormalName;
        UnitQuantityCode = unit.UnitQuantityCode ?? string.Empty;
        DecimalPlacesText = unit.DecimalPlaces.ToString(CultureInfo.InvariantCulture);

        if (unit.IsCompound)
        {
            FirstUnit = unit.FirstUnitId is { } fid ? _company.FindUnit(fid) : null;
            TailUnit = unit.TailUnitId is { } tid ? _company.FindUnit(tid) : null;
            ConversionFactorText = (unit.ConversionNumerator ?? 0).ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Ctrl+A <b>alter</b>: renames the unit and rewrites its formal name, and — for a SIMPLE unit only — its UQC
    /// and decimal places, via <see cref="InventoryService.AlterUnit"/>.
    ///
    /// <para>The decimals field is pre-validated here exactly as <c>CreateSimple</c> pre-validates it, so an
    /// operator who types "7" gets the form's own sentence rather than the engine's. On a COMPOUND unit the
    /// parsed value is discarded by the engine, so a malformed one is not worth refusing over — the field is not
    /// part of that shape at all.</para>
    /// </summary>
    public bool Alter()
    {
        Message = null;
        if (_editingId == Guid.Empty)
        {
            Message = "This screen is not altering an existing unit.";
            return false;
        }

        var isCompoundUnit = _company.FindUnit(_editingId)?.IsCompound ?? false;
        var decimals = 0;
        if (!isCompoundUnit
            && (!int.TryParse((DecimalPlacesText ?? string.Empty).Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out decimals) || decimals is < 0 or > 4))
        {
            Message = "Decimal places must be a whole number between 0 and 4.";
            return false;
        }

        var uqc = string.IsNullOrWhiteSpace(UnitQuantityCode) ? null : UnitQuantityCode.Trim();
        try
        {
            var altered = new InventoryService(_company)
                .AlterUnit(_editingId, Symbol, FormalName, decimals, uqc);
            _storage.Save(_company);
            Message = $"Unit '{altered.Symbol}' ({altered.FormalName}) altered.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        RefreshSimpleUnits();
        RefreshList();
        _onChanged();
        return true;
    }

    // ------------------------------------------- W28 C2: the shared Alt+D arm (census 3.5)

    /// <inheritdoc/>
    public string MasterKindLabel => "unit";

    /// <inheritdoc/>
    public IMasterListRow? HighlightedMasterRow => HighlightedRow;

    /// <inheritdoc/>
    /// <remarks>Refreshes the SIMPLE-UNIT pool too: a deleted unit must not stay on offer as a compound unit's
    /// first or tail component.</remarks>
    public void ReloadExisting()
    {
        RefreshSimpleUnits();
        RefreshList();
    }

    /// <inheritdoc/>
    /// <remarks>Engine-only — the shell saves and reloads after this returns. The refusal is
    /// <c>MasterDeletionRules.EnsureUnitDeletable</c>'s, and it is materially wider than the one
    /// <c>InventoryService.DeleteUnit</c> carried before wave 28: it now counts the alternate-unit column and
    /// both line-level unit columns, any of which would otherwise have made the open company unsavable.</remarks>
    public void DeleteMaster(Guid id) => new InventoryService(_company).DeleteUnit(id);

    // ------------------------------------------------- keyboard selection over the existing list

    private PayrollMasterHighlight<UnitListRow>? _highlight;

    private PayrollMasterHighlight<UnitListRow> Highlight =>
        _highlight ??= new PayrollMasterHighlight<UnitListRow>(
            Existing, () => OnPropertyChanged(nameof(HighlightedRow)));

    /// <summary>The highlighted existing-unit row, or <c>null</c>. Ctrl+Enter opens Unit Alteration; Alt+D
    /// deletes it.</summary>
    public UnitListRow? HighlightedRow => Highlight.Row;

    /// <inheritdoc/>
    public void MoveHighlight(int direction) => Highlight.Move(direction);

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Units",
        new[]
        {
            MasterListColumn.Text("Symbol"), MasterListColumn.Text("Formal Name"),
            MasterListColumn.Text("Kind"), MasterListColumn.Text("Detail"),
        },
        Existing.Select(r => (IReadOnlyList<string>)new[] { r.Symbol, r.FormalName, r.Kind, r.Detail }).ToList());

    /// <summary>The existing simple units — the pool a compound unit's first/tail can be built from.</summary>
    public ObservableCollection<Unit> SimpleUnits { get; } = new();

    /// <summary>The existing units, refreshed after each create.</summary>
    public ObservableCollection<UnitListRow> Existing { get; } = new();

    /// <summary>True ⇒ the Compound form is shown; false ⇒ the Simple form (the default).</summary>
    [ObservableProperty] private bool _isCompound;

    // ---- Simple form ----
    [ObservableProperty] private string _symbol = string.Empty;
    [ObservableProperty] private string _formalName = string.Empty;
    [ObservableProperty] private string _unitQuantityCode = string.Empty;
    [ObservableProperty] private string _decimalPlacesText = "0";

    // ---- Compound form ----
    [ObservableProperty] private Unit? _firstUnit;
    [ObservableProperty] private Unit? _tailUnit;
    [ObservableProperty] private string _conversionFactorText = string.Empty;

    [ObservableProperty] private string? _message;

    public UnitMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        RefreshSimpleUnits();
        RefreshList();
    }

    /// <summary>True once at least two simple units exist — a compound unit needs a distinct first + tail.</summary>
    public bool CanBuildCompound => SimpleUnits.Count >= 2;

    /// <summary>The label shown on the toggle explaining what a compound unit needs.</summary>
    public bool ShowSimpleForm => !IsCompound;

    partial void OnIsCompoundChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowSimpleForm));
        Message = null;
    }

    /// <summary>
    /// Ctrl+A create: builds a Simple or Compound unit per the toggle. Simple pre-validates decimals 0–4;
    /// Compound pre-validates the factor is a whole number &gt; 0 and first ≠ tail. Delegates to the engine
    /// (unique symbol + simple-component checks) and persists; any domain error goes to <see cref="Message"/>.
    /// </summary>
    public bool Create()
    {
        Message = null;
        return IsCompound ? CreateCompound() : CreateSimple();
    }

    private bool CreateSimple()
    {
        var symbol = (Symbol ?? string.Empty).Trim();
        var formal = (FormalName ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(symbol))
        {
            Message = "A unit symbol is required (e.g. Nos).";
            return false;
        }
        if (string.IsNullOrWhiteSpace(formal))
        {
            Message = "A formal name is required (e.g. Numbers).";
            return false;
        }
        if (!int.TryParse((DecimalPlacesText ?? string.Empty).Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var decimals) || decimals is < 0 or > 4)
        {
            Message = "Decimal places must be a whole number between 0 and 4.";
            return false;
        }

        var uqc = string.IsNullOrWhiteSpace(UnitQuantityCode) ? null : UnitQuantityCode.Trim();

        try
        {
            var service = new InventoryService(_company);
            service.CreateSimpleUnit(symbol, formal, decimals, uqc);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        RefreshSimpleUnits();
        RefreshList();
        Message = $"Unit '{symbol}' ({formal}) created.";
        Symbol = string.Empty;
        FormalName = string.Empty;
        UnitQuantityCode = string.Empty;
        DecimalPlacesText = "0";
        _onChanged();
        return true;
    }

    private bool CreateCompound()
    {
        var symbol = (Symbol ?? string.Empty).Trim();
        var formal = (FormalName ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(symbol))
        {
            Message = "A unit symbol is required (e.g. Dozen).";
            return false;
        }
        if (string.IsNullOrWhiteSpace(formal))
        {
            Message = "A formal name is required (e.g. Dozens).";
            return false;
        }
        if (FirstUnit is null || TailUnit is null)
        {
            Message = "Pick a first (larger) unit and a tail (smaller, base-measure) unit — "
                    + "both existing simple units, e.g. 1 Doz = 12 Nos.";
            return false;
        }
        if (FirstUnit.Id == TailUnit.Id)
        {
            Message = "A compound unit's first and tail units must be different.";
            return false;
        }
        if (!int.TryParse((ConversionFactorText ?? string.Empty).Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var factor) || factor <= 0)
        {
            Message = "Conversion factor must be a whole number > 0 (e.g. 12 for a Dozen = 12 Nos).";
            return false;
        }

        try
        {
            var service = new InventoryService(_company);
            service.CreateCompoundUnit(symbol, formal, FirstUnit.Id, TailUnit.Id, factor);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        var tailSymbol = TailUnit.Symbol;
        RefreshSimpleUnits();
        RefreshList();
        Message = $"Compound unit '{symbol}' created (1 {symbol} = {factor} {tailSymbol}).";
        Symbol = string.Empty;
        FormalName = string.Empty;
        ConversionFactorText = string.Empty;
        _onChanged();
        return true;
    }

    private void RefreshSimpleUnits()
    {
        var firstId = FirstUnit?.Id;
        var tailId = TailUnit?.Id;
        SimpleUnits.Clear();
        foreach (var u in _company.Units
                     .Where(u => !u.IsCompound)
                     .OrderBy(u => u.Symbol, StringComparer.OrdinalIgnoreCase))
            SimpleUnits.Add(u);

        FirstUnit = SimpleUnits.FirstOrDefault(u => u.Id == firstId);
        TailUnit = SimpleUnits.FirstOrDefault(u => u.Id == tailId);
        OnPropertyChanged(nameof(CanBuildCompound));
    }

    private void RefreshList()
    {
        // By ID, not by index — see PayrollMasterHighlight.RestoreTo. An alter that renames a unit must not walk
        // the highlight onto the neighbour that the next Alt+D would then delete.
        var previouslyHighlighted = Highlight.IdBeforeRebuild();

        Existing.Clear();
        foreach (var u in _company.Units)
        {
            string detail;
            if (u.IsCompound)
            {
                var tail = u.TailUnitId is { } tid ? _company.FindUnit(tid)?.Symbol ?? "?" : "?";
                var factor = u.ConversionNumerator ?? 0;
                var denom = u.ConversionDenominator ?? 1;
                detail = denom == 1
                    ? $"1 {u.Symbol} = {factor} {tail}"
                    : $"{factor}/{denom} {tail} per {u.Symbol}";
            }
            else
            {
                detail = $"{u.DecimalPlaces} dp"
                    + (string.IsNullOrEmpty(u.UnitQuantityCode) ? string.Empty : $" · UQC {u.UnitQuantityCode}");
            }

            Existing.Add(new UnitListRow
            {
                MasterId = u.Id,
                Symbol = u.Symbol,
                FormalName = u.FormalName,
                Kind = u.IsCompound ? "Compound" : "Simple",
                Detail = detail,
            });
        }

        Highlight.RestoreTo(previouslyHighlighted);
    }
}
