using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Apex.Desktop.ViewModels;

/// <summary>The outcome of a numbering-config <see cref="VoucherNumberingConfigViewModel.Save"/> attempt.</summary>
public enum NumberingSaveResult
{
    /// <summary>The config was validated and persisted through the S3 store path.</summary>
    Saved,

    /// <summary>A hard validation error (duplicate <c>ApplicableFrom</c>, bad width) — nothing persisted.</summary>
    Rejected,

    /// <summary>The edit would rewrite the document number of an already-filed (e-invoiced / e-Way) voucher — refused
    /// (numbering-design-v2 §5.4; a filed statutory number cannot change).</summary>
    Blocked,

    /// <summary>The edit changes the displayed/printed number of already-issued (posted, unfiled) vouchers — the caller
    /// must <see cref="VoucherNumberingConfigViewModel.ConfirmSave"/> to proceed (numbering-design-v2 §5.4).</summary>
    NeedsConfirmation,
}

/// <summary>
/// One date-effective affix row in the F12 numbering editor (numbering-design-v2 §5.2 N3): an
/// <see cref="ApplicableFrom"/> date and the full <see cref="Particulars"/> text (separators included). The date is
/// edited keyboard-first through the app-wide lenient <see cref="ApexDate"/> contract via <see cref="ApplicableFromText"/>.
/// </summary>
public sealed partial class AffixRowViewModel : ViewModelBase
{
    /// <summary>Stable surrogate key — carried through to the persisted <see cref="VoucherNumberAffix.Id"/> so the
    /// formatter's same-date tie-break stays deterministic (numbering-design-v2 §2.1).</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    [ObservableProperty] private DateOnly _applicableFrom;
    [ObservableProperty] private string _particulars = string.Empty;

    /// <summary>The date as shown/typed — renders through <see cref="ApexDate.Format"/> and parses through the lenient
    /// <see cref="ApexDate.TryParse(string?, DateOnly, out DateOnly)"/> ladder (WI-5), so the affix editor uses the one
    /// app-wide date contract like every other date field. A value that does not parse leaves the date unchanged.</summary>
    public string ApplicableFromText
    {
        get => ApexDate.Format(ApplicableFrom);
        set { if (ApexDate.TryParse(value, ApplicableFrom, out var d)) ApplicableFrom = d; }
    }

    partial void OnApplicableFromChanged(DateOnly value) => OnPropertyChanged(nameof(ApplicableFromText));
}

/// <summary>
/// The <b>F12 voucher-numbering configuration</b> page (numbering-design-v2 §5; §9 S4). Pushed as a cascade column to
/// the RIGHT of a voucher-entry context (prior panes persist; F12/Esc pops it — see
/// <see cref="MainWindowViewModel.OpenVoucherNumberingConfig"/>). It lists the company's voucher types (N1) and, for the
/// selected type, its numbering fields (N2) — Method (display-only this slice), Prevent-duplicate, Width, Prefill-with-zero —
/// and the date-keyed Prefix / Suffix row editors (N3). Committing writes the S3-persisted fields back to the live
/// <see cref="VoucherType"/> (via <see cref="VoucherType.SetAffixes"/> + the scalar setters) and saves through the existing
/// <see cref="CompanyStorage"/> path — no new persistence.
///
/// <para><b>Validation on commit</b> (numbering-design-v2 §5.3/§5.4):
/// (a) a duplicate <c>ApplicableFrom</c> within one affix kind is <see cref="NumberingSaveResult.Rejected"/>;
/// (b) a digit-adjacent boundary (a Prefix ending in a digit or a Suffix starting with one) yields a NON-blocking
/// warning but still saves — a legitimate numeric prefix like <c>"2025"</c> is never refused;
/// (c) an edit/delete of an existing affix row, or a Width/Prefill change, that would retroactively change the rendered
/// number of an already-posted voucher of the type is <see cref="NumberingSaveResult.Blocked"/> when any affected voucher
/// carries a generated e-invoice / e-Way record, else <see cref="NumberingSaveResult.NeedsConfirmation"/>. ADDING a
/// future-dated row that covers no already-posted voucher is always allowed (the date-effective model only touches future
/// vouchers), so it never warns.</para>
/// </summary>
public sealed partial class VoucherNumberingConfigViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action? _onSaved;

    public VoucherNumberingConfigViewModel(Company company, CompanyStorage storage, Action? onSaved = null)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onSaved = onSaved;

        Types = new ObservableCollection<VoucherType>(_company.VoucherTypes);
        if (Types.Count > 0) SelectedType = Types[0];
    }

    /// <summary>The column header.</summary>
    public string Title => "Voucher Numbering — Configure";

    // =========================================================== N1 — the voucher-type list

    /// <summary>Every voucher type on the company (the 24 seeds + any custom), inventory/pure-stock types included.</summary>
    public ObservableCollection<VoucherType> Types { get; }

    /// <summary>The type whose numbering is being edited (drives the N2/N3 editors).</summary>
    [ObservableProperty] private VoucherType? _selectedType;

    /// <summary>The N1 highlight index — kept in step with <see cref="SelectedType"/> so the shell's arrow navigation
    /// (<see cref="MoveHighlight"/>) and the bound list agree.</summary>
    [ObservableProperty] private int _selectedIndex = -1;

    // =========================================================== N2 — the per-type numbering fields (working copy)

    [ObservableProperty] private bool _preventDuplicate;
    [ObservableProperty] private string _widthText = "0";
    [ObservableProperty] private bool _prefillWithZero;

    /// <summary>The numbering Method, DISPLAY-ONLY this slice (Automatic / Manual / None) — editing it is deferred (S5+).</summary>
    public string MethodDisplay => SelectedType is null ? string.Empty : SelectedType.Numbering.ToString();

    /// <summary>False when the selected type's Method is <see cref="NumberingMethod.None"/> — the Width/affix editors are
    /// then meaningless and greyed (Prevent-duplicate stays available).</summary>
    public bool CanConfigureAffixes => SelectedType is { Numbering: not NumberingMethod.None };

    // =========================================================== N3 — the date-keyed affix editors (working copy)

    public ObservableCollection<AffixRowViewModel> Prefixes { get; } = new();
    public ObservableCollection<AffixRowViewModel> Suffixes { get; } = new();

    // =========================================================== status + the §5.4 confirmation gate

    [ObservableProperty] private string _message = string.Empty;

    /// <summary>True while a warn-and-confirm is pending — the caller must <see cref="ConfirmSave"/> to proceed.</summary>
    [ObservableProperty] private bool _isConfirmPending;

    /// <summary>The text of the pending warn-and-confirm question (empty when none).</summary>
    [ObservableProperty] private string _confirmText = string.Empty;

    // =========================================================== selection plumbing

    partial void OnSelectedTypeChanged(VoucherType? value) => LoadFromType();

    /// <summary>Selects the type with the given id (used to pre-select the entry screen's type when F12 opens).</summary>
    public void SelectByTypeId(Guid id)
    {
        var t = Types.FirstOrDefault(x => x.Id == id);
        if (t is not null) SelectedType = t;
    }

    /// <summary>Arrow Up/Down over the N1 type list (routed from the shell's <c>StepActive</c>, mirroring every other
    /// arrow-navigable page column). Wraps; a no-op with no types.</summary>
    public void MoveHighlight(int direction)
    {
        if (Types.Count == 0) return;
        var idx = SelectedIndex < 0 ? (direction > 0 ? -1 : 0) : SelectedIndex;
        idx = ((idx + direction) % Types.Count + Types.Count) % Types.Count;
        SelectedType = Types[idx];
    }

    // Loads the working editors from the selected type. Called on every selection change; discards uncommitted edits of
    // the previously-selected type by design (the commit is explicit, per Tally's per-type configure).
    private void LoadFromType()
    {
        Prefixes.Clear();
        Suffixes.Clear();
        Message = string.Empty;
        IsConfirmPending = false;
        ConfirmText = string.Empty;

        if (SelectedType is { } t)
        {
            PreventDuplicate = t.PreventDuplicate;
            WidthText = t.NumberWidth.ToString(CultureInfo.InvariantCulture);
            PrefillWithZero = t.PrefillWithZero;
            foreach (var a in t.Prefixes.OrderBy(a => a.ApplicableFrom).ThenBy(a => a.Id))
                Prefixes.Add(new AffixRowViewModel { Id = a.Id, ApplicableFrom = a.ApplicableFrom, Particulars = a.Particulars });
            foreach (var a in t.Suffixes.OrderBy(a => a.ApplicableFrom).ThenBy(a => a.Id))
                Suffixes.Add(new AffixRowViewModel { Id = a.Id, ApplicableFrom = a.ApplicableFrom, Particulars = a.Particulars });
            SelectedIndex = Types.IndexOf(t);
        }
        else
        {
            PreventDuplicate = false;
            WidthText = "0";
            PrefillWithZero = false;
            SelectedIndex = -1;
        }

        OnPropertyChanged(nameof(MethodDisplay));
        OnPropertyChanged(nameof(CanConfigureAffixes));
    }

    // =========================================================== N3 add / delete (keyboard buttons)

    /// <summary>The default date a freshly-appended affix row takes — the company books-begin, so a first prefix
    /// covers the whole current book by default.</summary>
    private DateOnly DefaultRowDate() => _company.BooksBeginFrom;

    public AffixRowViewModel AddPrefixRow(DateOnly applicableFrom, string particulars = "")
    {
        var row = new AffixRowViewModel { ApplicableFrom = applicableFrom, Particulars = particulars };
        Prefixes.Add(row);
        return row;
    }

    public AffixRowViewModel AddSuffixRow(DateOnly applicableFrom, string particulars = "")
    {
        var row = new AffixRowViewModel { ApplicableFrom = applicableFrom, Particulars = particulars };
        Suffixes.Add(row);
        return row;
    }

    /// <summary>Appends a blank prefix row dated the books-begin (the "Add Prefix Row" button).</summary>
    public AffixRowViewModel AddPrefixRow() => AddPrefixRow(DefaultRowDate());

    /// <summary>Appends a blank suffix row dated the books-begin (the "Add Suffix Row" button).</summary>
    public AffixRowViewModel AddSuffixRow() => AddSuffixRow(DefaultRowDate());

    public void RemovePrefixRow(AffixRowViewModel row) => Prefixes.Remove(row);
    public void RemoveSuffixRow(AffixRowViewModel row) => Suffixes.Remove(row);

    // ---- view commands (the keyboard-reachable buttons on the N3 editors + the save/confirm bar) ----

    [RelayCommand] private void AddPrefix() => AddPrefixRow();
    [RelayCommand] private void AddSuffix() => AddSuffixRow();
    [RelayCommand] private void RemovePrefix(AffixRowViewModel? row) { if (row is not null) RemovePrefixRow(row); }
    [RelayCommand] private void RemoveSuffix(AffixRowViewModel? row) { if (row is not null) RemoveSuffixRow(row); }
    [RelayCommand] private void RunSave() => Save();
    [RelayCommand] private void RunConfirm() => ConfirmSave();

    // =========================================================== commit

    /// <summary>
    /// Validates and (when clear) persists the working numbering config for <see cref="SelectedType"/>. Returns the
    /// outcome (numbering-design-v2 §5.3/§5.4); on <see cref="NumberingSaveResult.NeedsConfirmation"/> nothing is written
    /// until <see cref="ConfirmSave"/> is called.
    /// </summary>
    public NumberingSaveResult Save()
    {
        IsConfirmPending = false;
        ConfirmText = string.Empty;

        if (SelectedType is not { } type)
        {
            Message = "Select a voucher type to configure its numbering.";
            return NumberingSaveResult.Rejected;
        }

        // (a) duplicate ApplicableFrom within one affix kind — the UI forbids it (belt-and-braces to the formatter tie-break).
        if (HasDuplicateDate(Prefixes))
        {
            Message = "Two Prefix rows share the same 'Applicable From' date — each date may appear once.";
            return NumberingSaveResult.Rejected;
        }
        if (HasDuplicateDate(Suffixes))
        {
            Message = "Two Suffix rows share the same 'Applicable From' date — each date may appear once.";
            return NumberingSaveResult.Rejected;
        }

        if (!TryParseWidth(out var width))
        {
            Message = "Width of numerical part must be a whole number (0 = no left-pad).";
            return NumberingSaveResult.Rejected;
        }

        var newPrefixes = ToAffixes(Prefixes);
        var newSuffixes = ToAffixes(Suffixes);

        // (c) historical-stability guard — does this change any already-posted voucher's rendered number? BOTH engines:
        // accounting vouchers (which may carry a filed e-invoice / e-Way signal ⇒ Block) AND pure-inventory vouchers
        // (Delivery Note, Stock Journal, Physical Stock, Receipt/Order, Material In/Out) which render the SAME affix via
        // Company.FormatVoucherNumber(InventoryVoucher) yet can never be e-invoiced / e-Way (their filed-signal FKs
        // reference vouchers(id)), so an affected inventory voucher only ever contributes to the warn-and-confirm count.
        var affected = AffectedPostedVouchers(type, width, PrefillWithZero, newPrefixes, newSuffixes);
        var affectedInventory = AffectedPostedInventoryVouchers(type, width, PrefillWithZero, newPrefixes, newSuffixes);
        var affectedCount = affected.Count + affectedInventory.Count;
        if (affectedCount > 0)
        {
            var filed = affected.Count(IsFiledDocument); // inventory vouchers are never filed — only accounting ones can Block
            if (filed > 0)
            {
                Message = $"{filed} filed document(s) (e-invoice / e-Way) would have their document number rewritten — "
                        + "a filed number cannot change. Change blocked.";
                return NumberingSaveResult.Blocked;
            }

            ConfirmText = $"This changes the displayed/printed number of {affectedCount} already-issued voucher(s). Continue?";
            Message = ConfirmText;
            IsConfirmPending = true;
            return NumberingSaveResult.NeedsConfirmation;
        }

        return Commit(type, width, newPrefixes, newSuffixes);
    }

    /// <summary>Proceeds with a warn-and-confirm save after <see cref="Save"/> returned
    /// <see cref="NumberingSaveResult.NeedsConfirmation"/> (numbering-design-v2 §5.4).</summary>
    public NumberingSaveResult ConfirmSave()
    {
        if (!IsConfirmPending || SelectedType is not { } type)
        {
            Message = "Nothing to confirm.";
            return NumberingSaveResult.Rejected;
        }
        if (!TryParseWidth(out var width))
        {
            Message = "Width of numerical part must be a whole number (0 = no left-pad).";
            return NumberingSaveResult.Rejected;
        }
        return Commit(type, width, ToAffixes(Prefixes), ToAffixes(Suffixes));
    }

    // Writes the working config back to the LIVE type (same instance) and saves through the S3 store path.
    private NumberingSaveResult Commit(VoucherType type, int width,
        List<VoucherNumberAffix> prefixes, List<VoucherNumberAffix> suffixes)
    {
        type.PreventDuplicate = PreventDuplicate;
        type.NumberWidth = width;
        type.PrefillWithZero = PrefillWithZero;
        type.SetAffixes(prefixes, suffixes);

        _storage.Save(_company);
        _onSaved?.Invoke();

        var warning = DigitAdjacentWarning(prefixes, suffixes);
        LoadFromType(); // re-sort the working rows to canonical (ApplicableFrom, Id) order; clears Message + confirm state
        Message = warning is null                       // …so set the save confirmation AFTER the reload
            ? $"Saved numbering for {type.Name}."
            : $"Saved numbering for {type.Name}. {warning}";
        return NumberingSaveResult.Saved;
    }

    // =========================================================== validation helpers

    private bool TryParseWidth(out int width) =>
        int.TryParse((WidthText ?? "0").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out width) && width >= 0;

    private static bool HasDuplicateDate(IEnumerable<AffixRowViewModel> rows) =>
        rows.GroupBy(r => r.ApplicableFrom).Any(g => g.Count() > 1);

    private static List<VoucherNumberAffix> ToAffixes(IEnumerable<AffixRowViewModel> rows) =>
        rows.Select(r => new VoucherNumberAffix(r.Id, r.ApplicableFrom, r.Particulars ?? string.Empty)).ToList();

    /// <summary>The non-blocking digit-adjacent boundary warning (numbering-design-v2 §5.3(b)), or <c>null</c> when the
    /// affixes are unambiguous. A Prefix ending in a digit or a Suffix starting with one can fuse with the numeric core.</summary>
    private static string? DigitAdjacentWarning(
        IReadOnlyList<VoucherNumberAffix> prefixes, IReadOnlyList<VoucherNumberAffix> suffixes)
    {
        var prefixEndsDigit = prefixes.Any(a => a.Particulars.Length > 0 && char.IsDigit(a.Particulars[^1]));
        var suffixStartsDigit = suffixes.Any(a => a.Particulars.Length > 0 && char.IsDigit(a.Particulars[0]));
        return prefixEndsDigit || suffixStartsDigit
            ? "Warning: a digit-adjacent boundary can create ambiguous numbers; consider ending with a separator like / or -."
            : null;
    }

    // The already-posted vouchers of the type whose RENDERED number would change under the proposed config. Compares the
    // live type (still holding the OLD config — this runs before Commit) against a throwaway proposed type. Optional
    // (draft) vouchers are excluded; cancelled ones keep a real document number and are included.
    private List<Voucher> AffectedPostedVouchers(VoucherType type, int newWidth, bool newPrefill,
        List<VoucherNumberAffix> newPrefixes, List<VoucherNumberAffix> newSuffixes)
    {
        var proposed = new VoucherType(type.Id, type.Name, type.BaseType,
            numbering: type.Numbering, numberWidth: newWidth, prefillWithZero: newPrefill,
            prefixes: newPrefixes, suffixes: newSuffixes);

        var affected = new List<Voucher>();
        foreach (var v in _company.Vouchers)
        {
            if (v.TypeId != type.Id || v.Optional) continue;
            var oldR = VoucherNumberFormatter.Render(type, v.Number, v.Date);
            var newR = VoucherNumberFormatter.Render(proposed, v.Number, v.Date);
            if (!string.Equals(oldR, newR, StringComparison.Ordinal)) affected.Add(v);
        }
        return affected;
    }

    // The already-posted PURE-INVENTORY vouchers of the type (Delivery Note, Stock Journal, Physical Stock, orders,
    // Material In/Out) whose RENDERED number would change under the proposed config — the second (inventory) engine's
    // analogue of AffectedPostedVouchers, using the same did-this-rendered-number-change test. These live in
    // _company.InventoryVouchers and render their affix via Company.FormatVoucherNumber(InventoryVoucher); an inventory
    // voucher can never carry a filed e-invoice / e-Way record, so it never Blocks — only warn-and-confirm. Cancelled ones
    // (Alt+X) keep a real document number in sequence and are included, mirroring the accounting scan.
    private List<InventoryVoucher> AffectedPostedInventoryVouchers(VoucherType type, int newWidth, bool newPrefill,
        List<VoucherNumberAffix> newPrefixes, List<VoucherNumberAffix> newSuffixes)
    {
        var proposed = new VoucherType(type.Id, type.Name, type.BaseType,
            numbering: type.Numbering, numberWidth: newWidth, prefillWithZero: newPrefill,
            prefixes: newPrefixes, suffixes: newSuffixes);

        var affected = new List<InventoryVoucher>();
        foreach (var v in _company.InventoryVouchers)
        {
            if (v.TypeId != type.Id) continue;
            var oldR = VoucherNumberFormatter.Render(type, v.Number, v.Date);
            var newR = VoucherNumberFormatter.Render(proposed, v.Number, v.Date);
            if (!string.Equals(oldR, newR, StringComparison.Ordinal)) affected.Add(v);
        }
        return affected;
    }

    // True when a voucher carries a filed statutory document whose number is legally frozen (numbering-design-v2 §5.4).
    // For e-invoicing the frozen signal is any status that REACHED the IRP: GENERATED (IRN issued) OR CANCELLED (the IRN
    // was reported and is permanently burned — a cancelled doc-no is never reusable, §2.5). Pending/Failed never reached
    // the IRP, so they stay on the warn-and-confirm path. An active (non-Cancelled) e-Way bill likewise freezes the
    // number. (A filed-GSTR-1-period signal is not tracked in the domain model, so it is not consulted here.)
    private bool IsFiledDocument(Voucher v)
    {
        if (_company.FindEInvoiceRecordForVoucher(v.Id) is { Status: EInvoiceStatus.Generated or EInvoiceStatus.Cancelled }) return true;
        if (_company.FindEWayBillRecordForVoucher(v.Id) is not null) return true; // finder already excludes Cancelled
        return false;
    }
}
