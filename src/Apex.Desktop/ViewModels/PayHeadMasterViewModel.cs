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

// ---- picker option wrappers (a Display + the domain value; kept simple so ComboBoxes bind by SelectedItem) ----

/// <summary>A Pay-Head-Type picker option (the accounting/statutory nature).</summary>
public sealed class PayHeadTypeOption
{
    public PayHeadType Value { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>A Calculation-Type picker option (one of the five Tally methods).</summary>
public sealed class PayHeadCalcTypeOption
{
    public PayHeadCalculationType Value { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>An income-tax-component picker option (§192 tag; "None" is <see cref="IncomeTaxComponent.NotApplicable"/>).</summary>
public sealed class IncomeTaxComponentOption
{
    public IncomeTaxComponent Value { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>A rounding-method picker option.</summary>
public sealed class PayHeadRoundingOption
{
    public PayHeadRoundingMethod Value { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>A calculation-period picker option (Month / Day).</summary>
public sealed class PayHeadPeriodOption
{
    public PayHeadCalculationPeriod Value { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>A slab-type picker option (Percentage / Value).</summary>
public sealed class PayHeadSlabTypeOption
{
    public PayHeadComputationSlabType Value { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>An accounting-group "Under" picker option: "None" or an existing accounting <see cref="Group"/>.</summary>
public sealed class PayHeadGroupOption
{
    public Group? Group { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsNone => Group is null;
}

/// <summary>A pay-head picker option (used for the computation basis; carries the existing pay head).</summary>
public sealed class PayHeadPickerOption
{
    public PayHead PayHead { get; init; } = null!;
    public string Display { get; init; } = string.Empty;
}

/// <summary>An attendance/production-type picker option (used for On-Attendance / On-Production heads).</summary>
public sealed class PayHeadAttendanceOption
{
    public AttendanceType Type { get; init; } = null!;
    public string Display { get; init; } = string.Empty;
}

/// <summary>One added computation-basis component (a pay head, added or subtracted) shown in the editor list.</summary>
public sealed class PayHeadBasisRow
{
    public Guid PayHeadId { get; init; }
    public string PayHeadName { get; init; } = string.Empty;
    public bool IsSubtraction { get; init; }
    public string Display => (IsSubtraction ? "−  " : "+  ") + PayHeadName;   // − / +
}

/// <summary>One added computation slab (percentage or flat value, with optional band) shown in the editor list.</summary>
public sealed class PayHeadSlabRow
{
    public PayHeadComputationSlabType SlabType { get; init; }
    public int RateBasisPoints { get; init; }
    public Money Value { get; init; }
    public Money? FromAmount { get; init; }
    public Money? ToAmount { get; init; }

    public string Display
    {
        get
        {
            var band = (FromAmount, ToAmount) switch
            {
                (null, null) => "of basis",
                ({ } f, null) => $"over {IndianFormat.Amount(f.Amount)}",
                (null, { } t) => $"up to {IndianFormat.Amount(t.Amount)}",
                ({ } f, { } t) => $"{IndianFormat.Amount(f.Amount)}–{IndianFormat.Amount(t.Amount)}",
            };
            var amount = SlabType == PayHeadComputationSlabType.Percentage
                ? $"{(RateBasisPoints / 100m).ToString("0.###", CultureInfo.InvariantCulture)}%"
                : IndianFormat.Amount(Value.Amount);
            return $"{amount}  {band}";
        }
    }
}

/// <summary>A pay-head row for the existing-heads list on the master screen.</summary>
public sealed class PayHeadListRow
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string CalcType { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

/// <summary>
/// The <b>Pay Head</b> creation master ("Masters → Create → Payroll Masters → Pay Head"; Phase 8 slice 2; RQ-4;
/// Study Guide pp.198–210) — the heart of the salary structure. Captures a pay head's <b>Pay Head Type</b>, its
/// <b>Calculation Type</b> (one of the five methods), the accounting group it posts <c>Under</c>, its income-tax
/// component tag, gratuity flag, rounding method + limit and calculation period, and — the adaptive part — the
/// per-calc-type detail:
/// <list type="bullet">
///   <item><b>As Computed Value</b> shows a computation editor (a <em>basis</em> of other pay heads, each added or
///     subtracted, plus one or more <em>slabs</em>: a percentage or a flat value, optionally banded);</item>
///   <item><b>On Attendance / On Production</b> shows an attendance/production-type link (filtered to the right
///     kind) and, for On-Attendance, a per-day calculation basis;</item>
///   <item><b>Flat Rate / As User-Defined Value</b> need no extra detail (the per-employee amount lives on the
///     salary structure line, or is entered at the voucher).</item>
/// </list>
/// Creates through the <see cref="PayHeadService"/> (unique name; group/attendance references exist; computed
/// heads carry a valid, non-cyclic basis) and persists. Every engine guard — including the adversarial
/// computed-on cycle — is surfaced to <see cref="Message"/> without crashing the UI.
///
/// <para>Only reachable when Payroll is enabled (ER-13). MVVM boundary: domain + persistence only, no Avalonia
/// types ⇒ headlessly unit-testable.</para>
/// </summary>
public sealed partial class PayHeadMasterViewModel : ViewModelBase, IMasterListExportSource
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Pay Heads",
        new[]
        {
            MasterListColumn.Text("Name"), MasterListColumn.Text("Type"),
            MasterListColumn.Text("Calculation"), MasterListColumn.Text("Detail"),
        },
        Existing.Select(r => (IReadOnlyList<string>)new[] { r.Name, r.Type, r.CalcType, r.Detail }).ToList());

    // ---- picker sources ----
    public ObservableCollection<PayHeadTypeOption> Types { get; } = new();
    public ObservableCollection<PayHeadCalcTypeOption> CalcTypes { get; } = new();
    public ObservableCollection<IncomeTaxComponentOption> IncomeTaxComponents { get; } = new();
    public ObservableCollection<PayHeadRoundingOption> RoundingMethods { get; } = new();
    public ObservableCollection<PayHeadPeriodOption> Periods { get; } = new();
    public ObservableCollection<PayHeadSlabTypeOption> SlabTypes { get; } = new();
    public ObservableCollection<PayHeadGroupOption> GroupOptions { get; } = new();
    public ObservableCollection<PayHeadPickerOption> BasisPayHeadOptions { get; } = new();
    public ObservableCollection<PayHeadAttendanceOption> AttendanceTypeOptions { get; } = new();

    // ---- computation editor state ----
    public ObservableCollection<PayHeadBasisRow> BasisComponents { get; } = new();
    public ObservableCollection<PayHeadSlabRow> Slabs { get; } = new();

    /// <summary>The existing pay heads, refreshed after each create.</summary>
    public ObservableCollection<PayHeadListRow> Existing { get; } = new();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private PayHeadTypeOption? _selectedType;
    [ObservableProperty] private PayHeadCalcTypeOption? _selectedCalcType;
    [ObservableProperty] private PayHeadGroupOption? _selectedGroup;
    [ObservableProperty] private IncomeTaxComponentOption? _selectedIncomeTaxComponent;
    [ObservableProperty] private bool _useForGratuity;
    [ObservableProperty] private bool _affectsNetSalary = true;
    [ObservableProperty] private PayHeadRoundingOption? _selectedRoundingMethod;
    [ObservableProperty] private string _roundingLimitText = string.Empty;
    [ObservableProperty] private PayHeadPeriodOption? _selectedPeriod;

    // On-Attendance / On-Production
    [ObservableProperty] private PayHeadAttendanceOption? _selectedAttendanceType;
    [ObservableProperty] private string _perDayBasisText = string.Empty;

    // Computation editor inputs
    [ObservableProperty] private PayHeadPickerOption? _selectedBasisPayHead;
    [ObservableProperty] private bool _basisSubtract;
    [ObservableProperty] private PayHeadSlabTypeOption? _selectedSlabType;
    [ObservableProperty] private string _slabRateOrValueText = string.Empty;
    [ObservableProperty] private string _slabFromText = string.Empty;
    [ObservableProperty] private string _slabToText = string.Empty;

    [ObservableProperty] private string? _message;

    public PayHeadMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        Types.Add(new PayHeadTypeOption { Value = PayHeadType.Earnings, Display = "Earnings for Employees" });
        Types.Add(new PayHeadTypeOption { Value = PayHeadType.Deductions, Display = "Deductions from Employees" });
        Types.Add(new PayHeadTypeOption { Value = PayHeadType.EmployeesStatutoryDeductions, Display = "Employees' Statutory Deductions" });
        Types.Add(new PayHeadTypeOption { Value = PayHeadType.EmployersStatutoryContributions, Display = "Employer's Statutory Contributions" });
        Types.Add(new PayHeadTypeOption { Value = PayHeadType.EmployersOtherCharges, Display = "Employer's Other Charges" });
        Types.Add(new PayHeadTypeOption { Value = PayHeadType.Gratuity, Display = "Gratuity" });
        Types.Add(new PayHeadTypeOption { Value = PayHeadType.LoansAndAdvances, Display = "Loans & Advances" });
        Types.Add(new PayHeadTypeOption { Value = PayHeadType.Reimbursements, Display = "Reimbursements to Employees" });
        Types.Add(new PayHeadTypeOption { Value = PayHeadType.Bonus, Display = "Bonus" });
        Types.Add(new PayHeadTypeOption { Value = PayHeadType.NotApplicable, Display = "Not Applicable" });

        CalcTypes.Add(new PayHeadCalcTypeOption { Value = PayHeadCalculationType.OnAttendance, Display = "On Attendance" });
        CalcTypes.Add(new PayHeadCalcTypeOption { Value = PayHeadCalculationType.FlatRate, Display = "Flat Rate" });
        CalcTypes.Add(new PayHeadCalcTypeOption { Value = PayHeadCalculationType.AsComputedValue, Display = "As Computed Value" });
        CalcTypes.Add(new PayHeadCalcTypeOption { Value = PayHeadCalculationType.OnProduction, Display = "On Production" });
        CalcTypes.Add(new PayHeadCalcTypeOption { Value = PayHeadCalculationType.AsUserDefinedValue, Display = "As User-Defined Value" });

        IncomeTaxComponents.Add(new IncomeTaxComponentOption { Value = IncomeTaxComponent.NotApplicable, Display = "◦ None" });
        IncomeTaxComponents.Add(new IncomeTaxComponentOption { Value = IncomeTaxComponent.BasicSalary, Display = "Basic Salary" });
        IncomeTaxComponents.Add(new IncomeTaxComponentOption { Value = IncomeTaxComponent.DearnessAllowance, Display = "Dearness Allowance" });
        IncomeTaxComponents.Add(new IncomeTaxComponentOption { Value = IncomeTaxComponent.HouseRentAllowance, Display = "House Rent Allowance" });
        IncomeTaxComponents.Add(new IncomeTaxComponentOption { Value = IncomeTaxComponent.ConveyanceAllowance, Display = "Conveyance / Transport Allowance" });
        IncomeTaxComponents.Add(new IncomeTaxComponentOption { Value = IncomeTaxComponent.SpecialAllowance, Display = "Special / Other Allowance" });
        IncomeTaxComponents.Add(new IncomeTaxComponentOption { Value = IncomeTaxComponent.MedicalReimbursement, Display = "Medical Reimbursement" });
        IncomeTaxComponents.Add(new IncomeTaxComponentOption { Value = IncomeTaxComponent.Bonus, Display = "Bonus" });
        IncomeTaxComponents.Add(new IncomeTaxComponentOption { Value = IncomeTaxComponent.Gratuity, Display = "Gratuity" });
        IncomeTaxComponents.Add(new IncomeTaxComponentOption { Value = IncomeTaxComponent.FullyExempt, Display = "Fully Exempt" });

        RoundingMethods.Add(new PayHeadRoundingOption { Value = PayHeadRoundingMethod.NotApplicable, Display = "Not Applicable" });
        RoundingMethods.Add(new PayHeadRoundingOption { Value = PayHeadRoundingMethod.Normal, Display = "Normal Rounding" });
        RoundingMethods.Add(new PayHeadRoundingOption { Value = PayHeadRoundingMethod.Upward, Display = "Upward Rounding" });
        RoundingMethods.Add(new PayHeadRoundingOption { Value = PayHeadRoundingMethod.Downward, Display = "Downward Rounding" });

        Periods.Add(new PayHeadPeriodOption { Value = PayHeadCalculationPeriod.Month, Display = "Month" });
        Periods.Add(new PayHeadPeriodOption { Value = PayHeadCalculationPeriod.Day, Display = "Day" });

        SlabTypes.Add(new PayHeadSlabTypeOption { Value = PayHeadComputationSlabType.Percentage, Display = "Percentage" });
        SlabTypes.Add(new PayHeadSlabTypeOption { Value = PayHeadComputationSlabType.FlatValue, Display = "Value" });

        SelectedType = Types.First();
        SelectedCalcType = CalcTypes.Single(c => c.Value == PayHeadCalculationType.FlatRate);
        SelectedIncomeTaxComponent = IncomeTaxComponents.First();
        SelectedRoundingMethod = RoundingMethods.First();
        SelectedPeriod = Periods.First();
        SelectedSlabType = SlabTypes.First();

        RefreshGroups();
        RefreshBasisOptions();
        RefreshAttendanceOptions();
        RefreshList();
    }

    // ---- adaptive visibility ----

    /// <summary>True ⇒ the computation editor (basis + slabs) is shown (As Computed Value).</summary>
    public bool ShowComputationEditor => SelectedCalcType?.Value == PayHeadCalculationType.AsComputedValue;

    /// <summary>True ⇒ the attendance/production-type link is shown (On Attendance / On Production).</summary>
    public bool ShowAttendanceLink =>
        SelectedCalcType?.Value is PayHeadCalculationType.OnAttendance or PayHeadCalculationType.OnProduction;

    /// <summary>True ⇒ the per-day calculation basis is shown (On Attendance only).</summary>
    public bool ShowPerDayBasis => SelectedCalcType?.Value == PayHeadCalculationType.OnAttendance;

    partial void OnSelectedTypeChanged(PayHeadTypeOption? value)
    {
        // Default the "affect net salary" side from the chosen type (the user may still override).
        if (value is not null) AffectsNetSalary = PayHead.DefaultAffectsNetSalary(value.Value);
        Message = null;
    }

    partial void OnSelectedCalcTypeChanged(PayHeadCalcTypeOption? value)
    {
        OnPropertyChanged(nameof(ShowComputationEditor));
        OnPropertyChanged(nameof(ShowAttendanceLink));
        OnPropertyChanged(nameof(ShowPerDayBasis));
        RefreshAttendanceOptions();      // filter production vs attendance kinds by the calc type
        Message = null;
    }

    // ---- computation editor actions ----

    /// <summary>Adds the currently-picked basis pay head (added or subtracted) to the computation basis; a blank
    /// pick or a duplicate head is ignored (kept as a friendly no-op so the editor never throws).</summary>
    public void AddBasisComponent()
    {
        if (SelectedBasisPayHead is not { } option) { Message = "Pick a pay head to add to the basis."; return; }
        if (BasisComponents.Any(r => r.PayHeadId == option.PayHead.Id))
        {
            Message = $"'{option.PayHead.Name}' is already in the basis.";
            return;
        }
        BasisComponents.Add(new PayHeadBasisRow
        {
            PayHeadId = option.PayHead.Id,
            PayHeadName = option.PayHead.Name,
            IsSubtraction = BasisSubtract,
        });
        Message = null;
    }

    /// <summary>Removes a basis component row from the editor.</summary>
    public void RemoveBasisComponent(PayHeadBasisRow row)
    {
        if (row is not null) BasisComponents.Remove(row);
    }

    /// <summary>Adds a slab (percentage or flat value, with optional From/To band) to the computation; validates
    /// the numeric input and the band ordering, surfacing any problem to <see cref="Message"/>.</summary>
    public void AddSlab()
    {
        if (SelectedSlabType is not { } slabType) { Message = "Pick a slab type."; return; }

        Money? from = null, to = null;
        if (!string.IsNullOrWhiteSpace(SlabFromText))
        {
            if (!TryParseDecimal(SlabFromText, out var f) || f < 0m)
            {
                Message = "The slab 'over' amount must be a non-negative number (or blank).";
                return;
            }
            from = new Money(f);
        }
        if (!string.IsNullOrWhiteSpace(SlabToText))
        {
            if (!TryParseDecimal(SlabToText, out var t) || t < 0m)
            {
                Message = "The slab 'up to' amount must be a non-negative number (or blank).";
                return;
            }
            to = new Money(t);
        }
        if (from is { } lo && to is { } hi && hi.Amount <= lo.Amount)
        {
            Message = "The slab 'up to' amount must be greater than the 'over' amount.";
            return;
        }

        var rateBp = 0;
        var value = Money.Zero;
        if (slabType.Value == PayHeadComputationSlabType.Percentage)
        {
            if (!TryParseDecimal(SlabRateOrValueText, out var pct) || pct < 0m)
            {
                Message = "Enter a non-negative percentage for the slab (e.g. 12).";
                return;
            }
            rateBp = (int)Math.Round(pct * 100m, MidpointRounding.AwayFromZero);
        }
        else
        {
            if (!TryParseDecimal(SlabRateOrValueText, out var val) || val < 0m)
            {
                Message = "Enter a non-negative amount for the value slab (e.g. 200).";
                return;
            }
            value = new Money(val);
        }

        Slabs.Add(new PayHeadSlabRow
        {
            SlabType = slabType.Value,
            RateBasisPoints = rateBp,
            Value = value,
            FromAmount = from,
            ToAmount = to,
        });
        SlabRateOrValueText = string.Empty;
        SlabFromText = string.Empty;
        SlabToText = string.Empty;
        Message = null;
    }

    /// <summary>Removes a slab row from the editor.</summary>
    public void RemoveSlab(PayHeadSlabRow row)
    {
        if (row is not null) Slabs.Remove(row);
    }

    // ---- create ----

    /// <summary>
    /// Ctrl+A create: validates the form (name; per-calc-type detail), assembles the optional computation from the
    /// basis + slab editor, then creates the pay head via the <see cref="PayHeadService"/> (which enforces the
    /// unique name, the group/attendance references, and the computed-on / cycle rules). Any engine or parse error
    /// is surfaced to <see cref="Message"/> without crashing the UI; nothing is persisted on failure.
    /// </summary>
    public bool Create()
    {
        Message = null;
        var name = (Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "A pay head name is required.";
            return false;
        }
        if (SelectedType is null) { Message = "Pick a pay head type."; return false; }
        if (SelectedCalcType is null) { Message = "Pick a calculation type."; return false; }

        var calcType = SelectedCalcType.Value;

        // rounding: a positive limit is required only when a rounding method is chosen.
        var roundingMethod = (SelectedRoundingMethod ?? RoundingMethods.First()).Value;
        var roundingLimit = Money.Zero;
        if (roundingMethod != PayHeadRoundingMethod.NotApplicable)
        {
            if (!TryParseDecimal(RoundingLimitText, out var limit) || limit <= 0m)
            {
                Message = "A rounding method needs a positive rounding limit (e.g. 1).";
                return false;
            }
            roundingLimit = new Money(limit);
        }

        // computation (As Computed Value only)
        PayHeadComputation? computation = null;
        if (calcType == PayHeadCalculationType.AsComputedValue)
        {
            if (BasisComponents.Count == 0)
            {
                Message = "An As-Computed-Value pay head must compute on at least one pay head — add a basis.";
                return false;
            }
            if (Slabs.Count == 0)
            {
                Message = "Add at least one slab (a percentage or a value) for the computed pay head.";
                return false;
            }
            computation = new PayHeadComputation(
                BasisComponents.Select(r => new PayHeadComputationComponent(r.PayHeadId, r.IsSubtraction)),
                Slabs.Select(r => new PayHeadComputationSlab(r.SlabType, r.RateBasisPoints, r.Value, r.FromAmount, r.ToAmount)));
        }

        // attendance / production link + per-day basis
        Guid? attendanceTypeId = null;
        int? perDayBasis = null;
        if (calcType is PayHeadCalculationType.OnAttendance or PayHeadCalculationType.OnProduction)
        {
            if (SelectedAttendanceType is not { } at)
            {
                Message = calcType == PayHeadCalculationType.OnProduction
                    ? "An On-Production pay head must link a Production type."
                    : "An On-Attendance pay head must link an attendance/leave type.";
                return false;
            }
            attendanceTypeId = at.Type.Id;

            if (calcType == PayHeadCalculationType.OnAttendance && !string.IsNullOrWhiteSpace(PerDayBasisText))
            {
                if (!int.TryParse(PerDayBasisText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) || days <= 0)
                {
                    Message = "The per-day calculation basis must be a whole number of days > 0 (or blank).";
                    return false;
                }
                perDayBasis = days;
            }
        }

        try
        {
            var service = new PayHeadService(_company);
            service.CreatePayHead(
                name,
                SelectedType.Value,
                calcType,
                underGroupId: SelectedGroup?.Group?.Id,
                affectsNetSalary: AffectsNetSalary,
                incomeTaxComponent: (SelectedIncomeTaxComponent ?? IncomeTaxComponents.First()).Value,
                useForGratuity: UseForGratuity,
                roundingMethod: roundingMethod,
                roundingLimit: roundingLimit,
                calculationPeriod: (SelectedPeriod ?? Periods.First()).Value,
                attendanceTypeId: attendanceTypeId,
                perDayCalculationBasisDays: perDayBasis,
                computation: computation,
                displayName: string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim());
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        Message = $"Pay head '{name}' created.";
        ResetForm();
        RefreshBasisOptions();
        RefreshList();
        _onChanged();
        return true;
    }

    private void ResetForm()
    {
        Name = string.Empty;
        DisplayName = string.Empty;
        UseForGratuity = false;
        RoundingLimitText = string.Empty;
        PerDayBasisText = string.Empty;
        SlabRateOrValueText = string.Empty;
        SlabFromText = string.Empty;
        SlabToText = string.Empty;
        BasisSubtract = false;
        BasisComponents.Clear();
        Slabs.Clear();
        SelectedRoundingMethod = RoundingMethods.First();
        SelectedPeriod = Periods.First();
        SelectedIncomeTaxComponent = IncomeTaxComponents.First();
        SelectedGroup = GroupOptions.FirstOrDefault();
        SelectedAttendanceType = null;
        // keep the chosen type + calc type so a quick series of same-kind heads is easy;
        // re-apply the type's affect-net-salary default.
        if (SelectedType is { } t) AffectsNetSalary = PayHead.DefaultAffectsNetSalary(t.Value);
    }

    // ---- refreshers ----

    private void RefreshGroups()
    {
        var previous = SelectedGroup?.Group?.Id;
        GroupOptions.Clear();
        GroupOptions.Add(new PayHeadGroupOption { Group = null, Display = "◦ None" });
        foreach (var g in _company.Groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            GroupOptions.Add(new PayHeadGroupOption { Group = g, Display = g.Name });
        SelectedGroup = GroupOptions.FirstOrDefault(o => o.Group?.Id == previous) ?? GroupOptions.First();
    }

    private void RefreshBasisOptions()
    {
        var previous = SelectedBasisPayHead?.PayHead.Id;
        BasisPayHeadOptions.Clear();
        foreach (var ph in _company.PayHeads.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            BasisPayHeadOptions.Add(new PayHeadPickerOption { PayHead = ph, Display = ph.Name });
        SelectedBasisPayHead = BasisPayHeadOptions.FirstOrDefault(o => o.PayHead.Id == previous)
                               ?? BasisPayHeadOptions.FirstOrDefault();
    }

    private void RefreshAttendanceOptions()
    {
        var previous = SelectedAttendanceType?.Type.Id;
        AttendanceTypeOptions.Clear();

        var wantProduction = SelectedCalcType?.Value == PayHeadCalculationType.OnProduction;
        IEnumerable<AttendanceType> pool = _company.AttendanceTypes
            .Where(a => wantProduction
                ? a.Kind == AttendanceTypeKind.Production
                : a.Kind != AttendanceTypeKind.Production)
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var a in pool)
            AttendanceTypeOptions.Add(new PayHeadAttendanceOption { Type = a, Display = a.Name });

        // Keep a still-valid prior pick, but do NOT auto-select a default — an attendance/production link is a
        // conscious choice (Tally-faithful), and leaving it blank keeps the "must link a type" guard reachable.
        SelectedAttendanceType = AttendanceTypeOptions.FirstOrDefault(o => o.Type.Id == previous);
    }

    private void RefreshList()
    {
        Existing.Clear();
        foreach (var ph in _company.PayHeads.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            Existing.Add(new PayHeadListRow
            {
                Name = ph.Name,
                Type = DescribeType(ph.Type),
                CalcType = DescribeCalcType(ph.CalculationType),
                Detail = DescribeDetail(ph),
            });
    }

    private string DescribeDetail(PayHead ph)
    {
        switch (ph.CalculationType)
        {
            case PayHeadCalculationType.AsComputedValue when ph.Computation is { } c:
            {
                var basis = string.Join(" ", c.BasisComponents.Select((comp, i) =>
                {
                    var nm = _company.FindPayHead(comp.PayHeadId)?.Name ?? "?";
                    return i == 0
                        ? (comp.IsSubtraction ? "− " + nm : nm)
                        : (comp.IsSubtraction ? "− " + nm : "+ " + nm);
                }));
                var first = c.Slabs.FirstOrDefault();
                var rate = first is null
                    ? string.Empty
                    : first.SlabType == PayHeadComputationSlabType.Percentage
                        ? $"{(first.RateBasisPoints / 100m).ToString("0.###", CultureInfo.InvariantCulture)}% of "
                        : $"{IndianFormat.Amount(first.Value.Amount)} on ";
                return $"{rate}{basis}";
            }
            case PayHeadCalculationType.OnAttendance or PayHeadCalculationType.OnProduction
                when ph.AttendanceTypeId is { } aid:
                return "on " + (_company.FindAttendanceType(aid)?.Name ?? "?");
            case PayHeadCalculationType.FlatRate:
                return "flat rate";
            case PayHeadCalculationType.AsUserDefinedValue:
                return "user-defined";
            default:
                return "—";
        }
    }

    private static string DescribeType(PayHeadType t) => t switch
    {
        PayHeadType.Earnings => "Earnings",
        PayHeadType.Deductions => "Deductions",
        PayHeadType.EmployeesStatutoryDeductions => "Empl. Statutory Ded.",
        PayHeadType.EmployersStatutoryContributions => "Employer Contrib.",
        PayHeadType.EmployersOtherCharges => "Employer Charges",
        PayHeadType.Gratuity => "Gratuity",
        PayHeadType.LoansAndAdvances => "Loans & Advances",
        PayHeadType.Reimbursements => "Reimbursements",
        PayHeadType.Bonus => "Bonus",
        PayHeadType.NotApplicable => "Not Applicable",
        _ => t.ToString(),
    };

    private static string DescribeCalcType(PayHeadCalculationType c) => c switch
    {
        PayHeadCalculationType.OnAttendance => "On Attendance",
        PayHeadCalculationType.FlatRate => "Flat Rate",
        PayHeadCalculationType.AsComputedValue => "As Computed Value",
        PayHeadCalculationType.OnProduction => "On Production",
        PayHeadCalculationType.AsUserDefinedValue => "As User-Defined",
        _ => c.ToString(),
    };

    private static bool TryParseDecimal(string? text, out decimal value)
        => decimal.TryParse(
            (text ?? string.Empty).Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out value);
}
