using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A unit-of-measure row for the existing-units list on the master screen.</summary>
public sealed class UnitListRow
{
    public string Symbol { get; init; } = string.Empty;
    public string FormalName { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
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
public sealed partial class UnitMasterViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

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
            Message = "Pick a first (base) unit and a tail unit (both existing simple units).";
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
                Symbol = u.Symbol,
                FormalName = u.FormalName,
                Kind = u.IsCompound ? "Compound" : "Simple",
                Detail = detail,
            });
        }
    }
}
