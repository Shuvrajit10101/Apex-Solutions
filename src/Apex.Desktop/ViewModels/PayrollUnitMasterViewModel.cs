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
public sealed class PayrollUnitListRow
{
    public string Symbol { get; init; } = string.Empty;
    public string FormalName { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
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
public sealed partial class PayrollUnitMasterViewModel : ViewModelBase, IMasterListExportSource
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

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

        try
        {
            var service = new PayrollService(_company);
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
                Symbol = u.Symbol,
                FormalName = u.FormalName,
                Kind = u.IsCompound ? "Compound" : "Simple",
                Detail = detail,
            });
        }
    }
}
