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
/// One editable line of the Attendance / Production voucher: an <see cref="Employee"/> + an
/// <see cref="AttendanceType"/> the value is recorded against + the value itself (attended/leave days, overtime
/// hours or produced units). Parsing/validation is deferred to the parent on Accept; the row only holds the typed
/// value and raises change notifications so the parent keeps a trailing blank row.
/// </summary>
public sealed partial class AttendanceVoucherLineRowViewModel : ViewModelBase
{
    private readonly Action _onChanged;

    /// <summary>The shared employee pool (same instance for every row).</summary>
    public ObservableCollection<Employee> Employees { get; }

    /// <summary>The shared attendance/production-type pool (same instance for every row).</summary>
    public ObservableCollection<AttendanceType> AttendanceTypes { get; }

    [ObservableProperty] private Employee? _selectedEmployee;
    [ObservableProperty] private AttendanceType? _selectedAttendanceType;
    [ObservableProperty] private string _valueText = string.Empty;

    public AttendanceVoucherLineRowViewModel(
        ObservableCollection<Employee> employees,
        ObservableCollection<AttendanceType> attendanceTypes,
        Action onChanged)
    {
        Employees = employees ?? throw new ArgumentNullException(nameof(employees));
        AttendanceTypes = attendanceTypes ?? throw new ArgumentNullException(nameof(attendanceTypes));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
    }

    partial void OnSelectedEmployeeChanged(Employee? value) => _onChanged();
    partial void OnSelectedAttendanceTypeChanged(AttendanceType? value) => _onChanged();
    partial void OnValueTextChanged(string value) => _onChanged();

    /// <summary>True while the row is wholly untouched; a blank trailing row is ignored on Accept.</summary>
    public bool IsBlank =>
        SelectedEmployee is null && SelectedAttendanceType is null && string.IsNullOrWhiteSpace(ValueText);
}

/// <summary>A recorded attendance entry shown in the read-back list on the voucher screen.</summary>
public sealed class AttendanceEntryRow
{
    public string Employee { get; init; } = string.Empty;
    public string AttendanceType { get; init; } = string.Empty;
    public string Period { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

/// <summary>
/// The <b>Attendance / Production voucher</b> entry screen (Transactions → Vouchers → Payroll → Attendance /
/// Production; Phase 8 slice 3; RQ-6). Records per-employee attendance / leave / production values for a period as
/// <see cref="AttendanceEntry"/> rows through the pure <see cref="PayrollAttendanceService"/> — the data of a
/// <b>non-accounting</b> voucher (it books no ledger entry). The salary-computation engine reads these entries
/// back to pro-rate On-Attendance heads and value On-Production heads.
///
/// <para>Keyboard-first: pick the period once, then add rows (employee · attendance type · value), Ctrl+A records
/// them. Gated by <see cref="Company.PayrollEnabled"/> (ER-13). MVVM boundary: engine + persistence only, no
/// Avalonia types ⇒ headlessly unit-testable. Every engine guard (unknown employee/type, negative value, ordered
/// dates) surfaces to <see cref="Message"/> without crashing the UI; a run is validated whole and recorded
/// all-or-nothing.</para>
/// </summary>
public sealed partial class AttendanceVoucherEntryViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;
    private bool _rebuilding;

    /// <summary>The employees a value can be recorded for.</summary>
    public ObservableCollection<Employee> Employees { get; } = new();

    /// <summary>The attendance / production types a value can be recorded against.</summary>
    public ObservableCollection<AttendanceType> AttendanceTypes { get; } = new();

    /// <summary>The editable lines (with a trailing blank row).</summary>
    public ObservableCollection<AttendanceVoucherLineRowViewModel> Rows { get; } = new();

    /// <summary>The already-recorded entries contained in the chosen period (read-back).</summary>
    public ObservableCollection<AttendanceEntryRow> RecentEntries { get; } = new();

    [ObservableProperty] private string _periodFromText = string.Empty;
    [ObservableProperty] private string _periodToText = string.Empty;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _lastAcceptSucceeded;

    public AttendanceVoucherEntryViewModel(Company company, CompanyStorage storage, Action? onChanged = null)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? (() => { });

        var (from, to) = DefaultPeriod(company);
        _periodFromText = from.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        _periodToText = to.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

        foreach (var e in _company.Employees.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            Employees.Add(e);
        foreach (var a in _company.AttendanceTypes.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
            AttendanceTypes.Add(a);

        AddBlankRow();
        RebuildRecentEntries();
    }

    /// <summary>True once at least one employee and one attendance type exist (else nothing can be recorded).</summary>
    public bool CanRecord => Employees.Count > 0 && AttendanceTypes.Count > 0;

    partial void OnPeriodFromTextChanged(string value) => RebuildRecentEntries();
    partial void OnPeriodToTextChanged(string value) => RebuildRecentEntries();

    /// <summary>
    /// Ctrl+A / the Record button: validates the period and every non-blank row (employee + type chosen, value a
    /// number ≥ 0), then records them all through <see cref="PayrollAttendanceService"/> and persists. Nothing is
    /// recorded unless the whole set validates (all-or-nothing). Returns true on success.
    /// </summary>
    public bool Accept()
    {
        Message = null;
        LastAcceptSucceeded = false;

        if (!_company.PayrollEnabled)
        {
            Message = "Enable Payroll (F11 → Maintain Payroll) before recording attendance.";
            return false;
        }
        if (!TryParseDate(PeriodFromText, out var from) || !TryParseDate(PeriodToText, out var to))
        {
            Message = "The period From/To must be valid dates (dd-MM-yyyy).";
            return false;
        }
        if (to < from)
        {
            Message = "The period end must be on or after its start.";
            return false;
        }

        var pending = new List<(Guid Employee, Guid Type, decimal Value)>();
        foreach (var row in Rows)
        {
            if (row.IsBlank) continue;
            if (row.SelectedEmployee is null) { Message = "Every attendance line needs an employee."; return false; }
            if (row.SelectedAttendanceType is null) { Message = $"Choose the attendance/production type for '{row.SelectedEmployee.Name}'."; return false; }
            if (!TryParseValue(row.ValueText, out var value))
            {
                Message = $"The value for '{row.SelectedEmployee.Name}' must be a number ≥ 0 (e.g. 26 days or 480 units).";
                return false;
            }
            pending.Add((row.SelectedEmployee.Id, row.SelectedAttendanceType.Id, value));
        }

        if (pending.Count == 0)
        {
            Message = "Add at least one attendance line (employee · type · value) before recording.";
            return false;
        }

        try
        {
            var service = new PayrollAttendanceService(_company);
            foreach (var (employee, type, value) in pending)
                service.Record(employee, type, from, to, value);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = $"Could not record attendance: {ex.Message}";
            return false;
        }

        LastAcceptSucceeded = true;
        _onChanged();
        var period = $"{from:dd-MM-yyyy} to {to:dd-MM-yyyy}";
        Message = $"Recorded {pending.Count} attendance/production {(pending.Count == 1 ? "entry" : "entries")} for {period}.";
        ResetRows();
        RebuildRecentEntries();
        return true;
    }

    /// <summary>Appends a fresh blank editable line (used by the +Add-Line button and to keep a trailing blank).</summary>
    public AttendanceVoucherLineRowViewModel AddBlankRow()
    {
        var row = new AttendanceVoucherLineRowViewModel(Employees, AttendanceTypes, OnRowChanged);
        Rows.Add(row);
        return row;
    }

    private void OnRowChanged()
    {
        if (_rebuilding) return;
        // Keep exactly one trailing blank row so the grid always offers a fresh line.
        if (Rows.Count == 0 || !Rows[^1].IsBlank)
            AddBlankRow();
    }

    private void ResetRows()
    {
        _rebuilding = true;
        Rows.Clear();
        _rebuilding = false;
        AddBlankRow();
    }

    private void RebuildRecentEntries()
    {
        RecentEntries.Clear();
        if (!TryParseDate(PeriodFromText, out var from) || !TryParseDate(PeriodToText, out var to) || to < from)
            return;

        var entries = _company.AttendanceEntries
            .Where(e => e.FromDate >= from && e.ToDate <= to)
            .OrderBy(e => _company.FindEmployee(e.EmployeeId)?.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.FromDate);
        foreach (var e in entries)
        {
            RecentEntries.Add(new AttendanceEntryRow
            {
                Employee = _company.FindEmployee(e.EmployeeId)?.Name ?? "(unknown)",
                AttendanceType = _company.FindAttendanceType(e.AttendanceTypeId)?.Name ?? "(unknown)",
                Period = $"{e.FromDate:dd-MM-yyyy} – {e.ToDate:dd-MM-yyyy}",
                Value = e.Value.ToString("0.####", CultureInfo.InvariantCulture),
            });
        }
    }

    private static (DateOnly From, DateOnly To) DefaultPeriod(Company company)
    {
        var from = company.FinancialYearStart;
        var to = from.AddMonths(1).AddDays(-1); // last day of the FY's first month
        return (from, to);
    }

    private static bool TryParseValue(string? text, out decimal value)
    {
        value = 0m;
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0) return false;
        if (!decimal.TryParse(t, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out value) || value < 0m)
            return false;
        return true;
    }

    private static bool TryParseDate(string? text, out DateOnly date)
    {
        date = default;
        var t = (text ?? string.Empty).Trim();
        return DateOnly.TryParseExact(t, new[] { "dd-MM-yyyy", "dd-MMM-yyyy", "yyyy-MM-dd" },
                   CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
               || DateOnly.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
