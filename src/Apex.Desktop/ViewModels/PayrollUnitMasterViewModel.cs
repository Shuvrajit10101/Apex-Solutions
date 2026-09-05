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

/// <summary>A payroll-unit row for the existing-units list on the master screen.</summary>
public sealed partial class PayrollUnitListRow : ObservableObject, IPayrollMasterListRow
{
    public string Symbol { get; init; } = string.Empty;
    public string FormalName { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;

    /// <summary>The stable identity of the unit this row displays (census 7.16).</summary>
    public Guid MasterId { get; init; }

    /// <summary>A payroll unit is named by its SYMBOL — that is what the operator picks it by everywhere else,
    /// and the delete confirmation must name it the same way.</summary>
    string IPayrollMasterListRow.MasterName => Symbol;

    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>
/// The Payroll-Unit creation master ("Masters → Create → Payroll Masters → Payroll Unit"; Phase 8 slice 1;
/// RQ-3). A <b>Simple/Compound</b> toggle switches the form (mirrors <see cref="UnitMasterViewModel"/>):
/// <list type="bullet">
///   <item><b>Simple</b> — Symbol (e.g. Days / Hrs / Month), Formal Name, and Decimal places (0–4).</item>
///   <item><b>Compound</b> — a First (base) simple unit × a Conversion factor + a Tail simple unit (e.g.
///     "Hrs of 60 Min", "Month of 26 Days"). Both components come from existing simple payroll units; base
///     must differ from tail and the factor must be &gt; 0.</item>
/// </list>
/// Creates via the <see cref="PayrollService"/> (unique symbol; compound components must be simple) and
/// persists. Pre-validates decimals 0–4 and factor &gt; 0 before calling the engine, and surfaces any engine
/// error to <see cref="Message"/> so nothing crashes the UI.
///
/// <para>Only reachable when Payroll is enabled. MVVM boundary: references the domain + persistence but no
/// Avalonia/UI types, so it is headlessly unit-testable.</para>
/// </summary>
public sealed partial class PayrollUnitMasterViewModel : ViewModelBase, IMasterListExportSource, IPayrollMasterList
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;
    private PayrollMasterHighlight<PayrollUnitListRow> _highlight = null!;

    /// <summary>The id of the unit being ALTERED, or <see cref="Guid.Empty"/> in Create mode (7.16).</summary>
    private Guid _editingId = Guid.Empty;

    /// <inheritdoc/>
    public bool IsAltering => _editingId != Guid.Empty;

    /// <summary>The screen caption — the one visible signal telling the operator which verb Ctrl+A will run.</summary>
    public string Caption => IsAltering ? "Payroll Unit Alteration" : "Payroll Unit Creation";

    /// <inheritdoc/>
    public string MasterKindLabel => "payroll unit";

    /// <inheritdoc/>
    public IPayrollMasterListRow? HighlightedMasterRow => _highlight.Row;

    /// <summary>The highlighted existing-unit row, or <c>null</c>.</summary>
    public PayrollUnitListRow? HighlightedRow => _highlight.Row;

    /// <inheritdoc/>
    public void MoveHighlight(int direction) => _highlight.Move(direction);

    /// <inheritdoc/>
    public void ReloadExisting() { RefreshSimpleUnits(); RefreshList(); }

    /// <inheritdoc/>
    public void DeleteMaster(Guid id) => new PayrollService(_company).DeletePayrollUnit(id);

    /// <summary>
    /// Opens this master in <b>Alter</b> mode over an existing unit — the same form, pre-filled.
    ///
    /// <para>🔴 <b>A COMPOUND unit opens with its STRUCTURE READ-ONLY</b> (<see cref="CanAlterStructure"/> is
    /// false): first unit, tail unit and conversion factor are constructor-only on the domain type and are the
    /// arithmetic every attendance figure already recorded against this unit was converted with. Symbol and
    /// formal name are alterable; the structure is not. A compound unit that is wrong is deleted and re-created.
    /// Divergence, labelled as ours — and it is the safe half of the two.</para>
    /// </summary>
    public static PayrollUnitMasterViewModel? ForAlter(
        Company company, CompanyStorage storage, Guid unitId, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (company.FindPayrollUnit(unitId) is not { } unit) return null;

        var vm = new PayrollUnitMasterViewModel(company, storage, onChanged);
        vm._editingId = unitId;
        vm.IsCompound = unit.IsCompound;
        vm.Symbol = unit.Symbol;
        vm.FormalName = unit.FormalName;
        vm.DecimalPlacesText = unit.DecimalPlaces.ToString(CultureInfo.InvariantCulture);
        if (unit.IsCompound)
        {
            vm.FirstUnit = unit.FirstUnitId is { } fid ? company.FindPayrollUnit(fid) : null;
            vm.TailUnit = unit.TailUnitId is { } tid ? company.FindPayrollUnit(tid) : null;
            var num = unit.ConversionNumerator ?? 0;
            var den = unit.ConversionDenominator ?? 1;
            vm.ConversionFactorText = den == 1
                ? num.ToString(CultureInfo.InvariantCulture)
                : $"{num}/{den}";
        }
        vm.OnPropertyChanged(nameof(IsAltering));
        vm.OnPropertyChanged(nameof(Caption));
        vm.OnPropertyChanged(nameof(CanAlterStructure));
        return vm;
    }

    /// <summary>False while altering — the Simple/Compound toggle and the compound components are frozen, because
    /// changing them would silently re-scale every figure already recorded against this unit.</summary>
    public bool CanAlterStructure => !IsAltering;

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Payroll Units",
        new[]
        {
            MasterListColumn.Text("Symbol"), MasterListColumn.Text("Formal Name"),
            MasterListColumn.Text("Kind"), MasterListColumn.Text("Detail"),
        },
        Existing.Select(r => (IReadOnlyList<string>)new[] { r.Symbol, r.FormalName, r.Kind, r.Detail }).ToList());

    /// <summary>The existing simple payroll units — the pool a compound unit's first/tail can be built from.</summary>
    public ObservableCollection<PayrollUnit> SimpleUnits { get; } = new();

    /// <summary>The existing payroll units, refreshed after each create.</summary>
    public ObservableCollection<PayrollUnitListRow> Existing { get; } = new();

    /// <summary>True ⇒ the Compound form is shown; false ⇒ the Simple form (the default).</summary>
    [ObservableProperty] private bool _isCompound;

    // ---- Simple form ----
    [ObservableProperty] private string _symbol = string.Empty;
    [ObservableProperty] private string _formalName = string.Empty;
    [ObservableProperty] private string _decimalPlacesText = "0";

    // ---- Compound form ----
    [ObservableProperty] private PayrollUnit? _firstUnit;
    [ObservableProperty] private PayrollUnit? _tailUnit;
    [ObservableProperty] private string _conversionFactorText = string.Empty;

    [ObservableProperty] private string? _message;

    public PayrollUnitMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _highlight = new PayrollMasterHighlight<PayrollUnitListRow>(
            Existing, () => { OnPropertyChanged(nameof(HighlightedRow)); OnPropertyChanged(nameof(HighlightedMasterRow)); });

        RefreshSimpleUnits();
        RefreshList();
    }

    /// <summary>True once at least two simple units exist — a compound unit needs a distinct first + tail.</summary>
    public bool CanBuildCompound => SimpleUnits.Count >= 2;

    /// <summary>True ⇒ show the Simple form (the inverse of <see cref="IsCompound"/>).</summary>
    public bool ShowSimpleForm => !IsCompound;

    partial void OnIsCompoundChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowSimpleForm));
        Message = null;
    }

    /// <summary>
    /// Ctrl+A create: builds a Simple or Compound payroll unit per the toggle. Simple pre-validates decimals
    /// 0–4; Compound pre-validates the factor is a whole number &gt; 0 and first ≠ tail. Delegates to the
    /// engine (unique symbol + simple-component checks) and persists; any domain error goes to
    /// <see cref="Message"/>.
    /// </summary>
    public bool Create()
    {
        Message = null;
        // 7.16 — ALTERING always takes the simple path, compound or not: the only alterable fields are symbol,
        // formal name and decimals, and CreateCompound would try to build a SECOND unit from the frozen
        // components. Routing on IsCompound here would have done exactly that.
        if (IsAltering) return CreateSimple();
        return IsCompound ? CreateCompound() : CreateSimple();
    }

    private bool CreateSimple()
    {
        var symbol = (Symbol ?? string.Empty).Trim();
        var formal = (FormalName ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(symbol))
        {
            Message = "A payroll-unit symbol is required (e.g. Days).";
            return false;
        }
        if (string.IsNullOrWhiteSpace(formal))
        {
            Message = "A formal name is required (e.g. Days).";
            return false;
        }
        if (!int.TryParse((DecimalPlacesText ?? string.Empty).Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var decimals) || decimals is < 0 or > 4)
        {
            Message = "Decimal places must be a whole number between 0 and 4.";
            return false;
        }

        // 7.16 — the SAME Ctrl+A runs the verb the screen is in. Note the compound case routes here too when
        // altering: only symbol / formal name / decimals are alterable, so there is one alter path, not two.
        var altering = IsAltering;
        try
        {
            var service = new PayrollService(_company);
            if (altering)
                service.AlterPayrollUnit(_editingId, symbol, formal, decimals);
            else
                service.CreateSimplePayrollUnit(symbol, formal, decimals);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        RefreshSimpleUnits();
        RefreshList();
        if (altering)
        {
            Message = $"Payroll unit '{symbol}' ({formal}) altered.";
            _onChanged();
            return true;
        }

        Message = $"Payroll unit '{symbol}' ({formal}) created.";
        Symbol = string.Empty;
        FormalName = string.Empty;
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
            Message = "A payroll-unit symbol is required (e.g. Month of 26 Days).";
            return false;
        }
        if (string.IsNullOrWhiteSpace(formal))
        {
            Message = "A formal name is required.";
            return false;
        }
        if (FirstUnit is null || TailUnit is null)
        {
            Message = "Pick a first (base) unit and a tail unit (both existing simple payroll units).";
            return false;
        }
        if (FirstUnit.Id == TailUnit.Id)
        {
            Message = "A compound payroll unit's first and tail units must be different.";
            return false;
        }
        if (!int.TryParse((ConversionFactorText ?? string.Empty).Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var factor) || factor <= 0)
        {
            Message = "Conversion factor must be a whole number > 0 (e.g. 26 for a Month of 26 Days).";
            return false;
        }

        try
        {
            var service = new PayrollService(_company);
            service.CreateCompoundPayrollUnit(symbol, formal, FirstUnit.Id, TailUnit.Id, factor);
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
        Message = $"Compound payroll unit '{symbol}' created (1 {symbol} = {factor} {tailSymbol}).";
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
        foreach (var u in _company.PayrollUnits
                     .Where(u => !u.IsCompound)
                     .OrderBy(u => u.Symbol, StringComparer.OrdinalIgnoreCase))
            SimpleUnits.Add(u);

        FirstUnit = SimpleUnits.FirstOrDefault(u => u.Id == firstId);
        TailUnit = SimpleUnits.FirstOrDefault(u => u.Id == tailId);
        OnPropertyChanged(nameof(CanBuildCompound));
    }

    private void RefreshList()
    {
        // By id, never by index — see PayrollMasterHighlight.RestoreTo for why.
        var previous = _highlight.IdBeforeRebuild();

        Existing.Clear();
        foreach (var u in _company.PayrollUnits)
        {
            string detail;
            if (u.IsCompound)
            {
                var tail = u.TailUnitId is { } tid ? _company.FindPayrollUnit(tid)?.Symbol ?? "?" : "?";
                var factor = u.ConversionNumerator ?? 0;
                var denom = u.ConversionDenominator ?? 1;
                detail = denom == 1
                    ? $"1 {u.Symbol} = {factor} {tail}"
                    : $"{factor}/{denom} {tail} per {u.Symbol}";
            }
            else
            {
                detail = $"{u.DecimalPlaces} dp";
            }

            Existing.Add(new PayrollUnitListRow
            {
                MasterId = u.Id,
                Symbol = u.Symbol,
                FormalName = u.FormalName,
                Kind = u.IsCompound ? "Compound" : "Simple",
                Detail = detail,
            });
        }

        _highlight.RestoreTo(previous);
    }
}
