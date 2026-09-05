using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A voucher-type row for the existing-types list on the master screen.</summary>
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
