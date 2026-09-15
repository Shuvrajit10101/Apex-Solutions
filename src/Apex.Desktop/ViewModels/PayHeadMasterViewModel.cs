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

    /// <summary>The vendor's Computation Information "Effective From" (schema v63, census 7.19); null = perpetual.</summary>
    public DateOnly? EffectiveFrom { get; init; }

    /// <summary>The slab's last in-force date (schema v63); null = perpetual.</summary>
    public DateOnly? EffectiveTo { get; init; }

    /// <summary>
    /// The effective window as the user sees it in the added-slabs list.
    ///
    /// <para>🔴 <b>THE UNDATED CASE SAYS "every period" IN WORDS, DELIBERATELY.</b> An undated slab reads as a
    /// blank column on every other screen in this product, and a blank is exactly what a user configuring a
    /// Labour Welfare Fund deduction would skim past — while the consequence of skimming past it is an annual
    /// contribution coming off the payslip twelve times. Naming the perpetual case makes the dangerous default
    /// visible instead of invisible.</para>
    /// </summary>
    public string EffectiveDisplay => (EffectiveFrom, EffectiveTo) switch
    {
        (null, null) => "every period",
        ({ } f, null) => $"from {f:dd-MMM-yyyy}",
        (null, { } t) => $"up to {t:dd-MMM-yyyy}",
        ({ } f, { } t) when f == t => $"on {f:dd-MMM-yyyy}",
        ({ } f, { } t) => $"{f:dd-MMM-yyyy} to {t:dd-MMM-yyyy}",
    };

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
            return $"{amount}  {band}  ({EffectiveDisplay})";
        }
    }
}

/// <summary>A pay-head row for the existing-heads list on the master screen.</summary>
public sealed partial class PayHeadListRow : ObservableObject, IPayrollMasterListRow
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string CalcType { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;

    /// <summary>The stable identity of the pay head this row displays (census 7.6 / 7.16). Filled in
    /// <see cref="PayHeadMasterViewModel.RefreshList"/>; an empty id here would arm Alt+D against nothing — the
    /// exact trap <c>PayrollMasterHalfWiredKindsTests</c> caught on the employee list.</summary>
    public Guid MasterId { get; init; }

    string IMasterListRow.MasterName => Name;

    [ObservableProperty] private bool _isHighlighted;
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
public sealed partial class PayHeadMasterViewModel : ViewModelBase, IMasterListExportSource, IPayrollMasterList
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    private readonly PayrollMasterHighlight<PayHeadListRow> _highlight;

    /// <summary>The id of the pay head being ALTERED, or <see cref="Guid.Empty"/> in Create mode (7.6 / 7.16).</summary>
    private Guid _editingId = Guid.Empty;

    /// <inheritdoc/>
    public bool IsAltering => _editingId != Guid.Empty;

    /// <summary>The screen caption — the one visible signal telling the operator which verb Ctrl+A will run.</summary>
    public string Caption => IsAltering ? "Pay Head Alteration" : "Pay Head Creation";

    /// <inheritdoc/>
    public string MasterKindLabel => "pay head";

    /// <inheritdoc/>
    public IMasterListRow? HighlightedMasterRow => _highlight.Row;

    /// <summary>The highlighted existing-pay-head row, or <c>null</c>.</summary>
    public PayHeadListRow? HighlightedRow => _highlight.Row;

    /// <inheritdoc/>
    public void MoveHighlight(int direction) => _highlight.Move(direction);

    /// <inheritdoc/>
    public void ReloadExisting() { RefreshGroups(); RefreshBasisOptions(); RefreshAttendanceOptions(); RefreshList(); }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>The referential guards (another head computes on this one; a salary structure references it) live in
    /// <see cref="PayHeadService.DeletePayHead"/> and are not duplicated here — the shell turns the engine's own
    /// refusal message into a notice.</para>
    ///
    /// <para>🔴 <b>IT DOES NOT PERSIST, AND THAT IS THE CONTRACT RATHER THAN AN OMISSION.</b> This method used to
    /// end with <c>_storage.Save(_company)</c>. The only caller is
    /// <c>MainWindowViewModel.ConfirmDeletion</c>, which calls <c>list.DeleteMaster(id)</c> and then saves the
    /// company itself inside its own <c>SaveFailure</c> try — so the pay head wrote the WHOLE company twice on
    /// every delete, and it was the only one of the twelve sibling <c>DeleteMaster</c> implementations that did.
    /// Every sibling is one call to its engine service, <see cref="IMasterListScreen.DeleteMaster"/> says nothing
    /// about persisting, and the shell's save is the one that is wrapped in the failure handling. Pinned by
    /// <c>PayrollMasterAlterDeleteTests.Pay_head_DeleteMaster_does_not_persist_by_itself</c>.</para>
    /// </remarks>
    public void DeleteMaster(Guid id) => new PayHeadService(_company).DeletePayHead(id);

    /// <summary>
    /// Opens this master in <b>Alter</b> mode over an existing pay head — the same form, pre-filled, including the
    /// computation basis and every slab with its effective window. Returns <c>null</c> if the id does not resolve.
    ///
    /// <para>🔴 <b>Pre-filling the computation editor is the load-bearing half.</b> A pay head's rate lives in its
    /// slabs; opening the alteration screen with an EMPTY slab list would show the operator a computed head with no
    /// formula, and Ctrl+A would then save exactly that — silently deleting the rate they came to correct. Both
    /// collections are therefore rebuilt from the stored computation, and the effective-from / effective-to dates
    /// (schema v63, census 7.19) are carried across with them, because dropping those would turn a once-a-year
    /// Labour Welfare Fund deduction into a monthly one.</para>
    /// </summary>
    public static PayHeadMasterViewModel? ForAlter(
        Company company, CompanyStorage storage, Guid payHeadId, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (company.FindPayHead(payHeadId) is not { } head) return null;

        var vm = new PayHeadMasterViewModel(company, storage, onChanged);
        vm._editingId = payHeadId;

        vm.Name = head.Name;
        vm.DisplayName = head.DisplayName ?? string.Empty;
        // The type first: assigning it rebuilds the income-tax picker (OnSelectedTypeChanged) and resets
        // AffectsNetSalary to the type's default, so both must be re-applied AFTER it, not before.
        vm.SelectedType = vm.Types.FirstOrDefault(t => t.Value == head.Type) ?? vm.Types.First();
        vm.SelectedCalcType = vm.CalcTypes.FirstOrDefault(c => c.Value == head.CalculationType) ?? vm.CalcTypes.First();
        vm.AffectsNetSalary = head.AffectsNetSalary;
        vm.SelectedGroup = vm.GroupOptions.FirstOrDefault(o => o.Group?.Id == head.UnderGroupId)
                           ?? vm.GroupOptions.First();
        vm.SelectedIncomeTaxComponent = vm.IncomeTaxComponents.FirstOrDefault(o => o.Value == head.IncomeTaxComponent)
                                        ?? vm.IncomeTaxComponents.First();
        vm.UseForGratuity = head.UseForGratuity;
        vm.SelectedRoundingMethod = vm.RoundingMethods.FirstOrDefault(o => o.Value == head.RoundingMethod)
                                    ?? vm.RoundingMethods.First();
        vm.RoundingLimitText = head.RoundingMethod == PayHeadRoundingMethod.NotApplicable
            ? string.Empty
            : head.RoundingLimit.Amount.ToString("0.##", CultureInfo.InvariantCulture);
        vm.SelectedPeriod = vm.Periods.FirstOrDefault(o => o.Value == head.CalculationPeriod) ?? vm.Periods.First();

        // The attendance/production picker is filtered by calc type, and SelectedCalcType was assigned above, so
        // AttendanceTypeOptions already holds the right pool by the time this runs.
        vm.SelectedAttendanceType = vm.AttendanceTypeOptions.FirstOrDefault(o => o.Type.Id == head.AttendanceTypeId);
        vm.PerDayBasisText = head.PerDayCalculationBasisDays is { } d
            ? d.ToString(CultureInfo.InvariantCulture)
            : string.Empty;

        // A head may not compute on ITSELF — take it out of the basis picker rather than let the operator find out
        // by being refused. The engine's self-reference and cycle guards still have the last word.
        var self = vm.BasisPayHeadOptions.FirstOrDefault(o => o.PayHead.Id == payHeadId);
        if (self is not null) vm.BasisPayHeadOptions.Remove(self);
        vm.SelectedBasisPayHead = vm.BasisPayHeadOptions.FirstOrDefault();

        if (head.Computation is { } computation)
        {
            foreach (var component in computation.BasisComponents)
                vm.BasisComponents.Add(new PayHeadBasisRow
                {
                    PayHeadId = component.PayHeadId,
                    PayHeadName = company.FindPayHead(component.PayHeadId)?.Name ?? "?",
                    IsSubtraction = component.IsSubtraction,
                });
            foreach (var slab in computation.Slabs)
                vm.Slabs.Add(new PayHeadSlabRow
                {
                    SlabType = slab.SlabType,
                    RateBasisPoints = slab.RateBasisPoints,
                    Value = slab.Value,
                    FromAmount = slab.FromAmount,
                    ToAmount = slab.ToAmount,
                    EffectiveFrom = slab.EffectiveFrom,
                    EffectiveTo = slab.EffectiveTo,
                });
        }

        vm.OnPropertyChanged(nameof(IsAltering));
        vm.OnPropertyChanged(nameof(Caption));
        return vm;
    }

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

    // v63 / census 7.19 — the vendor's Computation Information "Effective From" (plus an explicit end date).
    // Blank in BOTH ⇒ the slab is in force in every payroll period, which is what every pre-v63 slab did.
    [ObservableProperty] private string _slabEffectiveFromText = string.Empty;
    [ObservableProperty] private string _slabEffectiveToText = string.Empty;

    [ObservableProperty] private string? _message;

    public PayHeadMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _highlight = new PayrollMasterHighlight<PayHeadListRow>(
            Existing, () => { OnPropertyChanged(nameof(HighlightedRow)); OnPropertyChanged(nameof(HighlightedMasterRow)); });

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

        // The income-tax-component picker is built by RefreshIncomeTaxComponents() so the §192 salary-TDS marker can
        // be gated to the one pay-head type it is valid on (see that method). It is populated when SelectedType is
        // first assigned below (OnSelectedTypeChanged → RefreshIncomeTaxComponents), before SelectedIncomeTaxComponent
        // reads IncomeTaxComponents.First().

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
        // Rebuild the income-tax picker: the §192 salary-TDS marker is offered only on an Employees' Statutory
        // Deductions head (the type it must post as), so it appears/disappears as the pay-head type changes.
        RefreshIncomeTaxComponents();
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
            // FRONT LINE (W0-13 B7). The band bounds are money too — they persist as from_amount_paisa /
            // to_amount_paisa through the same Paisa.FromMoney that throws on a sub-paisa figure. This file's
            // slab editor tested sign and band ORDER only; PayHeadService.ValidateComputation never looks at slab
            // money at all, and its one IsPaisaExact covers RoundingLimit. See StorableAmount for branch order.
            if (StorableAmount.ErrorFor(f, SlabFromText, "the slab 'over' amount") is { } fromError)
            {
                Message = fromError;
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
            if (StorableAmount.ErrorFor(t, SlabToText, "the slab 'up to' amount") is { } toError)
            {
                Message = toError;
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
            if (StorableAmount.ErrorFor(val, SlabRateOrValueText, "the value slab amount") is { } valueError)
            {
                Message = valueError;
                return;
            }
            value = new Money(val);
        }

        // v63 / census 7.19 — the effective window. Both blank ⇒ perpetual, i.e. exactly the pre-v63 slab.
        DateOnly? effectiveFrom = null, effectiveTo = null;
        if (!string.IsNullOrWhiteSpace(SlabEffectiveFromText))
        {
            if (!ApexDate.TryParse(SlabEffectiveFromText, out var ef))
            {
                Message = "The slab 'effective from' must be a date (or blank for every period).";
                return;
            }
            effectiveFrom = ef;
        }
        if (!string.IsNullOrWhiteSpace(SlabEffectiveToText))
        {
            if (!ApexDate.TryParse(SlabEffectiveToText, out var et))
            {
                Message = "The slab 'effective to' must be a date (or blank for every period).";
                return;
            }
            effectiveTo = et;
        }
        // Refused here as well as in the domain constructor, because an inverted window produces a slab that can
        // never be in force — a deduction that silently never happens, which is invisible on the payslip and only
        // shows up later as an unremitted statutory liability.
        if (effectiveFrom is { } dFrom && effectiveTo is { } dTo && dTo < dFrom)
        {
            Message = "The slab 'effective to' must be on or after the 'effective from' date.";
            return;
        }

        Slabs.Add(new PayHeadSlabRow
        {
            SlabType = slabType.Value,
            RateBasisPoints = rateBp,
            Value = value,
            FromAmount = from,
            ToAmount = to,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
        });
        SlabRateOrValueText = string.Empty;
        SlabFromText = string.Empty;
        SlabToText = string.Empty;
        SlabEffectiveFromText = string.Empty;
        SlabEffectiveToText = string.Empty;
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
            // FRONT LINE (W0-13 B7) — the FOURTH money field on this screen, and it needs the same guard as its
            // three slab siblings above. PayHeadService.cs's own `!payHead.RoundingLimit.IsPaisaExact` check is
            // HALF the rule: it has no magnitude ceiling, so a paisa-exact 18-digit limit passed the parse, passed
            // `<= 0m`, passed ValidatePayHead and overflowed long inside Paisa.FromMoney. The broad catch below now
            // contains that, but the operator would be shown the store's raw "too large or too small for an Int64"
            // instead of a refusal that names the field.
            if (StorableAmount.ErrorFor(limit, RoundingLimitText, "the rounding limit") is { } limitError)
            {
                Message = limitError;
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
                Slabs.Select(r => new PayHeadComputationSlab(
                    r.SlabType, r.RateBasisPoints, r.Value, r.FromAmount, r.ToAmount,
                    // v63 / census 7.19: carry the effective window into the domain. Dropping it here would let
                    // the screen show "on 31-Dec-2026" while the saved head deducted in all twelve months.
                    r.EffectiveFrom, r.EffectiveTo)));
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

        // 7.6 / 7.16 — the SAME Ctrl+A runs the verb the screen is in (see the Caption the operator is reading).
        var altering = IsAltering;

        // W0-13 B7 — PayHeadService.CreatePayHead validates and then writes the head to the shared aggregate
        // BEFORE the store is reached, and the shipped catch took no rollback at all: a refused save left a pay
        // head the .db does not hold, so every LATER save threw. `created` is null on an engine failure because
        // CreatePayHead validates before it adds, so the restore is exact rather than a list snapshot.
        //
        // 🔴 THE ALTER PATH HAS THE SAME HAZARD AND IT IS WORSE, so it gets the same treatment. AlterPayHead
        // writes the new field values onto the LIVE pay head and rolls them back itself if a domain guard
        // refuses — but it cannot see a STORE failure, which happens after it has returned. Without the state
        // captured here, a failed save would leave the in-memory head altered while the .db still held the old
        // one: the operator is shown a refusal, and the next unrelated save silently persists the alteration
        // anyway. Since the alteration is typically a RATE, that is a wrong-money outcome, not a stale screen.
        PayHead? created = null;
        PayHeadState? restorePoint = null;
        PayHead? edited = null;
        // F11 — how far this alteration reaches. Counted BEFORE the write (the count cannot change during it) and
        // reported after, because a salary structure's reference is not visible on this screen.
        var structuresAffected = 0;
        if (altering)
        {
            edited = _company.FindPayHead(_editingId);
            if (edited is null)
            {
                Message = "This pay head no longer exists — it may have been deleted in another window.";
                return false;
            }
            restorePoint = PayHeadState.Capture(edited);
        }

        try
        {
            var service = new PayHeadService(_company);
            if (altering)
            {
                // 🔴 BUILT FROM THE HEAD, THEN OVERRIDDEN FIELD BY FIELD. `PayHeadEdit.From(edited)` starts from
                // the head exactly as it stands, so everything this screen does NOT show — the four statutory
                // tags and the overtime flag — is carried through unchanged instead of being reset to
                // None/false, which would take a statutory head out of its own return with nothing on screen to
                // say so. This used to be six hand-copied arguments guarded only by a comment; it is now the
                // default, and the only fields that move are the ones listed below, which are exactly the fields
                // the operator can see.
                structuresAffected = service.SalaryStructuresUsing(_editingId);
                service.AlterPayHead(_editingId, PayHeadEdit.From(edited!) with
                {
                    Name = name,
                    DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim(),
                    Type = SelectedType.Value,
                    CalculationType = calcType,
                    AffectsNetSalary = AffectsNetSalary,
                    UnderGroupId = SelectedGroup?.Group?.Id,
                    IncomeTaxComponent = (SelectedIncomeTaxComponent ?? IncomeTaxComponents.First()).Value,
                    UseForGratuity = UseForGratuity,
                    RoundingMethod = roundingMethod,
                    RoundingLimit = roundingLimit,
                    CalculationPeriod = (SelectedPeriod ?? Periods.First()).Value,
                    AttendanceTypeId = attendanceTypeId,
                    PerDayCalculationBasisDays = perDayBasis,
                    Computation = computation,
                });
            }
            else
            {
                created = service.CreatePayHead(
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
            }
            _storage.Save(_company);
        }
        catch (Exception ex)
        {
            // Restore FIRST and UNCONDITIONALLY — a type filter must never decide whether the rollback runs. The
            // old `when (ex is InvalidOperationException or ArgumentException)` filter also let a SqliteException
            // (SQLITE_BUSY from a second instance holding the write lock, READONLY, FULL) escape as a crash.
            if (created is not null) _company.RemovePayHead(created);
            if (restorePoint is { } point && edited is not null) point.RestoreTo(edited);
            if (!SaveFailure.IsReportable(ex)) throw;
            Message = ex.Message;
            return false;
        }

        if (altering)
        {
            // Deliberately NOT ResetForm() — the operator stays on the altered head, exactly as the five payroll
            // masters that had alteration before this one behave. RefreshBasisOptions is skipped for the same
            // reason: it would put the head back into its own basis picker while the screen is still editing it.
            RefreshList();
            // 🔴 THE REACH IS STATED, BECAUSE IT IS NOT VISIBLE ON THIS SCREEN (review finding F11). Deleting a
            // referenced head is REFUSED; altering one is deliberately allowed, since a mistyped rate on a head no
            // structure references is the rare case and refusing the normal one is what made the wrong figure
            // permanent. What the operator cannot see from here is how many salary structures the correction will
            // reach. The alteration is forward-only — a posted payroll voucher carries its own immutable
            // PayrollLineDetail — so the sentence names the NEXT payroll run and does not imply a restatement.
            Message = structuresAffected == 0
                ? $"Pay head '{name}' altered."
                : $"Pay head '{name}' altered. {structuresAffected} salary " +
                  (structuresAffected == 1 ? "structure uses" : "structures use") +
                  " it — the next payroll run will use the new values. Periods already paid are unchanged.";
            _onChanged();
            return true;
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
        // v63 / census 7.19 — cleared with the rest of the slab editor, so a date typed for one pay head can
        // never leak onto the next one the user creates.
        SlabEffectiveFromText = string.Empty;
        SlabEffectiveToText = string.Empty;
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

    /// <summary>
    /// Rebuilds the income-tax-component picker for the currently-selected pay-head type. The ten earning/exempt
    /// classifications are always offered; the <see cref="IncomeTaxComponent.TaxDeductedAtSource"/> §192 salary-TDS
    /// marker — the tag that makes a head <b>the</b> salary-TDS withholding head the <c>SalaryIncomeTax</c> engine
    /// computes (WI-6) — is offered <b>only</b> on an <see cref="PayHeadType.EmployeesStatutoryDeductions"/> head,
    /// the accounting side it must post on (the <c>PayHeadService</c> role guard rejects it on any other type). The
    /// two <b>NPS statutory pay types</b> (census row 7.18) are offered on the side(s) the scheme has — Tier-I on an
    /// employee deduction <b>or</b> an employer contribution, Tier-II on an employee deduction only. A still-valid
    /// prior selection is preserved; otherwise the picker falls back to "None".
    /// </summary>
    private void RefreshIncomeTaxComponents()
    {
        var previous = SelectedIncomeTaxComponent?.Value;
        IncomeTaxComponents.Clear();
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
        if (SelectedType?.Value == PayHeadType.EmployeesStatutoryDeductions)
            IncomeTaxComponents.Add(new IncomeTaxComponentOption
            {
                Value = IncomeTaxComponent.TaxDeductedAtSource,
                Display = "Income Tax (TDS on Salary)",
            });

        // The two NPS statutory pay types (census row 7.18), each offered only on the pay-head type(s) the scheme
        // actually has a side for — Tier-I on BOTH the employee deduction and the employer contribution, Tier-II on
        // the employee deduction only. The rule and its vendor citation live in NationalPensionScheme so the picker
        // and the PayHeadService guard can never drift apart: an option is offered here exactly when the service
        // would accept it, so the operator is never shown a choice that is then rejected on save.
        if (SelectedType?.Value is { } npsType)
            foreach (var nps in new[]
                     {
                         IncomeTaxComponent.NationalPensionSchemeTierI,
                         IncomeTaxComponent.NationalPensionSchemeTierII,
                     })
                if (NationalPensionScheme.IsPayHeadTypeAllowed(nps, npsType))
                    IncomeTaxComponents.Add(new IncomeTaxComponentOption
                    {
                        Value = nps,
                        Display = NationalPensionScheme.CaptionFor(nps)!,
                    });

        SelectedIncomeTaxComponent = IncomeTaxComponents.FirstOrDefault(o => o.Value == previous)
                                     ?? IncomeTaxComponents.First();
    }

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
        // By id, never by index — see PayrollMasterHighlight.RestoreTo for why. This list is SORTED BY NAME, so an
        // alteration that renames a head re-sorts it, and an index restore would leave the cursor on a NEIGHBOUR
        // that the next Alt+D would delete.
        var previous = _highlight.IdBeforeRebuild();

        Existing.Clear();
        foreach (var ph in _company.PayHeads.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            Existing.Add(new PayHeadListRow
            {
                MasterId = ph.Id,
                Name = ph.Name,
                Type = DescribeType(ph.Type),
                CalcType = DescribeCalcType(ph.CalculationType),
                Detail = DescribeDetail(ph),
            });

        _highlight.RestoreTo(previous);
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
                // v63 / census 7.19 — say so in the LIST when any slab is dated. Without this the existing-heads
                // list renders a once-a-year Labour Welfare Fund head and an every-month deduction identically,
                // and the difference between them is eleven extra deductions a year.
                var dated = c.Slabs.Any(s => s.IsDated) ? "  [dated]" : string.Empty;
                return $"{rate}{basis}{dated}";
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
