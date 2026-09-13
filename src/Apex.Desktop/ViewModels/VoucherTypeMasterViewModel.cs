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

/// <summary>A voucher-type row for the existing-types list on the master screen.</summary>
/// <summary>
/// One row in the voucher-type master's <b>Voucher Classes</b> list (census 9.9) — the vendor's "Name of Class"
/// and its "Use Class for Inter-Godown Transfers" setting, for a class already defined on the type being altered.
/// </summary>
public sealed class VoucherClassListRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>"Yes" / "No" — the operator-facing rendering of
    /// <see cref="VoucherClass.UseClassForInterGodownTransfers"/>.</summary>
    public string InterGodownTransfers { get; init; } = string.Empty;
}

/// <summary>One row of the vendor's <b>"Default Accounting Allocations for all items in Invoice"</b> table
/// (census 2.6), rendered for the screen.</summary>
public sealed class VoucherClassAllocationRow
{
    public Guid Id { get; init; }
    public string Ledger { get; init; } = string.Empty;
    /// <summary>The allocation as a percentage string ("33.33%") — the stored value is basis points.</summary>
    public string Percent { get; init; } = string.Empty;
}

/// <summary>One row of the vendor's <b>Additional Accounting Entries</b> table (census 2.6), rendered for the
/// screen. Every column is the vendor's own: Ledger Name, Type of Calculation, Value Basis, the rounding, and
/// Remove if Zero.</summary>
public sealed class VoucherClassEntryRow
{
    public Guid Id { get; init; }
    public string Ledger { get; init; } = string.Empty;
    public string CalculationType { get; init; } = string.Empty;
    public string ValueBasis { get; init; } = string.Empty;
    public string Rounding { get; init; } = string.Empty;
    public string RemoveIfZero { get; init; } = string.Empty;
}

/// <summary>A ledger pick for the two census-2.6 tables.</summary>
public sealed class ClassLedgerOption
{
    public Guid Id { get; init; }
    public string Display { get; init; } = string.Empty;
    public override string ToString() => Display;
}

/// <summary>A vendor "Type of Calculation" pick.</summary>
public sealed class ClassCalculationOption
{
    public VoucherClassCalculationType Value { get; init; }
    public string Display { get; init; } = string.Empty;
    public override string ToString() => Display;
}

/// <summary>A vendor "Rounding Method" pick.</summary>
public sealed class ClassRoundingOption
{
    public VoucherClassRoundingMethod Value { get; init; }
    public string Display { get; init; } = string.Empty;
    public override string ToString() => Display;
}

public sealed partial class VoucherTypeListRow : ObservableObject, IMasterListRow
{
    public string Name { get; init; } = string.Empty;
    public string BaseType { get; init; } = string.Empty;
    public string Numbering { get; init; } = string.Empty;
    public string Abbreviation { get; init; } = string.Empty;

    /// <summary>"Yes" / "No" — the operator-facing rendering of <see cref="VoucherType.IsActive"/>. It is a COLUMN
    /// rather than a filtered-out row, because the whole point of the Show-Inactive gesture is to see the
    /// deactivated types in order to switch one back on.</summary>
    public string Active { get; init; } = string.Empty;

    /// <summary>The stable identity of the voucher type this row displays (census 2.4).</summary>
    public Guid MasterId { get; init; }

    string IMasterListRow.MasterName => Name;

    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>A "Type of Voucher" (base kind) picker option — the label plus the enum value.</summary>
public sealed class VoucherBaseTypeOption
{
    public VoucherBaseType Value { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>A "Method of Voucher Numbering" picker option — the label plus the enum value.</summary>
public sealed class NumberingMethodOption
{
    public NumberingMethod Value { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>
/// <b>The Voucher Type master (census 2.4, 5.10, 5.11) — "Masters → Create → Voucher Type".</b> Name, <b>Type of
/// Voucher</b> (the base kind), <b>Abbreviation</b>, <b>Method of Voucher Numbering</b>, and the three user
/// switches: <b>Activate this Voucher Type</b>, <b>Print voucher after saving</b> and <b>Provide narration for
/// each ledger in voucher</b>. Ctrl+A creates or alters; the existing-types list takes the arrows, Ctrl+Enter
/// (alter) and Alt+D (delete) through <see cref="IMasterListScreen"/>.
///
/// <para><b>Why this screen matters more than its census rows suggest.</b> <see cref="VoucherType"/> carries ~20
/// configurable properties and 24 seeded instances, and before this screen existed <b>not one of them could be
/// edited by an operator</b>. Two consequences followed and both are closed here: the numbering method was a
/// get-only display string (5.10), and — the larger one — <see cref="VoucherType.IsActive"/> could not be
/// flipped by any route in the product, so the seeded-inactive payroll voucher types could never post. That is
/// what <see cref="ShowInactive"/> plus <see cref="ToggleActiveOnHighlighted"/> unblock.</para>
///
/// <para><b>R7 — fidelity.</b> ATTESTED at help.tallysolutions.com (fetched 2026-09-05): the five numbering
/// methods (<see cref="NumberingMethodOption"/>), <i>"Provide narration for each ledger in voucher"</i>,
/// <i>"Enable Print voucher after saving to automatically open the Voucher Printing screen"</i>, the
/// <b>Abbreviation</b> field, and that alteration is reached via <i>Alter Master &gt; Voucher Type</i>.
/// 🔴 <b>OURS (ruling 9)</b>: the base kind being immutable on alter, a predefined type being undeletable, the
/// Show-Inactive gesture living on THIS screen only (see the scope note below), and every message string.</para>
///
/// <para>🔴 <b>SHOW INACTIVE IS SCOPED TO VOUCHER TYPES, AND THAT IS A DELIBERATE UNDER-CLAIM.</b> Census row
/// 2.13 asks for Show Inactive across <i>every</i> master; <see cref="VoucherType.IsActive"/> is the only active
/// flag that exists anywhere in this domain, so a general gesture would need a new flag on every master and a
/// wide schema change. This screen closes the voucher-type half (census 5.11) and row 2.13 stays open.</para>
///
/// <para>MVVM boundary: references the domain + persistence but no Avalonia/UI types, so it is headlessly
/// unit-testable.</para>
/// </summary>
public sealed partial class VoucherTypeMasterViewModel : ViewModelBase, IMasterListExportSource, IMasterListScreen
{
    private readonly PayrollMasterHighlight<VoucherTypeListRow> _highlight;

    /// <summary>The id of the type being ALTERED, or <see cref="Guid.Empty"/> in Create mode.</summary>
    private Guid _editingId = Guid.Empty;

    /// <inheritdoc/>
    public bool IsAltering => _editingId != Guid.Empty;

    /// <summary>The screen caption — the one visible signal telling the operator which verb Ctrl+A will run.</summary>
    public string Caption => IsAltering ? "Voucher Type Alteration" : "Voucher Type Creation";

    /// <inheritdoc/>
    public string MasterKindLabel => "voucher type";

    /// <inheritdoc/>
    public IMasterListRow? HighlightedMasterRow => _highlight.Row;

    /// <summary>The highlighted existing-type row, or <c>null</c>.</summary>
    public VoucherTypeListRow? HighlightedRow => _highlight.Row;

    /// <inheritdoc/>
    public void MoveHighlight(int direction) => _highlight.Move(direction);

    /// <inheritdoc/>
    public void ReloadExisting() => RefreshList();

    /// <inheritdoc/>
    public void DeleteMaster(Guid id) => new VoucherTypeService(_company).Delete(id);

    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Voucher Types",
        new[]
        {
            MasterListColumn.Text("Name"), MasterListColumn.Text("Type of Voucher"),
            MasterListColumn.Text("Numbering"), MasterListColumn.Text("Abbrev."),
            MasterListColumn.Text("Active"),
        },
        Existing.Select(r => (IReadOnlyList<string>)new[] { r.Name, r.BaseType, r.Numbering, r.Abbreviation, r.Active })
                .ToList());

    /// <summary>Every base kind a new voucher type may derive from, in the domain's own order.</summary>
    public ObservableCollection<VoucherBaseTypeOption> BaseTypes { get; } = new();

    /// <summary>The five ATTESTED methods of voucher numbering.</summary>
    public ObservableCollection<NumberingMethodOption> NumberingMethods { get; } = new();

    /// <summary>The existing voucher types, refreshed after each create, alter, delete or activation.</summary>
    public ObservableCollection<VoucherTypeListRow> Existing { get; } = new();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _abbreviation = string.Empty;
    [ObservableProperty] private VoucherBaseTypeOption? _selectedBaseType;
    [ObservableProperty] private NumberingMethodOption? _selectedNumbering;
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private bool _printAfterSaving;
    [ObservableProperty] private bool _provideNarrationForEachLedger;
    [ObservableProperty] private string? _message;

    /// <summary>
    /// <b>Show Inactive</b> (census 5.11). When off — the default — the existing-types list shows only ACTIVE
    /// types, which is what every other master list in this application shows. Switching it on brings the
    /// deactivated ones into view so one can be highlighted and switched back on with
    /// <see cref="ToggleActiveOnHighlighted"/>.
    ///
    /// <para>🔴 <b>Off by default is the load-bearing half.</b> With it always on, a company that has deactivated
    /// a series would see it in every picker-shaped list and could re-select it; with it never available, a
    /// deactivated type is unreachable forever — which is precisely the state
    /// <c>VoucherTypeResolver</c>'s own remarks record as "the documented show-inactive → activate gesture meant
    /// nothing".</para>
    /// </summary>
    [ObservableProperty] private bool _showInactive;

    partial void OnShowInactiveChanged(bool value) => RefreshList();

    // ------------------------------------------- W-K1 · census 9.9 — Voucher Classes (the Stock Journal transfer class)

    /// <summary>The classes already defined on the type being ALTERED (census 9.9). Empty in Create mode: a class
    /// hangs off a type, so there is no type to hang one on until the type exists.</summary>
    public ObservableCollection<VoucherClassListRow> Classes { get; } = new();

    /// <summary>The vendor's <b>"Name of Class"</b> for the class about to be added.</summary>
    [ObservableProperty] private string _newClassName = string.Empty;

    /// <summary>The vendor's <b>"Use Class for Inter-Godown Transfers"</b> for the class about to be added.</summary>
    [ObservableProperty] private bool _newClassInterGodownTransfers = true;

    /// <summary>
    /// True when the Voucher Classes section is shown: an <b>alteration</b> of a type whose base kind can carry a
    /// class — the Stock Journal (census 9.9, the transfer class) or one of the <b>invoice-shaped</b> kinds
    /// (census 2.6, the general machinery).
    ///
    /// <para>Create mode is still excluded: a class is a child of a type that does not exist yet, so offering the
    /// field there would either silently discard what the operator typed or require a second, hidden save.</para>
    ///
    /// <para>🔴 <b>THE OTHER HALF OF THIS GATE WAS WIDENED AT v62, AND THE OLD COMMENT WAS RIGHT WHEN IT WAS
    /// WRITTEN.</b> It read: <i>"the only class this product ships is the transfer class, and census row 2.6 — the
    /// general Voucher Class machinery — is ABSENT … a 'Name of Class' box on a Sales type would advertise a
    /// feature that does not exist."</i> Row 2.6 now ships (ledger pre-maps, default accounting allocations,
    /// additional-ledger rules, rounding), so a Sales class no longer advertises anything absent. The list is
    /// still a LIST rather than "any type": a class on an Attendance or Payroll type would offer an item-value
    /// allocation on a voucher that has no item value.</para>
    /// </summary>
    public bool ShowVoucherClasses =>
        IsAltering && SelectedBaseType?.Value is { } b && BaseTypesThatCarryAClass.Contains(b);

    /// <summary>The base kinds a voucher class is offered on. Stock Journal for census 9.9's transfer class; the
    /// invoice-shaped kinds for census 2.6's pre-maps and additional entries.</summary>
    private static readonly IReadOnlySet<VoucherBaseType> BaseTypesThatCarryAClass = new HashSet<VoucherBaseType>
    {
        VoucherBaseType.StockJournal,
        VoucherBaseType.Sales,
        VoucherBaseType.Purchase,
        VoucherBaseType.CreditNote,
        VoucherBaseType.DebitNote,
    };

    /// <summary>True when the vendor's <b>"Use Class for Inter-Godown Transfers"</b> box is offered — a Stock
    /// Journal only. The engine refuses the flag on anything else (<c>VoucherTypeService.AddClass</c>); this keeps
    /// the screen from offering a box whose only outcome is a refusal.</summary>
    public bool ShowInterGodownOption =>
        IsAltering && SelectedBaseType?.Value == VoucherBaseType.StockJournal;

    /// <summary>True when the census-2.6 tables are offered — every class-carrying kind EXCEPT the Stock Journal,
    /// which posts no ledger entry at all and so has nothing to pre-map or round.</summary>
    public bool ShowClassAccountingTables =>
        ShowVoucherClasses && SelectedBaseType?.Value != VoucherBaseType.StockJournal;

    /// <summary>
    /// Adds the class named in <see cref="NewClassName"/> to the type under alteration and saves. Domain refusals
    /// (blank name, duplicate name, the flag on a non-Stock-Journal type) are surfaced to
    /// <see cref="Message"/> without crashing.
    /// </summary>
    public bool AddClass()
    {
        Message = null;
        if (!IsAltering)
        {
            Message = "Save the voucher type first, then alter it to add a class.";
            return false;
        }
        try
        {
            // 🔴 THE FLAG IS ONLY SENT WHEN THE BOX IS ACTUALLY OFFERED. NewClassInterGodownTransfers defaults
            // to TRUE (a Stock Journal class almost always wants it), and the box is hidden on every other base
            // kind — so passing the field through unconditionally made EVERY Sales class fail at the engine with
            // "Use Class for Inter-Godown Transfers applies only to a Stock Journal voucher type", naming an
            // option the operator was never shown and could not turn off.
            var interGodown = ShowInterGodownOption && NewClassInterGodownTransfers;
            new VoucherTypeService(_company)
                .AddClass(_editingId, NewClassName ?? string.Empty, interGodown);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        var added = (NewClassName ?? string.Empty).Trim();
        RefreshClasses();
        NewClassName = string.Empty;
        Message = $"Voucher class '{added}' added.";
        _onChanged();
        return true;
    }

    /// <summary>Removes a class from the type under alteration and saves.</summary>
    public bool RemoveClass(Guid classId)
    {
        Message = null;
        if (!IsAltering) return false;
        try
        {
            new VoucherTypeService(_company).RemoveClass(_editingId, classId);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }
        RefreshClasses();
        _onChanged();
        return true;
    }

    private void RefreshClasses()
    {
        Classes.Clear();
        if (!IsAltering || _company.FindVoucherType(_editingId) is not { } type) return;
        foreach (var c in type.Classes)
            Classes.Add(new VoucherClassListRow
            {
                Id = c.Id,
                Name = c.Name,
                // Spelled out rather than shown as a tick: "Yes/No" under a column captioned with the vendor's
                // own sentence reads correctly when the list is EXPORTED, where a tick glyph does not.
                InterGodownTransfers = c.UseClassForInterGodownTransfers ? "Yes" : "No",
            });

        // If the class the two v62 tables were showing has gone, stop showing its rows.
        if (SelectedClassId is { } id && Classes.All(c => c.Id != id)) SelectedClassId = null;
        RefreshClassAccounting();
    }

    // ---------------------------------- census 2.6 (v62) — the class's own accounting tables

    /// <summary>The class whose Default Accounting Allocations and Additional Accounting Entries the two tables
    /// below are showing. Null until the operator picks one — a class has to exist before its rules can.</summary>
    [ObservableProperty] private Guid? _selectedClassId;

    partial void OnSelectedClassIdChanged(Guid? value)
    {
        OnPropertyChanged(nameof(HasSelectedClass));
        OnPropertyChanged(nameof(SelectedClassName));
        RefreshClassAccounting();
    }

    /// <summary>True once a class is selected — the two v62 tables are meaningless without one.</summary>
    public bool HasSelectedClass => SelectedClassId is not null;

    /// <summary>The selected class's vendor "Name of Class", for the section caption.</summary>
    public string SelectedClassName =>
        SelectedClassId is { } id ? Classes.FirstOrDefault(c => c.Id == id)?.Name ?? string.Empty : string.Empty;

    /// <summary>The vendor's <b>"Default Accounting Allocations for all items in Invoice"</b> for the selected
    /// class (census 2.6).</summary>
    public ObservableCollection<VoucherClassAllocationRow> Allocations { get; } = new();

    /// <summary>The vendor's <b>"Additional Accounting Entries"</b> for the selected class (census 2.6).</summary>
    public ObservableCollection<VoucherClassEntryRow> AdditionalEntries { get; } = new();

    /// <summary>Every ledger in the company, for the two ledger pickers.</summary>
    public ObservableCollection<ClassLedgerOption> LedgerOptions { get; } = new();

    /// <summary>The vendor's <b>Type of Calculation</b> options (census 2.6). Only the members a vendor page
    /// names are offered — see <see cref="VoucherClassCalculationType"/> for why the list is short.</summary>
    public ObservableCollection<ClassCalculationOption> CalculationTypes { get; } = new();

    /// <summary>The vendor's four <b>Rounding Method</b> options.</summary>
    public ObservableCollection<ClassRoundingOption> RoundingMethods { get; } = new();

    [ObservableProperty] private ClassLedgerOption? _newAllocationLedger;
    /// <summary>The vendor's <b>percentage</b> of allocation, as typed. Parsed to BASIS POINTS on add.</summary>
    [ObservableProperty] private string _newAllocationPercentText = string.Empty;

    [ObservableProperty] private ClassLedgerOption? _newEntryLedger;
    [ObservableProperty] private ClassCalculationOption? _newEntryCalculationType;
    /// <summary>The vendor's <b>Value Basis</b>, as typed — a rate per BASE UNIT for "Based on Quantity".</summary>
    [ObservableProperty] private string _newEntryValueBasisText = string.Empty;
    [ObservableProperty] private ClassRoundingOption? _newEntryRoundingMethod;
    /// <summary>The vendor's <b>Rounding Limit</b>, as typed — the multiple the amount snaps to.</summary>
    [ObservableProperty] private string _newEntryRoundingLimitText = string.Empty;
    /// <summary>The vendor's <b>Remove if Zero</b>.</summary>
    [ObservableProperty] private bool _newEntryRemoveIfZero;

    /// <summary>Shows the two v62 tables for one class.</summary>
    public void SelectClass(Guid classId) => SelectedClassId = classId;

    private void RefreshClassAccounting()
    {
        Allocations.Clear();
        AdditionalEntries.Clear();
        OnPropertyChanged(nameof(AllocationTotalCaption));

        if (!IsAltering
            || SelectedClassId is not { } classId
            || _company.FindVoucherType(_editingId) is not { } type
            || type.Classes.FirstOrDefault(c => c.Id == classId) is not { } cls) return;

        foreach (var a in cls.LedgerAllocations)
            Allocations.Add(new VoucherClassAllocationRow
            {
                Id = a.Id,
                Ledger = LedgerName(a.LedgerId),
                // Basis points back to a percentage for display — 3_333 reads as "33.33%".
                Percent = (a.PercentBasisPoints / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%",
            });

        foreach (var e in cls.AdditionalEntries)
            AdditionalEntries.Add(new VoucherClassEntryRow
            {
                Id = e.Id,
                Ledger = LedgerName(e.LedgerId),
                CalculationType = DescribeCalculation(e.CalculationType),
                ValueBasis = e.CalculationType == VoucherClassCalculationType.BasedOnQuantity
                    ? e.ValueBasis.ToString()
                    : string.Empty,
                Rounding = e.RoundingMethod == VoucherClassRoundingMethod.NotApplicable
                    ? "Not Applicable"
                    : $"{DescribeRounding(e.RoundingMethod)} to {e.RoundingLimit}",
                RemoveIfZero = e.RemoveIfZero ? "Yes" : "No",
            });

        OnPropertyChanged(nameof(AllocationTotalCaption));
    }

    /// <summary>
    /// 🔴 <b>THE RUNNING TOTAL, SHOWN BECAUSE THE OPERATOR CANNOT OTHERWISE SEE THE ONE MISTAKE THAT MATTERS.</b>
    /// Allocations must reach exactly 100% or every voucher entered under the class posts out of balance. The
    /// engine refuses an incomplete class at save and at posting, but a refusal after the fact is a worse
    /// experience than a total that visibly reads "80% — must reach 100%" while the table is being keyed.
    /// </summary>
    public string AllocationTotalCaption
    {
        get
        {
            if (SelectedClassId is not { } id
                || _company.FindVoucherType(_editingId) is not { } type
                || type.Classes.FirstOrDefault(c => c.Id == id) is not { } cls
                || cls.LedgerAllocations.Count == 0) return string.Empty;

            var bp = cls.LedgerAllocations.Sum(a => (long)a.PercentBasisPoints);
            var pct = (bp / 100m).ToString("0.##", CultureInfo.InvariantCulture);
            return bp == VoucherClassLedgerAllocation.FullAllocationBasisPoints
                ? $"Allocated: {pct}%"
                : $"Allocated: {pct}% — must reach 100%";
        }
    }

    private string LedgerName(Guid id) => _company.FindLedger(id)?.Name ?? "(deleted ledger)";

    /// <summary>Fills the two ledger pickers. Ordered by name with <see cref="StringComparer.Ordinal"/> — NOT a
    /// culture-sensitive sort, which orders differently on the ubuntu leg of the gate than on the windows one.</summary>
    private void RefreshLedgerOptions()
    {
        LedgerOptions.Clear();
        foreach (var l in _company.Ledgers.OrderBy(l => l.Name, StringComparer.Ordinal))
            LedgerOptions.Add(new ClassLedgerOption { Id = l.Id, Display = l.Name });
    }

    private static string DescribeCalculation(VoucherClassCalculationType t) => t switch
    {
        VoucherClassCalculationType.BasedOnQuantity => "Based on Quantity",
        VoucherClassCalculationType.AsTotalAmountRounding => "As Total Amount Rounding",
        _ => "Not Applicable",
    };

    private static string DescribeRounding(VoucherClassRoundingMethod m) => m switch
    {
        VoucherClassRoundingMethod.Normal => "Normal Rounding",
        VoucherClassRoundingMethod.Upward => "Upward Rounding",
        VoucherClassRoundingMethod.Downward => "Downward Rounding",
        _ => "Not Applicable",
    };

    /// <summary>
    /// Adds one Default Accounting Allocation to the selected class and saves. The typed percentage is converted
    /// to BASIS POINTS — never held as a decimal percentage, for the reason on
    /// <see cref="VoucherClassLedgerAllocation"/>.
    /// </summary>
    public bool AddAllocation()
    {
        Message = null;
        if (SelectedClassId is not { } classId) { Message = "Pick a voucher class first."; return false; }
        if (NewAllocationLedger is null) { Message = "Pick a ledger for the allocation."; return false; }

        if (!TryParsePercentToBasisPoints(NewAllocationPercentText, out var bp))
        {
            Message = "The allocation percentage must be a number between 0 and 100, to at most two decimals.";
            return false;
        }

        try
        {
            new VoucherTypeService(_company)
                .AddClassAllocation(_editingId, classId, NewAllocationLedger.Id, bp);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ArgumentOutOfRangeException)
        {
            Message = ex.Message;
            return false;
        }

        NewAllocationPercentText = string.Empty;
        RefreshClassAccounting();
        _onChanged();
        return true;
    }

    /// <summary>Removes one Default Accounting Allocation and saves.</summary>
    public bool RemoveAllocation(Guid allocationId)
    {
        Message = null;
        if (SelectedClassId is not { } classId) return false;
        try
        {
            new VoucherTypeService(_company).RemoveClassAllocation(_editingId, classId, allocationId);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }
        RefreshClassAccounting();
        _onChanged();
        return true;
    }

    /// <summary>Adds one Additional Accounting Entry to the selected class and saves.</summary>
    public bool AddAdditionalEntry()
    {
        Message = null;
        if (SelectedClassId is not { } classId) { Message = "Pick a voucher class first."; return false; }
        if (NewEntryLedger is null) { Message = "Pick a ledger for the additional accounting entry."; return false; }

        var calc = (NewEntryCalculationType ?? CalculationTypes.FirstOrDefault())?.Value
                   ?? VoucherClassCalculationType.NotApplicable;
        var rounding = (NewEntryRoundingMethod ?? RoundingMethods.FirstOrDefault())?.Value
                       ?? VoucherClassRoundingMethod.NotApplicable;

        if (!TryParseMoney(NewEntryValueBasisText, out var basis))
        {
            Message = "The value basis must be an amount in rupees, to at most two decimals.";
            return false;
        }
        if (!TryParseMoney(NewEntryRoundingLimitText, out var limit))
        {
            Message = "The rounding limit must be an amount in rupees, to at most two decimals.";
            return false;
        }

        try
        {
            new VoucherTypeService(_company).AddClassAdditionalEntry(
                _editingId, classId, NewEntryLedger.Id, calc, basis, rounding, limit, NewEntryRemoveIfZero);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ArgumentOutOfRangeException)
        {
            Message = ex.Message;
            return false;
        }

        NewEntryValueBasisText = string.Empty;
        NewEntryRoundingLimitText = string.Empty;
        NewEntryRemoveIfZero = false;
        RefreshClassAccounting();
        _onChanged();
        return true;
    }

    /// <summary>Removes one Additional Accounting Entry and saves.</summary>
    public bool RemoveAdditionalEntry(Guid entryId)
    {
        Message = null;
        if (SelectedClassId is not { } classId) return false;
        try
        {
            new VoucherTypeService(_company).RemoveClassAdditionalEntry(_editingId, classId, entryId);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }
        RefreshClassAccounting();
        _onChanged();
        return true;
    }

    /// <summary>
    /// Parses the operator's percentage into BASIS POINTS. <b>Invariant culture and two decimals maximum</b>: the
    /// gate runs on ubuntu and macOS too, where a culture-sensitive parse reads "33.33" as thirty-three thousand
    /// three hundred and thirty-three under a comma-decimal locale — which would be a 333 300% allocation.
    ///
    /// <para>🔴 <b>THE ×100 IS DELEGATED TO <see cref="PaisaConversion"/> RATHER THAN WRITTEN HERE, AND THAT IS
    /// NOT A DODGE OF THE DRIFT LOCK — IT IS THE SAME RULE.</b> "Scale by a hundred and refuse anything with a
    /// finer tail" is exactly what rupees→paisa is, and <c>percent</c>→<c>basis points</c> is that arithmetic with
    /// different units: 33.33% is to 3 333 bp precisely what ₹33.33 is to 3 333 paisa. A hand-written
    /// <c>pct * 100m</c> plus a truncation test here would be a second copy of the one rule D3 exists to keep
    /// single — and the copy would be the one that drifts, because nothing else would ever test it.</para>
    /// </summary>
    private static bool TryParsePercentToBasisPoints(string? text, out int basisPoints)
    {
        basisPoints = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!decimal.TryParse(text.Trim().TrimEnd('%'), NumberStyles.Number, CultureInfo.InvariantCulture, out var pct))
            return false;
        if (pct <= 0m || pct > 100m) return false;

        // Finer than a basis point cannot be stored, and TryToPaisaExact is the one place that test lives.
        if (!PaisaConversion.TryToPaisaExact(pct, out var bp)) return false;
        basisPoints = (int)bp;
        return true;
    }

    /// <summary>Parses an optional rupee amount, invariant-culture. Blank is a legitimate zero — the Value Basis
    /// and Rounding Limit are both unused on some calculation types.</summary>
    private static bool TryParseMoney(string? text, out Money money)
    {
        money = Money.Zero;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            return false;
        money = Money.FromRupees(value);
        return money.IsPaisaExact;
    }

    /// <summary>True while the base-kind picker may be changed — Create mode only. On an alteration the base kind
    /// is fixed (see the class remarks: changing it would re-interpret every voucher already posted).</summary>
    public bool CanChooseBaseType => !IsAltering;

    public VoucherTypeMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _highlight = new PayrollMasterHighlight<VoucherTypeListRow>(
            Existing, () => { OnPropertyChanged(nameof(HighlightedRow)); OnPropertyChanged(nameof(HighlightedMasterRow)); });

        foreach (var b in Enum.GetValues<VoucherBaseType>())
            BaseTypes.Add(new VoucherBaseTypeOption { Value = b, Display = DescribeBaseType(b) });

        // census 2.6 (v62) — the two pickers. Only the Type-of-Calculation members a vendor page NAMES are
        // offered; see VoucherClassCalculationType for why that list is deliberately short.
        foreach (var t in new[]
                 {
                     VoucherClassCalculationType.NotApplicable,
                     VoucherClassCalculationType.BasedOnQuantity,
                     VoucherClassCalculationType.AsTotalAmountRounding,
                 })
            CalculationTypes.Add(new ClassCalculationOption { Value = t, Display = DescribeCalculation(t) });

        foreach (var m in new[]
                 {
                     VoucherClassRoundingMethod.NotApplicable,
                     VoucherClassRoundingMethod.Normal,
                     VoucherClassRoundingMethod.Upward,
                     VoucherClassRoundingMethod.Downward,
                 })
            RoundingMethods.Add(new ClassRoundingOption { Value = m, Display = DescribeRounding(m) });

        NewEntryCalculationType = CalculationTypes[0];
        NewEntryRoundingMethod = RoundingMethods[0];
        RefreshLedgerOptions();
        SelectedBaseType = BaseTypes.First();

        // The vendor's own order on the Voucher Type screen, with None (from the numbering-methods page) last.
        NumberingMethods.Add(new NumberingMethodOption { Value = NumberingMethod.Automatic, Display = "Automatic" });
        NumberingMethods.Add(new NumberingMethodOption { Value = NumberingMethod.AutomaticManualOverride, Display = "Automatic (Manual Override)" });
        NumberingMethods.Add(new NumberingMethodOption { Value = NumberingMethod.Manual, Display = "Manual" });
        NumberingMethods.Add(new NumberingMethodOption { Value = NumberingMethod.MultiUserAuto, Display = "Multi-user Auto" });
        NumberingMethods.Add(new NumberingMethodOption { Value = NumberingMethod.None, Display = "None" });
        SelectedNumbering = NumberingMethods.First();

        RefreshList();
    }

    /// <summary>Opens this master in <b>Alter</b> mode over an existing type — the same form, pre-filled. Returns
    /// <c>null</c> if the id does not resolve.</summary>
    public static VoucherTypeMasterViewModel? ForAlter(
        Company company, CompanyStorage storage, Guid typeId, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (company.FindVoucherType(typeId) is not { } type) return null;

        var vm = new VoucherTypeMasterViewModel(company, storage, onChanged);
        vm._editingId = typeId;
        vm.Name = type.Name;
        vm.Abbreviation = type.Abbreviation ?? string.Empty;
        vm.SelectedBaseType = vm.BaseTypes.FirstOrDefault(o => o.Value == type.BaseType) ?? vm.BaseTypes.First();
        vm.SelectedNumbering = vm.NumberingMethods.FirstOrDefault(o => o.Value == type.Numbering)
                               ?? vm.NumberingMethods.First();
        vm.IsActive = type.IsActive;
        vm.PrintAfterSaving = type.PrintAfterSaving;
        vm.ProvideNarrationForEachLedger = type.ProvideNarrationForEachLedger;

        // An alteration must be able to SEE the type it is altering in the list beneath it, and a deactivated
        // type is exactly the one an operator opens the alteration for. Without this, opening an inactive type
        // for alteration would leave the list showing every type except that one.
        if (!type.IsActive) vm.ShowInactive = true;

        vm.OnPropertyChanged(nameof(IsAltering));
        vm.OnPropertyChanged(nameof(Caption));
        vm.OnPropertyChanged(nameof(CanChooseBaseType));
        // W-K1 (census 9.9): fill the class list and tell the view the section is now reachable. ShowVoucherClasses
        // depends on IsAltering AND the base type, and neither raised a notification of its own — without this
        // the section stays hidden on a Stock Journal alteration and 9.9 has no user route at all.
        vm.RefreshClasses();
        vm.OnPropertyChanged(nameof(ShowVoucherClasses));
        return vm;
    }

    /// <summary>
    /// Ctrl+A: creates the type, or commits the alteration the screen is in (the caption says which). Validates a
    /// non-empty name and a chosen base kind + method, then calls the engine, which owns uniqueness; any domain
    /// refusal is surfaced to <see cref="Message"/> without crashing the UI.
    /// </summary>
    public bool Create()
    {
        Message = null;
        var name = (Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "A voucher type name is required.";
            return false;
        }
        if (SelectedBaseType is null)
        {
            Message = "Pick the type of voucher this one is based on.";
            return false;
        }
        if (SelectedNumbering is null)
        {
            Message = "Pick a method of voucher numbering.";
            return false;
        }

        var abbreviation = string.IsNullOrWhiteSpace(Abbreviation) ? null : Abbreviation.Trim();
        var altering = IsAltering;
        try
        {
            var service = new VoucherTypeService(_company);
            if (altering)
                service.Alter(_editingId, name, SelectedNumbering.Value, abbreviation,
                    IsActive, PrintAfterSaving, ProvideNarrationForEachLedger);
            else
                service.Create(name, SelectedBaseType.Value, SelectedNumbering.Value, abbreviation,
                    PrintAfterSaving, ProvideNarrationForEachLedger);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        RefreshList();
        if (altering)
        {
            Message = $"Voucher type '{name}' altered.";
            _onChanged();
            return true;
        }

        Message = $"Voucher type '{name}' created under {SelectedBaseType.Display}.";
        Name = string.Empty;
        Abbreviation = string.Empty;
        IsActive = true;
        PrintAfterSaving = false;
        ProvideNarrationForEachLedger = false;
        _onChanged();
        return true;
    }

    /// <summary>
    /// <b>Space on the highlighted existing-type row — activate / deactivate it (census 5.11).</b> The
    /// single-keystroke half of the Show-Inactive gesture: switch Show Inactive on, arrow to a deactivated type,
    /// press Space, and it is postable again. Returns <c>false</c> (a quiet no-op) when nothing is highlighted or
    /// the screen is mid-alteration — the same rule Alt+D follows, for the same reason.
    /// </summary>
    public bool ToggleActiveOnHighlighted()
    {
        Message = null;
        if (IsAltering) return false;
        if (_highlight.Row is not { } row) return false;
        if (_company.FindVoucherType(row.MasterId) is not { } type) return false;

        var nowActive = !type.IsActive;
        try
        {
            new VoucherTypeService(_company).SetActive(type.Id, nowActive);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        RefreshList();
        Message = nowActive
            ? $"Voucher type '{type.Name}' activated."
            : $"Voucher type '{type.Name}' deactivated — it no longer appears for entry. "
              + "Switch Show Inactive on to find it again.";
        _onChanged();
        return true;
    }

    private void RefreshList()
    {
        // By id, never by index — see PayrollMasterHighlight.RestoreTo for why.
        var previous = _highlight.IdBeforeRebuild();

        Existing.Clear();
        foreach (var t in _company.VoucherTypes)
        {
            if (!t.IsActive && !ShowInactive) continue;
            Existing.Add(new VoucherTypeListRow
            {
                MasterId = t.Id,
                Name = t.Name,
                BaseType = DescribeBaseType(t.BaseType),
                Numbering = DescribeNumbering(t.Numbering),
                Abbreviation = t.Abbreviation ?? "—",
                Active = t.IsActive ? "Yes" : "No",
            });
        }

        _highlight.RestoreTo(previous);
        OnPropertyChanged(nameof(InactiveCount));
        OnPropertyChanged(nameof(ShowInactiveHint));
    }

    /// <summary>How many voucher types are currently switched OFF — the number the Show-Inactive hint reports, so
    /// an operator can tell "there are none" from "they are hidden".</summary>
    public int InactiveCount => _company.VoucherTypes.Count(t => !t.IsActive);

    /// <summary>The line under the list telling the operator what Show Inactive and Space do, with the live
    /// inactive count folded in.</summary>
    public string ShowInactiveHint => InactiveCount == 0
        ? "Space activates or deactivates the highlighted type. No voucher types are currently inactive."
        : ShowInactive
            ? $"Space activates or deactivates the highlighted type. {Plural(InactiveCount)} currently inactive."
            : $"{Plural(InactiveCount)} hidden — switch Show Inactive on to see and activate them.";

    private static string Plural(int n) => n == 1 ? "1 voucher type is" : $"{n} voucher types are";

    /// <summary>"StockJournal" → "Stock Journal". The enum names are PascalCase compounds and an operator has
    /// never seen an enum.</summary>
    private static string DescribeBaseType(VoucherBaseType baseType)
    {
        var raw = baseType.ToString();
        var sb = new System.Text.StringBuilder(raw.Length + 4);
        for (var i = 0; i < raw.Length; i++)
        {
            if (i > 0 && char.IsUpper(raw[i]) && !char.IsUpper(raw[i - 1])) sb.Append(' ');
            sb.Append(raw[i]);
        }
        return sb.ToString();
    }

    /// <summary>The vendor's own caption for each method — used in the list column AND in the picker, so the two
    /// can never read differently.</summary>
    public static string DescribeNumbering(NumberingMethod method) => method switch
    {
        NumberingMethod.Automatic => "Automatic",
        NumberingMethod.AutomaticManualOverride => "Automatic (Manual Override)",
        NumberingMethod.Manual => "Manual",
        NumberingMethod.MultiUserAuto => "Multi-user Auto",
        NumberingMethod.None => "None",
        _ => method.ToString(),
    };
}
