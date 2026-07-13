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

/// <summary>
/// One editable line of the Salary Details grid: a <see cref="PayHead"/> assignment with the per-employee value
/// its calculation type needs (a Flat-Rate / On-Attendance / On-Production head needs an amount; an
/// As-Computed-Value / As-User-Defined head must be left blank — the formula lives on the pay head, or the value
/// is entered at the voucher). Parsing/validation is deferred to the parent on Save; this row only holds the typed
/// value and raises change notifications so the parent keeps a trailing blank row.
/// </summary>
public sealed partial class SalaryStructureLineRowViewModel : ViewModelBase
{
    private readonly Action _onChanged;

    /// <summary>The shared pay-head pool (same instance for every row).</summary>
    public ObservableCollection<PayHeadPickerOption> PayHeadOptions { get; }

    [ObservableProperty] private PayHeadPickerOption? _selectedPayHead;
    [ObservableProperty] private string _amountText = string.Empty;

    public SalaryStructureLineRowViewModel(ObservableCollection<PayHeadPickerOption> pool, Action onChanged)
    {
        PayHeadOptions = pool ?? throw new ArgumentNullException(nameof(pool));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
    }

    partial void OnSelectedPayHeadChanged(PayHeadPickerOption? value)
    {
        OnPropertyChanged(nameof(RequiresAmount));
        OnPropertyChanged(nameof(AmountHint));
        _onChanged();
    }

    partial void OnAmountTextChanged(string value) => _onChanged();

    /// <summary>True once the row is wholly untouched; a blank trailing row is ignored on Save.</summary>
    public bool IsBlank => SelectedPayHead is null && string.IsNullOrWhiteSpace(AmountText);

    /// <summary>True ⇒ the chosen pay head's calculation type needs a per-employee amount on this line.</summary>
    public bool RequiresAmount => SelectedPayHead?.PayHead.CalculationType
        is PayHeadCalculationType.FlatRate
        or PayHeadCalculationType.OnAttendance
        or PayHeadCalculationType.OnProduction;

    /// <summary>A hint shown in the amount cell for heads that must be left blank.</summary>
    public string AmountHint => SelectedPayHead is null
        ? string.Empty
        : RequiresAmount ? string.Empty : "computed / entered at voucher";
}

/// <summary>A dated structure version shown in the append-only history list on the master screen.</summary>
public sealed class SalaryStructureVersionRow
{
    public string EffectiveFrom { get; init; } = string.Empty;
    public string StartType { get; init; } = string.Empty;
    public string Lines { get; init; } = string.Empty;
}

/// <summary>A Start-Type picker option (how a new structure is seeded).</summary>
public sealed class SalaryStartTypeOption
{
    public SalaryStructureStartType Value { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>A scope picker option — whether the structure is defined for an <see cref="Employee"/> or an
/// <see cref="EmployeeGroup"/> (RQ-5: a structure is definable at BOTH levels).</summary>
public sealed class SalaryScopeOption
{
    public SalaryStructureScope Value { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>
/// The <b>Salary Details</b> (salary structure) master ("Masters → Create → Payroll Masters → Salary Details";
/// Phase 8 slice 2; RQ-5; Study Guide pp.211–212) — a <b>dated</b> set of <see cref="PayHead"/> assignments
/// defined for either an <see cref="Employee"/> or an <see cref="EmployeeGroup"/> (the two RQ-5 scopes). Pick a
/// scope + target, an <b>effective-from</b> date and a Start Type, add ordered pay-head lines (each carrying the
/// value its calculation type needs), then Save — which <b>appends a dated version</b> via
/// <see cref="SalaryStructureService.DefineForEmployee"/> / <see cref="SalaryStructureService.DefineForGroup"/>
/// (a revision never overwrites; ER-4). Choosing <b>Copy From Parent Value</b> (the target's parent group
/// structure) or <b>Copy From Employee</b> (a source employee's structure) seeds the line grid from that source.
/// The screen lists the target's existing dated versions so a revision is visibly an append.
///
/// <para>Gated by <see cref="Company.PayrollEnabled"/> (ER-13). MVVM boundary: domain + persistence only, no
/// Avalonia types ⇒ headlessly unit-testable. Pre-validates the per-calc-type value rule before the engine and
/// surfaces every engine guard (duplicate effective-from, unknown reference, value-vs-calc-type mismatch) to
/// <see cref="Message"/> without crashing the UI.</para>
/// </summary>
public sealed partial class SalaryStructureMasterViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;
    private bool _initialized;
    private bool _suppressLineChange;

    /// <summary>The employees a structure can be defined for.</summary>
    public ObservableCollection<Employee> Employees { get; } = new();

    /// <summary>The employee groups a structure can be defined for (RQ-5 group-level scope).</summary>
    public ObservableCollection<EmployeeGroup> EmployeeGroups { get; } = new();

    /// <summary>The candidate source employees for a "Copy From Employee" start (every employee).</summary>
    public ObservableCollection<Employee> SourceEmployees { get; } = new();

    /// <summary>The shared pay-head pool every line's picker binds to.</summary>
    public ObservableCollection<PayHeadPickerOption> PayHeadOptions { get; } = new();

    /// <summary>The scope options (Employee / Employee Group).</summary>
    public ObservableCollection<SalaryScopeOption> Scopes { get; } = new();

    /// <summary>The Start-Type options (Start Afresh / Copy From Parent / Copy From Employee).</summary>
    public ObservableCollection<SalaryStartTypeOption> StartTypes { get; } = new();

    /// <summary>The editable pay-head lines; always one blank trailing row.</summary>
    public ObservableCollection<SalaryStructureLineRowViewModel> Lines { get; } = new();

    /// <summary>The existing dated versions for the chosen scope target — the append-only history (ER-4).</summary>
    public ObservableCollection<SalaryStructureVersionRow> History { get; } = new();

    [ObservableProperty] private SalaryScopeOption? _selectedScope;
    [ObservableProperty] private Employee? _selectedEmployee;
    [ObservableProperty] private EmployeeGroup? _selectedEmployeeGroup;
    [ObservableProperty] private Employee? _selectedSourceEmployee;
    [ObservableProperty] private string _effectiveFromText = string.Empty;
    [ObservableProperty] private SalaryStartTypeOption? _selectedStartType;
    [ObservableProperty] private string? _message;

    public SalaryStructureMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        foreach (var e in company.Employees.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            Employees.Add(e);
            SourceEmployees.Add(e);
        }
        foreach (var g in company.EmployeeGroups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            EmployeeGroups.Add(g);
        foreach (var ph in company.PayHeads.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            PayHeadOptions.Add(new PayHeadPickerOption { PayHead = ph, Display = ph.Name });

        Scopes.Add(new SalaryScopeOption { Value = SalaryStructureScope.Employee, Display = "Employee" });
        Scopes.Add(new SalaryScopeOption { Value = SalaryStructureScope.EmployeeGroup, Display = "Employee Group" });
        SelectedScope = Scopes.First();

        StartTypes.Add(new SalaryStartTypeOption { Value = SalaryStructureStartType.StartAfresh, Display = "Start Afresh" });
        StartTypes.Add(new SalaryStartTypeOption { Value = SalaryStructureStartType.CopyFromParent, Display = "Copy From Parent Value" });
        StartTypes.Add(new SalaryStartTypeOption { Value = SalaryStructureStartType.CopyFromEmployee, Display = "Copy From Employee" });
        SelectedStartType = StartTypes.First();

        SelectedEmployee = Employees.FirstOrDefault();
        SelectedEmployeeGroup = EmployeeGroups.FirstOrDefault();

        // Default the effective-from to books-begin (a sensible "as of the books" starting point).
        EffectiveFromText = company.BooksBeginFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        AddLineRow();          // one blank trailing row ready to type into
        _initialized = true;
        RefreshHistory();
    }

    /// <summary>True ⇒ the structure is scoped to a single employee (the default).</summary>
    public bool IsEmployeeScope => (SelectedScope?.Value ?? SalaryStructureScope.Employee) == SalaryStructureScope.Employee;

    /// <summary>True ⇒ the structure is scoped to an employee group.</summary>
    public bool IsGroupScope => !IsEmployeeScope;

    /// <summary>The label for the scope target picker ("Employee" / "Employee Group").</summary>
    public string ScopeTargetLabel => IsGroupScope ? "Employee Group" : "Employee";

    /// <summary>True ⇒ a "Copy From Employee" source picker is shown.</summary>
    public bool ShowCopyFromEmployeeSource =>
        SelectedStartType?.Value == SalaryStructureStartType.CopyFromEmployee;

    /// <summary>True once a target for the current scope exists and at least one pay head exists.</summary>
    public bool CanCreate => PayHeadOptions.Count > 0 &&
        (IsGroupScope ? EmployeeGroups.Count > 0 : Employees.Count > 0);

    partial void OnSelectedScopeChanged(SalaryScopeOption? value)
    {
        OnPropertyChanged(nameof(IsEmployeeScope));
        OnPropertyChanged(nameof(IsGroupScope));
        OnPropertyChanged(nameof(ScopeTargetLabel));
        OnPropertyChanged(nameof(CanCreate));
        if (!_initialized) return;
        Message = null;
        RefreshHistory();
        SeedLinesForStartType();
    }

    partial void OnSelectedEmployeeChanged(Employee? value)
    {
        if (!_initialized) return;
        RefreshHistory();
        SeedLinesForStartType();
    }

    partial void OnSelectedEmployeeGroupChanged(EmployeeGroup? value)
    {
        if (!_initialized) return;
        RefreshHistory();
        SeedLinesForStartType();
    }

    partial void OnSelectedStartTypeChanged(SalaryStartTypeOption? value)
    {
        OnPropertyChanged(nameof(ShowCopyFromEmployeeSource));
        if (!_initialized) return;
        SeedLinesForStartType();
    }

    partial void OnSelectedSourceEmployeeChanged(Employee? value)
    {
        if (!_initialized) return;
        SeedLinesForStartType();
    }

    /// <summary>Adds a blank line row; keeps exactly one trailing blank row.</summary>
    public SalaryStructureLineRowViewModel AddLineRow()
    {
        var row = new SalaryStructureLineRowViewModel(PayHeadOptions, OnLineChanged);
        Lines.Add(row);
        return row;
    }

    private void OnLineChanged()
    {
        if (_suppressLineChange) return;
        if (Lines.Count == 0 || !Lines[^1].IsBlank) AddLineRow();
    }

    /// <summary>
    /// Ctrl+A save: parses the effective-from date and the non-blank pay-head lines (each value pre-validated
    /// against its pay head's calculation type), then appends a dated version via
    /// <see cref="SalaryStructureService.DefineForEmployee"/> (append-only; ER-4). The engine re-validates the
    /// references, dense ordering and per-calc-type rule, and rejects a duplicate effective-from; any error is
    /// surfaced to <see cref="Message"/> without crashing the UI, and nothing is persisted on failure.
    /// </summary>
    public bool Save()
    {
        Message = null;

        if (IsGroupScope)
        {
            if (SelectedEmployeeGroup is null)
            {
                Message = "Pick an employee group (create one first under Payroll Masters → Employee Group).";
                return false;
            }
        }
        else if (SelectedEmployee is null)
        {
            Message = "Pick an employee (create one first under Payroll Masters → Employee).";
            return false;
        }
        if (!TryParseDate(EffectiveFromText, out var effectiveFrom))
        {
            Message = "Enter a valid effective-from date (e.g. 2025-04-01).";
            return false;
        }

        var lines = new List<SalaryStructureLine>();
        var order = 0;
        foreach (var row in Lines.Where(r => !r.IsBlank))
        {
            if (row.SelectedPayHead is not { } option)
            {
                Message = "Each salary line needs a pay head.";
                return false;
            }
            var payHead = option.PayHead;
            var needsAmount = row.RequiresAmount;

            if (needsAmount)
            {
                if (!TryParseDecimal(row.AmountText, out var amount) || amount < 0m)
                {
                    Message = $"Pay head '{payHead.Name}' ({DescribeCalcType(payHead.CalculationType)}) needs a non-negative amount.";
                    return false;
                }
                lines.Add(new SalaryStructureLine(payHead.Id, order, new Money(amount)));
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(row.AmountText))
                {
                    Message = $"Pay head '{payHead.Name}' is {DescribeCalcType(payHead.CalculationType)} — leave its amount blank (it is computed / entered at the voucher).";
                    return false;
                }
                lines.Add(new SalaryStructureLine(payHead.Id, order, null));
            }
            order++;
        }

        if (lines.Count == 0)
        {
            Message = "Add at least one pay-head line to the salary structure.";
            return false;
        }

        var startType = (SelectedStartType ?? StartTypes.First()).Value;
        string targetName;
        try
        {
            var service = new SalaryStructureService(_company);
            if (IsGroupScope)
            {
                service.DefineForGroup(SelectedEmployeeGroup!.Id, effectiveFrom, lines, startType);
                targetName = SelectedEmployeeGroup.Name;
            }
            else
            {
                service.DefineForEmployee(SelectedEmployee!.Id, effectiveFrom, lines, startType);
                targetName = SelectedEmployee.Name;
            }
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        RefreshHistory();
        Message = $"Salary details for '{targetName}' saved (effective from {effectiveFrom:yyyy-MM-dd}).";

        // Reset the line grid for the next entry (keep the employee + start type so a quick revision is easy).
        Lines.Clear();
        AddLineRow();
        _onChanged();
        return true;
    }

    /// <summary>Rebuilds the append-only history for the chosen scope target, newest first (ER-4).</summary>
    private void RefreshHistory()
    {
        History.Clear();

        SalaryStructureScope scope;
        Guid scopeId;
        if (IsGroupScope)
        {
            if (SelectedEmployeeGroup is null) return;
            scope = SalaryStructureScope.EmployeeGroup;
            scopeId = SelectedEmployeeGroup.Id;
        }
        else
        {
            if (SelectedEmployee is null) return;
            scope = SalaryStructureScope.Employee;
            scopeId = SelectedEmployee.Id;
        }

        foreach (var s in _company.SalaryStructures
                     .Where(s => s.Scope == scope && s.ScopeId == scopeId)
                     .OrderByDescending(s => s.EffectiveFrom))
        {
            var summary = string.Join("   ", s.Lines.Select(FormatLine));
            History.Add(new SalaryStructureVersionRow
            {
                EffectiveFrom = s.EffectiveFrom.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
                StartType = DescribeStartType(s.StartType),
                Lines = summary,
            });
        }
    }

    /// <summary>
    /// Seeds the line grid from the source implied by the chosen Start Type (RQ-5; Study Guide pp.211–212):
    /// <b>Copy From Parent Value</b> copies the target's parent-group structure (the employee's group, or a
    /// group's parent group), <b>Copy From Employee</b> copies the picked source employee's structure, resolved
    /// as of the effective-from date. <b>Start Afresh</b> leaves the grid untouched. When no source structure is
    /// found the grid is left as-is with a friendly hint; the copied lines are fully editable before Save.
    /// </summary>
    private void SeedLinesForStartType()
    {
        var start = (SelectedStartType ?? StartTypes.First()).Value;
        if (start == SalaryStructureStartType.StartAfresh) return;

        var source = ResolveCopySource(start);
        if (source is null)
        {
            Message = start == SalaryStructureStartType.CopyFromParent
                ? "No parent-group salary structure to copy yet — define a group-level structure first, or start afresh."
                : "Pick a source employee that already has a salary structure to copy from, or start afresh.";
            return;
        }

        LoadLinesFrom(source);
        Message = $"Copied {source.Lines.Count} line(s) from the " +
                  (start == SalaryStructureStartType.CopyFromParent ? "parent group" : "source employee") +
                  " structure — edit as needed, then Save.";
    }

    /// <summary>Resolves the salary structure to copy for the chosen Start Type, honoring the effective-from date
    /// (the latest version on/before it; the latest overall when the date is not yet valid).</summary>
    private SalaryStructure? ResolveCopySource(SalaryStructureStartType start)
    {
        var asOf = TryParseDate(EffectiveFromText, out var d) ? d : DateOnly.MaxValue;
        var svc = new SalaryStructureService(_company);

        if (start == SalaryStructureStartType.CopyFromEmployee)
            return SelectedSourceEmployee is { } src
                ? svc.InForceOn(SalaryStructureScope.Employee, src.Id, asOf)
                : null;

        // Copy From Parent Value: the parent group's structure.
        Guid? parentGroupId = IsGroupScope
            ? SelectedEmployeeGroup?.ParentId
            : SelectedEmployee?.EmployeeGroupId;
        return parentGroupId is { } gid
            ? svc.InForceOn(SalaryStructureScope.EmployeeGroup, gid, asOf)
            : null;
    }

    /// <summary>Replaces the line grid with rows built from <paramref name="source"/>'s lines (pay head +
    /// amount), keeping exactly one trailing blank row. A line whose pay head no longer exists is skipped.</summary>
    private void LoadLinesFrom(SalaryStructure source)
    {
        _suppressLineChange = true;
        Lines.Clear();
        foreach (var line in source.Lines.OrderBy(l => l.Order))
        {
            var option = PayHeadOptions.FirstOrDefault(o => o.PayHead.Id == line.PayHeadId);
            if (option is null) continue;
            var row = AddLineRow();
            row.SelectedPayHead = option;
            if (line.Amount is { } amt)
                row.AmountText = amt.Amount.ToString(CultureInfo.InvariantCulture);
        }
        _suppressLineChange = false;
        if (Lines.Count == 0 || !Lines[^1].IsBlank) AddLineRow();
    }

    private string FormatLine(SalaryStructureLine line)
    {
        var name = _company.FindPayHead(line.PayHeadId)?.Name ?? "?";
        return line.Amount is { } amt ? $"{name} {IndianFormat.Amount(amt.Amount)}" : name;
    }

    private static string DescribeStartType(SalaryStructureStartType s) => s switch
    {
        SalaryStructureStartType.StartAfresh => "Afresh",
        SalaryStructureStartType.CopyFromParent => "From Parent",
        SalaryStructureStartType.CopyFromEmployee => "From Employee",
        _ => s.ToString(),
    };

    private static string DescribeCalcType(PayHeadCalculationType c) => c switch
    {
        PayHeadCalculationType.OnAttendance => "On Attendance",
        PayHeadCalculationType.FlatRate => "Flat Rate",
        PayHeadCalculationType.AsComputedValue => "As Computed Value",
        PayHeadCalculationType.OnProduction => "On Production",
        PayHeadCalculationType.AsUserDefinedValue => "As User-Defined Value",
        _ => c.ToString(),
    };

    private static bool TryParseDate(string? text, out DateOnly value)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
            return true;
        return DateOnly.TryParseExact(trimmed, new[] { "dd-MMM-yyyy", "dd/MM/yyyy", "yyyy-MM-dd" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private static bool TryParseDecimal(string? text, out decimal value)
        => decimal.TryParse(
            (text ?? string.Empty).Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out value);
}
