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
using CommunityToolkit.Mvvm.Input;

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
    /// <summary>🔴 T0-12. The recorded entry's id — the handle the Remove button needs. Without it the row was a
    /// read-back string with no way back to the entry, which is why a wrongly-keyed attendance figure could not be
    /// undone anywhere in the product.</summary>
    public Guid Id { get; init; }

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
public sealed partial class AttendanceVoucherEntryViewModel : ViewModelBase, ISetsWorkingDate
{

    /// <summary>
    /// WI-5 (4c): the working-date field <b>F2</b> targets on this screen — the attendance period start. Assigning routes
    /// through the one shared day-first parser and echoes the canonical spelling.
    /// </summary>
    public string WorkingDateText
    {
        get => PeriodFromText;
        set => PeriodFromText = value;
    }

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
        _periodFromText = ApexDate.Format(from);
        _periodToText = ApexDate.Format(to);

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
    /// number ≥ 0, <b>and no duplicate period</b>), then records them all through
    /// <see cref="PayrollAttendanceService"/> and persists. Nothing is recorded unless the whole set validates
    /// (all-or-nothing). Returns true on success.
    ///
    /// <para>🔴 <b>A duplicate REFUSES THE WHOLE RUN; it does not skip the line.</b> Both because that is the
    /// all-or-nothing contract every other guard on this screen already keeps, and because skipping is the more
    /// dangerous of the two in payroll: a silently skipped line means an employee's attendance is <i>not</i>
    /// recorded while the operator reads "Recorded 4 entries" and believes the month is keyed — the same
    /// invisible-wrong-figure defect as T0-12, only under-paying instead of double-paying. A refusal costs one
    /// keystroke, names the offending line, and the Remove button below makes it recoverable.</para>
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

        // 🔴 T0-12 FOLLOW-ON — the duplicate refusal is PRE-VALIDATED here, beside every other row guard, because
        // it is the ONLY PayrollAttendanceService.Record guard this screen does not otherwise pre-check (unknown
        // employee/type, date order and negative value are all caught above or by the row loop, so none of them
        // could ever fire mid-batch). Left to the record loop below, the duplicate fired mid-batch: Record mutates
        // the open company row by row via AddAttendanceEntry, while _storage.Save runs only AFTER the loop. A
        // 5-row run whose 3rd row duplicated therefore left 3 entries in the in-memory company against 1 on disk,
        // with the read-back list still showing 1 — two entries neither saved nor displayed, which the next save
        // from ANY screen (the Remove button below included) silently committed. Measured before this guard:
        // pressing Remove on the one visible row took disk from 1 entry to 2 GHOST entries.
        var service = new PayrollAttendanceService(_company);
        var pending = new List<(Guid Employee, Guid Type, decimal Value)>();
        // The whole run shares ONE screen-level [from, to], so two pending lines collide exactly when employee ×
        // attendance type repeats. Checking each line against the COMPANY alone cannot see that collision: nothing
        // is added to the company until the record loop, so an earlier pending line is not there to be found yet.
        var claimedInThisRun = new HashSet<(Guid Employee, Guid Type)>();
        for (var i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            if (row.IsBlank) continue;
            var line = i + 1;   // the line number as the operator sees it in the grid, blanks included
            if (row.SelectedEmployee is null) { Message = "Every attendance line needs an employee."; return false; }
            if (row.SelectedAttendanceType is null) { Message = $"Choose the attendance/production type for '{row.SelectedEmployee.Name}'."; return false; }
            if (!TryParseValue(row.ValueText, out var value))
            {
                Message = $"The value for '{row.SelectedEmployee.Name}' must be a number ≥ 0 (e.g. 26 days or 480 units).";
                return false;
            }

            // (1) The line duplicates an entry ALREADY on record — the production caller FindExact's own doc
            // comment claims ("so the entry screen can warn before the operator commits a whole batch") and, until
            // now, did not have.
            if (service.FindExact(row.SelectedEmployee.Id, row.SelectedAttendanceType.Id, from, to) is { } existing)
            {
                Message = $"Line {line}: {row.SelectedAttendanceType.Name} for {row.SelectedEmployee.Name} over "
                    + $"{from:dd-MM-yyyy} to {to:dd-MM-yyyy} is already recorded as "
                    + $"{existing.Value.ToString("0.####", CultureInfo.InvariantCulture)}. Remove that entry from the "
                    + "list below if you meant to correct it, then record again. Nothing was recorded.";
                return false;
            }

            // (2) The line duplicates an EARLIER LINE OF THIS SAME RUN. Without this the run still threw
            // mid-batch, because case (1) cannot see a row that has not been recorded yet.
            if (!claimedInThisRun.Add((row.SelectedEmployee.Id, row.SelectedAttendanceType.Id)))
            {
                Message = $"Line {line}: {row.SelectedAttendanceType.Name} for {row.SelectedEmployee.Name} is on an "
                    + $"earlier line of this run as well, over the same {from:dd-MM-yyyy} to {to:dd-MM-yyyy}. The two "
                    + "values would be ADDED together, not replaced — keep one line. Nothing was recorded.";
                return false;
            }

            pending.Add((row.SelectedEmployee.Id, row.SelectedAttendanceType.Id, value));
        }

        if (pending.Count == 0)
        {
            Message = "Add at least one attendance line (employee · type · value) before recording.";
            return false;
        }

        var recorded = new List<AttendanceEntry>(pending.Count);
        try
        {
            foreach (var (employee, type, value) in pending)
                recorded.Add(service.Record(employee, type, from, to, value));
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // 🔴 ROLL THE PART-RUN BACK. The pre-validation above closes the duplicate window, but it is a SECOND
            // layer and not the only one: Record keeps every guard of its own and can still throw here for a
            // condition this screen cannot pre-check — an employee deleted from another screen behind a row's
            // cached Employee object, say — and _storage.Save throws after the loop has already mutated the
            // company. Either way the rows recorded so far sit in the in-memory company and NOT on disk, and the
            // read-back list is not rebuilt on this path, so they were invisible until some later save from any
            // screen committed them. Undoing them is what makes "Could not record attendance" true, and it is
            // exact: every guard in Record precedes its AddAttendanceEntry, so a throwing Record never half-added
            // and `recorded` holds precisely the entries this run put in the company.
            //
            // Note this deliberately does NOT also call RebuildRecentEntries(). Because the roll-back is complete,
            // the company is back to its pre-run state and the displayed list already matches it — a rebuild here
            // is a no-op that no test could redden, i.e. a dead guard. The staleness the failure used to cause is
            // fixed at the root instead of papered over at the view.
            //
            // Removal goes through the company rather than PayrollAttendanceService.Delete deliberately: Delete
            // THROWS when the entry is missing, and throwing out of a catch block would crash the screen instead
            // of reporting.
            foreach (var entry in recorded)
                _company.RemoveAttendanceEntry(entry);
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

    /// <summary>
    /// 🔴 <b>T0-12 — the correction route.</b> Removes one recorded attendance/production entry and persists. The
    /// engine has always had <see cref="PayrollAttendanceService.Delete"/>; it had <b>no caller in this layer</b>,
    /// so a wrongly-keyed attendance figure was permanent and the only "fix" available to an operator was to record
    /// it again — which added to it and paid twice. This button is also what makes the duplicate REFUSAL fair:
    /// delete, then re-record the right figure.
    /// </summary>
    [RelayCommand]
    private void RemoveEntry(AttendanceEntryRow? row)
    {
        Message = null;
        if (row is null) return;
        try
        {
            new PayrollAttendanceService(_company).Delete(row.Id);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = $"Could not remove the entry: {ex.Message}";
            return;
        }

        _onChanged();
        Message = $"Removed {row.AttendanceType} for {row.Employee} ({row.Period}).";
        RebuildRecentEntries();
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
                Id = e.Id,
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

    /// <summary>
    /// WI-5: delegates to the ONE app-wide day-first parser. This used to be a per-screen ladder that fell
    /// through to a bare InvariantCulture parse — i.e. the MM/dd misread — so "03/04/2024" silently read as
    /// 4-Mar instead of 3-Apr. The shared helper accepts the same day-first spellings on every screen.
    /// </summary>
    private static bool TryParseDate(string? text, out DateOnly date) => ApexDate.TryParse(text, out date);
}
