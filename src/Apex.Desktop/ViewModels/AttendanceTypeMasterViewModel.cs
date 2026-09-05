using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>An attendance/production-type row for the existing-types list on the master screen.</summary>
public sealed partial class AttendanceTypeListRow : ObservableObject, IPayrollMasterListRow
{
    public string Name { get; init; } = string.Empty;
    public string Under { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;

    /// <summary>The stable identity of the attendance type this row displays (census 7.16).</summary>
    public Guid MasterId { get; init; }

    string IMasterListRow.MasterName => Name;

    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>An attendance/production-type "kind" picker option (label + the enum value).</summary>
public sealed class AttendanceTypeKindOption
{
    public AttendanceTypeKind Value { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>
/// One entry in the "Under" parent picker for an attendance type: "Primary" (top-level) or any existing
/// attendance type. <see cref="Type"/> is null for the Primary option.
/// </summary>
public sealed class ParentAttendanceTypeOption
{
    public AttendanceType? Type { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsPrimary => Type is null;
}

/// <summary>
/// One entry in the payroll-unit picker for an attendance type: "None" or any existing payroll unit.
/// <see cref="Unit"/> is null for the None option.
/// </summary>
public sealed class AttendanceUnitOption
{
    public PayrollUnit? Unit { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsNone => Unit is null;
}

/// <summary>
/// The Attendance/Production-Type creation master ("Masters → Create → Payroll Masters → Attendance /
/// Production Type"; Phase 8 slice 1; RQ-3): a name, a <b>Type</b> (Attendance/Leave-with-Pay ·
/// Leave-without-Pay · Production · User-defined), an optional <b>Under</b> parent (Primary ⇒ top-level,
/// hierarchical), and an optional period/production <b>Unit</b> (a payroll unit — Days/Hrs for attendance, the
/// production unit for a Production type). Creates via the <see cref="PayrollService"/> (unique name + valid,
/// non-cyclic parent + existing unit) and persists.
///
/// <para>Only reachable when Payroll is enabled. MVVM boundary: references the domain + persistence but no
/// Avalonia/UI types, so it is headlessly unit-testable.</para>
/// </summary>
public sealed partial class AttendanceTypeMasterViewModel : ViewModelBase, IMasterListExportSource, IPayrollMasterList
{
    private PayrollMasterHighlight<AttendanceTypeListRow> _highlight = null!;

    /// <summary>The id of the type being ALTERED, or <see cref="Guid.Empty"/> in Create mode (7.16).</summary>
    private Guid _editingId = Guid.Empty;

    /// <inheritdoc/>
    public bool IsAltering => _editingId != Guid.Empty;

    /// <summary>The screen caption — the one visible signal telling the operator which verb Ctrl+A will run.</summary>
    public string Caption => IsAltering
        ? "Attendance / Production Type Alteration"
        : "Attendance / Production Type Creation";

    /// <inheritdoc/>
    public string MasterKindLabel => "attendance type";

    /// <inheritdoc/>
    public IMasterListRow? HighlightedMasterRow => _highlight.Row;

    /// <summary>The highlighted existing-type row, or <c>null</c>.</summary>
    public AttendanceTypeListRow? HighlightedRow => _highlight.Row;

    /// <inheritdoc/>
    public void MoveHighlight(int direction) => _highlight.Move(direction);

    /// <inheritdoc/>
    public void ReloadExisting() { RefreshParentOptions(); RefreshUnitOptions(); RefreshList(); }

    /// <inheritdoc/>
    public void DeleteMaster(Guid id) => new PayrollService(_company).DeleteAttendanceType(id);

    /// <summary>Opens this master in <b>Alter</b> mode over an existing type — the same form, pre-filled.
    /// Returns <c>null</c> if the id does not resolve.</summary>
    public static AttendanceTypeMasterViewModel? ForAlter(
        Company company, CompanyStorage storage, Guid typeId, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (company.FindAttendanceType(typeId) is not { } type) return null;

        var vm = new AttendanceTypeMasterViewModel(company, storage, onChanged);
        vm._editingId = typeId;
        vm.Name = type.Name;
        vm.SelectedKind = vm.Kinds.FirstOrDefault(k => k.Value == type.Kind) ?? vm.Kinds.First();
        // A type may not be its own parent — take it out of the picker rather than let the operator find out by
        // being refused. The engine's cycle guard still has the last word on deeper cycles.
        var self = vm.ParentOptions.FirstOrDefault(o => o.Type?.Id == typeId);
        if (self is not null) vm.ParentOptions.Remove(self);
        vm.SelectedParent = vm.ParentOptions.FirstOrDefault(o => o.Type?.Id == type.ParentId)
                            ?? vm.ParentOptions.FirstOrDefault();
        vm.SelectedUnit = vm.UnitOptions.FirstOrDefault(o => o.Unit?.Id == type.PayrollUnitId)
                          ?? vm.UnitOptions.FirstOrDefault();
        vm.OnPropertyChanged(nameof(IsAltering));
        vm.OnPropertyChanged(nameof(Caption));
        return vm;
    }

    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Attendance / Production Types",
        new[]
        {
            MasterListColumn.Text("Name"), MasterListColumn.Text("Under"),
            MasterListColumn.Text("Type"), MasterListColumn.Text("Unit"),
        },
        Existing.Select(r => (IReadOnlyList<string>)new[] { r.Name, r.Under, r.Kind, r.Unit }).ToList());

    /// <summary>The four attendance/production-type kinds.</summary>
    public ObservableCollection<AttendanceTypeKindOption> Kinds { get; } = new();

    /// <summary>The parent options: "Primary" plus every existing attendance type.</summary>
    public ObservableCollection<ParentAttendanceTypeOption> ParentOptions { get; } = new();

    /// <summary>The period/production-unit options: "None" plus every existing payroll unit.</summary>
    public ObservableCollection<AttendanceUnitOption> UnitOptions { get; } = new();

    /// <summary>The existing attendance types, refreshed after each create.</summary>
    public ObservableCollection<AttendanceTypeListRow> Existing { get; } = new();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private AttendanceTypeKindOption? _selectedKind;
    [ObservableProperty] private ParentAttendanceTypeOption? _selectedParent;
    [ObservableProperty] private AttendanceUnitOption? _selectedUnit;
    [ObservableProperty] private string? _message;

    public AttendanceTypeMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _highlight = new PayrollMasterHighlight<AttendanceTypeListRow>(
            Existing, () => { OnPropertyChanged(nameof(HighlightedRow)); OnPropertyChanged(nameof(HighlightedMasterRow)); });

        Kinds.Add(new AttendanceTypeKindOption { Value = AttendanceTypeKind.AttendancePaid, Display = "Attendance/Leave with Pay" });
        Kinds.Add(new AttendanceTypeKindOption { Value = AttendanceTypeKind.LeaveWithoutPay, Display = "Leave without Pay" });
        Kinds.Add(new AttendanceTypeKindOption { Value = AttendanceTypeKind.Production, Display = "Production" });
        Kinds.Add(new AttendanceTypeKindOption { Value = AttendanceTypeKind.UserDefined, Display = "User-defined" });
        SelectedKind = Kinds.First();

        RefreshParentOptions();
        RefreshUnitOptions();
        RefreshList();
    }

    /// <summary>
    /// Ctrl+A create: validates the name is non-empty and a kind is chosen, then creates the type under the
    /// chosen parent (Primary ⇒ top-level) with the chosen period/production unit via the engine and persists.
    /// The engine enforces uniqueness + a valid, non-cyclic parent + an existing unit; any domain error is
    /// surfaced to <see cref="Message"/> without crashing the UI.
    /// </summary>
    public bool Create()
    {
        Message = null;
        var name = (Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "An attendance / production type name is required.";
            return false;
        }
        if (SelectedKind is null)
        {
            Message = "Pick a type (Attendance / Leave without Pay / Production / User-defined).";
            return false;
        }

        var parentId = SelectedParent?.Type?.Id;
        var unitId = SelectedUnit?.Unit?.Id;

        // 7.16 — the SAME Ctrl+A runs the verb the screen is in (see the Caption the operator is reading).
        var altering = IsAltering;
        try
        {
            var service = new PayrollService(_company);
            if (altering)
                service.AlterAttendanceType(_editingId, name, SelectedKind.Value, parentId, unitId);
            else
                service.CreateAttendanceType(name, SelectedKind.Value, parentId, unitId);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        var underLabel = SelectedParent is { IsPrimary: false } p ? p.Type!.Name : "Primary";
        if (altering)
        {
            RefreshList();
            Message = $"Attendance type '{name}' altered (under {underLabel}).";
            _onChanged();
            return true;
        }

        RefreshParentOptions();
        RefreshList();
        Message = $"Attendance type '{name}' created under {underLabel}.";
        Name = string.Empty;
        SelectedKind = Kinds.First();
        SelectedUnit = UnitOptions.FirstOrDefault();
        _onChanged();
        return true;
    }

    private void RefreshParentOptions()
    {
        var previousId = SelectedParent?.Type?.Id;
        ParentOptions.Clear();
        ParentOptions.Add(new ParentAttendanceTypeOption { Type = null, Display = "◦ Primary (top-level)" });
        foreach (var a in _company.AttendanceTypes.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
            ParentOptions.Add(new ParentAttendanceTypeOption { Type = a, Display = a.Name });

        SelectedParent = ParentOptions.FirstOrDefault(o => o.Type?.Id == previousId)
                         ?? ParentOptions.FirstOrDefault();
    }

    private void RefreshUnitOptions()
    {
        var previousId = SelectedUnit?.Unit?.Id;
        UnitOptions.Clear();
        UnitOptions.Add(new AttendanceUnitOption { Unit = null, Display = "◦ None" });
        foreach (var u in _company.PayrollUnits.OrderBy(u => u.Symbol, StringComparer.OrdinalIgnoreCase))
            UnitOptions.Add(new AttendanceUnitOption { Unit = u, Display = u.Symbol });

        SelectedUnit = UnitOptions.FirstOrDefault(o => o.Unit?.Id == previousId)
                       ?? UnitOptions.FirstOrDefault();
    }

    private void RefreshList()
    {
        // By id, never by index — see PayrollMasterHighlight.RestoreTo for why.
        var previous = _highlight.IdBeforeRebuild();

        Existing.Clear();
        foreach (var a in _company.AttendanceTypes)
        {
            var under = a.ParentId is { } pid
                ? _company.FindAttendanceType(pid)?.Name ?? "—"
                : "Primary";
            var unit = a.PayrollUnitId is { } uid
                ? _company.FindPayrollUnit(uid)?.Symbol ?? "—"
                : "—";
            Existing.Add(new AttendanceTypeListRow
            {
                MasterId = a.Id,
                Name = a.Name,
                Under = under,
                Kind = DescribeKind(a.Kind),
                Unit = unit,
            });
        }

        _highlight.RestoreTo(previous);
    }

    private static string DescribeKind(AttendanceTypeKind kind) => kind switch
    {
        AttendanceTypeKind.AttendancePaid => "Attendance/Leave with Pay",
        AttendanceTypeKind.LeaveWithoutPay => "Leave without Pay",
        AttendanceTypeKind.Production => "Production",
        AttendanceTypeKind.UserDefined => "User-defined",
        _ => kind.ToString(),
    };
}
