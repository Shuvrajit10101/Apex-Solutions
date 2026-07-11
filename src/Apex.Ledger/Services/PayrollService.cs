using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The payroll-masters + F11-enable service (Phase 8 slice 1; RQ-1/RQ-2/RQ-3). Mirrors
/// <see cref="InventoryService"/> (masters + guards) and <see cref="GstService"/>/<c>TdsTcsService</c>
/// (idempotent F11 enable). Framework-, DB-, clock- and RNG-free: pure, deterministic mutation over the
/// <see cref="Company"/> aggregate. Responsibilities in this slice:
/// <list type="bullet">
///   <item><see cref="EnablePayroll"/> / <see cref="DisablePayroll"/> — the F11 "Maintain Payroll" +
///     "Enable Payroll Statutory" toggles, idempotent, disabled by default (ER-13 byte-identical when off).</item>
///   <item>Create/Alter/Delete the payroll masters — Employee Category, Employee Group (hierarchical),
///     Employee, Payroll Unit (simple + compound), Attendance/Production Type (hierarchical) — enforcing the
///     same discipline the accounting/inventory masters ship with: names unique within a company; a parent must
///     exist and not form a cycle; a master is delete-blocked while referenced; PAN/UAN/ESI are structurally
///     validated at the master-save boundary.</item>
/// </list>
/// <b>No PF/ESI/§192 computation ships in this slice</b> — this is masters + enable flags only. The service
/// throws <see cref="InvalidOperationException"/> on any violation (never mutating the company).
/// </summary>
public sealed class PayrollService
{
    private readonly Company _company;

    public PayrollService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    // ------------------------------------------------------------------ F11 enable (idempotent)

    /// <summary>
    /// Enables Payroll on the company (F11 "Maintain Payroll = Yes"), <b>idempotently</b>. When
    /// <paramref name="enableStatutory"/> is true it also turns on "Enable Payroll Statutory" (which surfaces the
    /// PF/ESI/NPS/IT statutory-code screen in later slices). Disabled by default, so a company that never enables
    /// Payroll serialises byte-identically to a pre-v30 company (ER-13).
    /// </summary>
    public void EnablePayroll(bool enableStatutory = true)
    {
        _company.PayrollEnabled = true;
        if (enableStatutory) _company.PayrollStatutoryEnabled = true;
    }

    /// <summary>Turns Payroll (and, implicitly, Payroll Statutory) off. Idempotent; leaves any masters in place
    /// (turning the feature off merely hides the payroll UI — it never deletes data).</summary>
    public void DisablePayroll()
    {
        _company.PayrollEnabled = false;
        _company.PayrollStatutoryEnabled = false;
    }

    // ------------------------------------------------------------------ Employee categories

    /// <summary>Creates an employee category; name unique within the company, allocating revenue and/or
    /// non-revenue cost items (at least one must be Yes — mirror of <see cref="CostCategory"/>, RQ-2).</summary>
    public EmployeeCategory CreateEmployeeCategory(
        string name,
        bool allocateRevenueItems = true,
        bool allocateNonRevenueItems = false)
    {
        var trimmed = RequireName(name, "employee category");
        if (!allocateRevenueItems && !allocateNonRevenueItems)
            throw new InvalidOperationException(
                "An employee category must allocate revenue and/or non-revenue items (at least one must be Yes).");
        if (_company.FindEmployeeCategoryByName(trimmed) is not null)
            throw new InvalidOperationException($"An employee category named '{trimmed}' already exists.");

        var category = new EmployeeCategory(Guid.NewGuid(), trimmed, allocateRevenueItems, allocateNonRevenueItems);
        _company.AddEmployeeCategory(category);
        return category;
    }

    /// <summary>Deletes an employee category, blocked while it is predefined or referenced by any employee.</summary>
    public void DeleteEmployeeCategory(Guid categoryId)
    {
        var category = _company.FindEmployeeCategory(categoryId)
            ?? throw new InvalidOperationException($"Employee category {categoryId} not found.");
        if (category.IsPredefined)
            throw new InvalidOperationException($"Predefined employee category '{category.Name}' cannot be deleted.");
        if (_company.Employees.Any(e => e.EmployeeCategoryId == categoryId))
            throw new InvalidOperationException($"Employee category '{category.Name}' is used by employees and cannot be deleted.");
        _company.RemoveEmployeeCategory(category);
    }

    // ------------------------------------------------------------------ Employee groups

    /// <summary>Creates an employee group; name unique, parent (if any) must exist and not cycle.</summary>
    public EmployeeGroup CreateEmployeeGroup(
        string name,
        Guid? parentId = null,
        string? alias = null,
        bool defineSalaryDetails = false)
    {
        var trimmed = RequireName(name, "employee group");
        if (_company.FindEmployeeGroupByName(trimmed) is not null)
            throw new InvalidOperationException($"An employee group named '{trimmed}' already exists.");

        var group = new EmployeeGroup(Guid.NewGuid(), trimmed, parentId, alias, defineSalaryDetails);
        EnsureEmployeeGroupParentValid(group);
        _company.AddEmployeeGroup(group);
        return group;
    }

    /// <summary>Re-parents an employee group, rejecting a move that would create a cycle.</summary>
    public void SetEmployeeGroupParent(Guid groupId, Guid? parentId)
    {
        var group = _company.FindEmployeeGroup(groupId)
            ?? throw new InvalidOperationException($"Employee group {groupId} not found.");
        var previous = group.ParentId;
        group.ParentId = parentId;
        try { EnsureEmployeeGroupParentValid(group); }
        catch { group.ParentId = previous; throw; }
    }

    /// <summary>Deletes an employee group, blocked while it has child groups or employees under it.</summary>
    public void DeleteEmployeeGroup(Guid groupId)
    {
        var group = _company.FindEmployeeGroup(groupId)
            ?? throw new InvalidOperationException($"Employee group {groupId} not found.");
        if (_company.EmployeeGroups.Any(g => g.ParentId == groupId))
            throw new InvalidOperationException($"Employee group '{group.Name}' has child groups and cannot be deleted.");
        if (_company.Employees.Any(e => e.EmployeeGroupId == groupId))
            throw new InvalidOperationException($"Employee group '{group.Name}' has employees under it and cannot be deleted.");
        _company.RemoveEmployeeGroup(group);
    }

    private void EnsureEmployeeGroupParentValid(EmployeeGroup group)
    {
        if (group.ParentId is not { } parentId) return;
        if (parentId == group.Id)
            throw new InvalidOperationException("An employee group cannot be its own parent.");
        var seen = new HashSet<Guid> { group.Id };
        var cursor = _company.FindEmployeeGroup(parentId)
            ?? throw new InvalidOperationException($"Parent employee group {parentId} not found.");
        while (true)
        {
            if (!seen.Add(cursor.Id))
                throw new InvalidOperationException($"Employee group '{group.Name}' would form a nesting cycle.");
            if (cursor.ParentId is not { } next) break;
            cursor = _company.FindEmployeeGroup(next)
                ?? throw new InvalidOperationException($"Parent employee group {next} not found.");
        }
    }

    // ------------------------------------------------------------------ Employees

    /// <summary>
    /// Creates an employee under a group (required, must exist) and an optional category (must exist), with the
    /// commonly-validated identity fields. Name unique; PAN/UAN/ESI structurally validated when supplied. Any
    /// remaining fields (designation, gender, bank details, tax regime, …) are settable on the returned entity.
    /// </summary>
    public Employee CreateEmployee(
        string name,
        Guid employeeGroupId,
        Guid? employeeCategoryId = null,
        string? employeeNumber = null,
        string? pan = null,
        string? uan = null,
        string? esiNumber = null,
        DateOnly? dateOfJoining = null)
    {
        var trimmed = RequireName(name, "employee");
        if (_company.FindEmployeeByName(trimmed) is not null)
            throw new InvalidOperationException($"An employee named '{trimmed}' already exists.");
        if (_company.FindEmployeeGroup(employeeGroupId) is null)
            throw new InvalidOperationException($"Employee group {employeeGroupId} not found.");
        if (employeeCategoryId is { } cid && _company.FindEmployeeCategory(cid) is null)
            throw new InvalidOperationException($"Employee category {cid} not found.");

        ValidatePan(pan);
        ValidateUan(uan);
        ValidateEsi(esiNumber);

        var employee = new Employee(Guid.NewGuid(), trimmed, employeeGroupId)
        {
            EmployeeCategoryId = employeeCategoryId,
            EmployeeNumber = string.IsNullOrWhiteSpace(employeeNumber) ? null : employeeNumber.Trim(),
            Pan = string.IsNullOrWhiteSpace(pan) ? null : pan.Trim(),
            Uan = string.IsNullOrWhiteSpace(uan) ? null : uan.Trim(),
            EsiNumber = string.IsNullOrWhiteSpace(esiNumber) ? null : esiNumber.Trim(),
            DateOfJoining = dateOfJoining,
        };
        _company.AddEmployee(employee);
        return employee;
    }

    /// <summary>Deletes an employee. (No later master references an employee in this slice, so this always
    /// succeeds; the attendance/payroll-voucher guard arrives with those slices.)</summary>
    public void DeleteEmployee(Guid employeeId)
    {
        var employee = _company.FindEmployee(employeeId)
            ?? throw new InvalidOperationException($"Employee {employeeId} not found.");
        _company.RemoveEmployee(employee);
    }

    // ------------------------------------------------------------------ Payroll units

    /// <summary>Creates a simple payroll unit (symbol + formal name + decimals 0–4).</summary>
    public PayrollUnit CreateSimplePayrollUnit(string symbol, string formalName, int decimalPlaces = 0)
    {
        var trimmed = RequireName(symbol, "payroll unit symbol");
        if (_company.FindPayrollUnitByName(trimmed) is not null)
            throw new InvalidOperationException($"A payroll unit '{trimmed}' already exists.");
        var unit = PayrollUnit.Simple(Guid.NewGuid(), trimmed, formalName, decimalPlaces);
        _company.AddPayrollUnit(unit);
        return unit;
    }

    /// <summary>Creates a compound payroll unit (first × factor + tail). Both components must exist and be
    /// <b>simple</b> payroll units; the factor must be &gt; 0 and the first unit must differ from the tail.</summary>
    public PayrollUnit CreateCompoundPayrollUnit(
        string symbol,
        string formalName,
        Guid firstUnitId,
        Guid tailUnitId,
        int conversionNumerator,
        int conversionDenominator = 1)
    {
        var trimmed = RequireName(symbol, "payroll unit symbol");
        if (_company.FindPayrollUnitByName(trimmed) is not null)
            throw new InvalidOperationException($"A payroll unit '{trimmed}' already exists.");

        var first = _company.FindPayrollUnit(firstUnitId)
            ?? throw new InvalidOperationException($"First payroll unit {firstUnitId} not found.");
        var tail = _company.FindPayrollUnit(tailUnitId)
            ?? throw new InvalidOperationException($"Tail payroll unit {tailUnitId} not found.");
        if (first.IsCompound || tail.IsCompound)
            throw new InvalidOperationException("A compound payroll unit's first and tail units must both be simple units.");

        var unit = PayrollUnit.Compound(Guid.NewGuid(), trimmed, formalName, firstUnitId, tailUnitId,
            conversionNumerator, conversionDenominator);
        _company.AddPayrollUnit(unit);
        return unit;
    }

    /// <summary>Deletes a payroll unit, blocked while it is a component of a compound payroll unit or the period /
    /// production unit of an attendance type.</summary>
    public void DeletePayrollUnit(Guid unitId)
    {
        var unit = _company.FindPayrollUnit(unitId)
            ?? throw new InvalidOperationException($"Payroll unit {unitId} not found.");
        if (_company.PayrollUnits.Any(u => u.FirstUnitId == unitId || u.TailUnitId == unitId))
            throw new InvalidOperationException($"Payroll unit '{unit.Symbol}' is a component of a compound unit and cannot be deleted.");
        if (_company.AttendanceTypes.Any(a => a.PayrollUnitId == unitId))
            throw new InvalidOperationException($"Payroll unit '{unit.Symbol}' is used by an attendance type and cannot be deleted.");
        _company.RemovePayrollUnit(unit);
    }

    // ------------------------------------------------------------------ Attendance / production types

    /// <summary>Creates an attendance/production type; name unique, parent (if any) must exist and not cycle, and
    /// the referenced payroll unit (if any) must exist.</summary>
    public AttendanceType CreateAttendanceType(
        string name,
        AttendanceTypeKind kind,
        Guid? parentId = null,
        Guid? payrollUnitId = null)
    {
        var trimmed = RequireName(name, "attendance type");
        if (_company.FindAttendanceTypeByName(trimmed) is not null)
            throw new InvalidOperationException($"An attendance type named '{trimmed}' already exists.");
        if (payrollUnitId is { } uid && _company.FindPayrollUnit(uid) is null)
            throw new InvalidOperationException($"Payroll unit {uid} not found.");

        var type = new AttendanceType(Guid.NewGuid(), trimmed, kind, parentId, payrollUnitId);
        EnsureAttendanceTypeParentValid(type);
        _company.AddAttendanceType(type);
        return type;
    }

    /// <summary>Re-parents an attendance type, rejecting a move that would create a cycle.</summary>
    public void SetAttendanceTypeParent(Guid typeId, Guid? parentId)
    {
        var type = _company.FindAttendanceType(typeId)
            ?? throw new InvalidOperationException($"Attendance type {typeId} not found.");
        var previous = type.ParentId;
        type.ParentId = parentId;
        try { EnsureAttendanceTypeParentValid(type); }
        catch { type.ParentId = previous; throw; }
    }

    /// <summary>Deletes an attendance type, blocked while it has child types.</summary>
    public void DeleteAttendanceType(Guid typeId)
    {
        var type = _company.FindAttendanceType(typeId)
            ?? throw new InvalidOperationException($"Attendance type {typeId} not found.");
        if (_company.AttendanceTypes.Any(a => a.ParentId == typeId))
            throw new InvalidOperationException($"Attendance type '{type.Name}' has child types and cannot be deleted.");
        _company.RemoveAttendanceType(type);
    }

    private void EnsureAttendanceTypeParentValid(AttendanceType type)
    {
        if (type.ParentId is not { } parentId) return;
        if (parentId == type.Id)
            throw new InvalidOperationException("An attendance type cannot be its own parent.");
        var seen = new HashSet<Guid> { type.Id };
        var cursor = _company.FindAttendanceType(parentId)
            ?? throw new InvalidOperationException($"Parent attendance type {parentId} not found.");
        while (true)
        {
            if (!seen.Add(cursor.Id))
                throw new InvalidOperationException($"Attendance type '{type.Name}' would form a nesting cycle.");
            if (cursor.ParentId is not { } next) break;
            cursor = _company.FindAttendanceType(next)
                ?? throw new InvalidOperationException($"Parent attendance type {next} not found.");
        }
    }

    // ------------------------------------------------------------------ helpers

    private static string RequireName(string? value, string what)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException($"A {what} name is required.");
        return trimmed;
    }

    private static void ValidatePan(string? pan)
    {
        if (string.IsNullOrWhiteSpace(pan)) return;
        if (!Pan.IsValid(pan.Trim()))
            throw new InvalidOperationException(
                $"'{pan}' is not a valid PAN (expected 10 chars = 5 letters + 4 digits + 1 letter, e.g. AAPFU0939F).");
    }

    private static void ValidateUan(string? uan)
    {
        if (string.IsNullOrWhiteSpace(uan)) return;
        if (!IsAllDigits(uan.Trim(), 12))
            throw new InvalidOperationException($"'{uan}' is not a valid UAN (expected 12 digits).");
    }

    private static void ValidateEsi(string? esi)
    {
        if (string.IsNullOrWhiteSpace(esi)) return;
        if (!IsAllDigits(esi.Trim(), 17))
            throw new InvalidOperationException($"'{esi}' is not a valid ESI number (expected 17 digits).");
    }

    private static bool IsAllDigits(string value, int length)
    {
        if (value.Length != length) return false;
        foreach (var ch in value) if (ch is < '0' or > '9') return false;
        return true;
    }
}
