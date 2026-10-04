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

/// <summary>One editable slab row of the Price List master grid: a quantity band (From/To) with a per-unit rate
/// and an optional discount %. A blank To means the open-ended top slab. Parsing/validation is deferred to the
/// engine on Save; this row only holds the typed text and raises change notifications so the parent keeps a
/// trailing blank row.</summary>
public sealed partial class PriceListSlabRowViewModel : ViewModelBase
{
    private readonly Action _onChanged;

    [ObservableProperty] private string _fromText = string.Empty;
    [ObservableProperty] private string _toText = string.Empty;
    [ObservableProperty] private string _rateText = string.Empty;
    [ObservableProperty] private string _discountText = string.Empty;

    public PriceListSlabRowViewModel(Action onChanged)
        => _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

    partial void OnFromTextChanged(string value) => _onChanged();
    partial void OnToTextChanged(string value) => _onChanged();
    partial void OnRateTextChanged(string value) => _onChanged();
    partial void OnDiscountTextChanged(string value) => _onChanged();

    /// <summary>True once any field is touched; a wholly blank trailing row is ignored on Save.</summary>
    public bool IsBlank =>
        string.IsNullOrWhiteSpace(FromText) && string.IsNullOrWhiteSpace(ToText)
        && string.IsNullOrWhiteSpace(RateText) && string.IsNullOrWhiteSpace(DiscountText);
}

/// <summary>A dated version row shown in the append-only history list on the master screen.</summary>
public sealed partial class PriceListVersionRow : ObservableObject, IMasterListRow
{
    public string ApplicableFrom { get; init; } = string.Empty;
    public string Slabs { get; init; } = string.Empty;

    /// <inheritdoc/>
    public Guid MasterId { get; init; }

    /// <summary><inheritdoc/>
    /// <para>"Wholesale / Widget applicable from 01-Apr-2026". A version has no name — it is identified by the
    /// (level, item) pair it prices plus its applicable-from date — and the pair is NOT redundant just because
    /// the form above the list currently shows it: the confirmation is the last thing the operator reads before
    /// an irreversible act, and it should be complete on its own.</para></summary>
    public string MasterName { get; init; } = string.Empty;

    /// <inheritdoc/>
    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>
/// The <b>Price List</b> creation master ("Masters → Create → Inventory Masters → Price List"; Phase 6 slice 5;
/// RQ-27/RQ-28; Book pp.33–34): pick a <see cref="PriceLevel"/> + an inventory <see cref="StockItem"/>,
/// enter an <b>Applicable-From</b> date and one or more quantity slabs (From / To / Rate / Discount %), then
/// Save — which <b>appends a new dated version</b> via <see cref="PriceListService.AddOrReviseList"/> (a revision
/// never overwrites; RQ-27). The screen shows the existing dated versions for the chosen (level, item) so a
/// revision is visibly an append.
///
/// <para>Gated by <see cref="Company.EnableMultiplePriceLevels"/> (RQ-52) — a non-price-level company never
/// reaches it (ER-13). MVVM boundary: domain + persistence only, no Avalonia types ⇒ headlessly testable.</para>
/// </summary>
public sealed partial class PriceListsViewModel : ViewModelBase, IMasterListScreen
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    // ------------------------------------------------- W33 C3 (census 3.11): the shared master-list arm
    //
    // 🔴 THE LIST THE ARROWS WALK IS THE VERSION HISTORY, AND THE HISTORY IS SCOPED TO THE CHOSEN (level, item).
    // That is the only list on this screen that holds deletable records; the Levels and Items collections are
    // pickers, not masters, and the Slabs collection is an unsaved edit buffer. So Alt+D here withdraws ONE
    // dated version — precisely the half of row 3.11's gap ("no route deletes a list or a version") that has a
    // record behind it. Changing the level or the item rebuilds the history, which drops the highlight, so the
    // chord can never act on a version the operator is no longer looking at.

    /// <inheritdoc/>
    public string MasterKindLabel => "price list";

    /// <summary>The dated version this screen is correcting in place, or <see cref="Guid.Empty"/> when creating.
    /// Set only by <see cref="ForAlter"/>.</summary>
    private Guid _editingId;

    /// <inheritdoc/>
    /// <remarks>🔴 Was the constant <c>false</c> with a remark saying this screen is "create-only — a revision is
    /// a NEW dated version, not an Alter mode" until census 3.11 wired <see cref="ForAlter"/>. That remark was
    /// true of REVISION and wrongly generalised to ALTERATION; the vendor documents both. See
    /// <see cref="PriceListService.AlterList"/> for the distinction and the citation.</remarks>
    public bool IsAltering => _editingId != Guid.Empty;

    /// <summary>Screen caption — mirrors the vendor's Creation / Alteration pair.</summary>
    public string Caption => IsAltering ? "Price List Alteration" : "Price List Creation";

    /// <inheritdoc/>
    public IMasterListRow? HighlightedMasterRow => HighlightedRow;

    /// <inheritdoc/>
    public void ReloadExisting() => RefreshHistory();

    /// <inheritdoc/>
    /// <remarks>Engine-only — the shell saves and reloads after this returns. See
    /// <c>PriceListService.DeleteList</c> for why no referential guard is owed here and for the one real
    /// consequence (the preceding version's date range widens).</remarks>
    public void DeleteMaster(Guid id) => new PriceListService(_company).DeleteList(id);

    private PayrollMasterHighlight<PriceListVersionRow>? _highlight;

    private PayrollMasterHighlight<PriceListVersionRow> Highlight =>
        _highlight ??= new PayrollMasterHighlight<PriceListVersionRow>(
            History, () => OnPropertyChanged(nameof(HighlightedRow)));

    /// <summary>The arrow-highlighted existing price-list version, or null.</summary>
    public PriceListVersionRow? HighlightedRow => Highlight.Row;

    /// <inheritdoc/>
    public void MoveHighlight(int direction) => Highlight.Move(direction);

    /// <summary>The price levels to price against (all defined levels).</summary>
    public ObservableCollection<PriceLevel> Levels { get; } = new();

    /// <summary>The inventory items a price list can price (RQ-31, inventory items only).</summary>
    public ObservableCollection<StockItem> Items { get; } = new();

    /// <summary>The editable slab rows (From / To / Rate / Discount %); always one blank trailing row.</summary>
    public ObservableCollection<PriceListSlabRowViewModel> Slabs { get; } = new();

    /// <summary>The existing dated versions for the chosen (level, item) — the append-only history (RQ-27).</summary>
    public ObservableCollection<PriceListVersionRow> History { get; } = new();

    [ObservableProperty] private PriceLevel? _selectedLevel;
    [ObservableProperty] private StockItem? _selectedItem;
    [ObservableProperty] private string _applicableFromText = string.Empty;
    [ObservableProperty] private string? _message;

    public PriceListsViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        foreach (var l in company.PriceLevels.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            Levels.Add(l);
        foreach (var i in company.StockItems.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            Items.Add(i);

        _selectedLevel = Levels.FirstOrDefault();
        _selectedItem = Items.FirstOrDefault();

        // Default the Applicable-From to the last voucher date (or books-begin), the same default the entry
        // screens use — a sensible "as of today's books" starting point.
        var last = company.Vouchers.Count == 0 ? (DateOnly?)null : company.Vouchers.Max(v => v.Date);
        var applicable = last ?? company.BooksBeginFrom;
        _applicableFromText = ApexDate.Format(applicable);

        AddSlabRow();          // one blank trailing row ready to type into
        RefreshHistory();
    }

    partial void OnSelectedLevelChanged(PriceLevel? value) => RefreshHistory();
    partial void OnSelectedItemChanged(StockItem? value) => RefreshHistory();

    /// <summary>Adds a blank slab row; keeps exactly one trailing blank row.</summary>
    public PriceListSlabRowViewModel AddSlabRow()
    {
        var row = new PriceListSlabRowViewModel(OnSlabChanged);
        Slabs.Add(row);
        return row;
    }

    private void OnSlabChanged()
    {
        if (Slabs.Count == 0 || !Slabs[^1].IsBlank) AddSlabRow();
    }

    /// <summary>
    /// Ctrl+A save: parses the Applicable-From date + the non-blank slab rows and appends a dated version via
    /// <see cref="PriceListService.AddOrReviseList"/> (append-only; RQ-27). The engine validates the slabs
    /// (contiguous / ascending / one open-ended top slab / paisa-exact rate / discount in [0,100)) and that the
    /// date is strictly later than the newest existing version; any error is surfaced to <see cref="Message"/>
    /// without crashing the UI.
    /// </summary>
    public bool Save()
    {
        Message = null;
        if (!TryBuildEntry(out var applicableFrom, out var slabs)) return false;

        try
        {
            var service = new PriceListService(_company);
            service.AddOrReviseList(SelectedLevel!.Id, SelectedItem!.Id, applicableFrom, slabs);
            _storage.Save(_company);
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
            return false;
        }

        RefreshHistory();
        Message = $"Price list for '{SelectedItem!.Name}' under '{SelectedLevel!.Name}' " +
                  $"saved (applicable from {applicableFrom:dd-MMM-yyyy}).";

        // Reset the slab grid for the next entry (keep the level/item/date so a quick revision is easy).
        Slabs.Clear();
        AddSlabRow();
        _onChanged();
        return true;
    }

    /// <summary>
    /// Opens an EXISTING dated version for <b>alteration</b> (census 3.11), loading its level, item,
    /// applicable-from date and slab rows into the screen; returns <c>null</c> when the id does not resolve.
    /// Mirrors <c>PriceLevelsViewModel.ForAlter</c>.
    ///
    /// <para>🔴 <b>The level and item pickers are loaded but the PAIR is not alterable</b> — moving a priced
    /// version onto a different item is not an edit of that version, it is a different price list, and
    /// <see cref="PriceListService.AlterList"/> keeps both ids from the stored row rather than from the screen.
    /// Changing the pickers mid-alteration therefore rebuilds the history and <see cref="Alter"/> still corrects
    /// the version it was opened over.</para>
    /// </summary>
    public static PriceListsViewModel? ForAlter(
        Company company, CompanyStorage storage, Guid priceListId, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(company);
        var list = company.PriceLists.FirstOrDefault(pl => pl.Id == priceListId);
        if (list is null) return null;

        var vm = new PriceListsViewModel(company, storage, onChanged);
        vm._editingId = priceListId;
        vm.SelectedLevel = vm.Levels.FirstOrDefault(l => l.Id == list.PriceLevelId);
        vm.SelectedItem = vm.Items.FirstOrDefault(i => i.Id == list.StockItemId);
        vm.ApplicableFromText = ApexDate.Format(list.ApplicableFrom);

        // Load the stored slabs into editable rows, then the single blank trailing row the grid contract wants.
        //
        // 🔴 THE GRID IS NORMALISED AFTERWARDS RATHER THAN BUILT PERFECTLY, and that is deliberate: every text
        // setter raises OnSlabChanged, which appends a blank row the moment the LAST row stops being blank. Filling
        // row N therefore grows a blank row that filling row N+1 then strands in the MIDDLE of the band list. The
        // parse ignores blanks, so this would not have produced a wrong price — it would have shown the operator a
        // gap in the middle of a contiguous slab ladder, on a screen whose whole contract is that the bands are
        // contiguous. Stripping blanks once at the end and re-adding exactly one trailing row is immune to the
        // ordering of the notifications.
        vm.Slabs.Clear();
        foreach (var slab in list.Slabs)
        {
            var row = vm.AddSlabRow();
            row.FromText = slab.FromQty.ToString("0.######", CultureInfo.InvariantCulture);
            row.ToText = slab.ToQty is { } to ? to.ToString("0.######", CultureInfo.InvariantCulture) : string.Empty;
            row.RateText = slab.Rate.Amount.ToString("0.00", CultureInfo.InvariantCulture);
            row.DiscountText = slab.DiscountPercent > 0m
                ? slab.DiscountPercent.ToString("0.###", CultureInfo.InvariantCulture)
                : string.Empty;
        }
        for (var i = vm.Slabs.Count - 1; i >= 0; i--)
            if (vm.Slabs[i].IsBlank) vm.Slabs.RemoveAt(i);
        vm.AddSlabRow();

        vm.OnPropertyChanged(nameof(IsAltering));
        vm.OnPropertyChanged(nameof(Caption));
        return vm;
    }

    /// <summary>
    /// Ctrl+A <b>alter</b>: overwrites the dated version this screen was opened over, via
    /// <see cref="PriceListService.AlterList"/>. Any domain refusal (bad slab set, or another version already on
    /// that date) is surfaced to <see cref="Message"/> without crashing the UI, exactly as <see cref="Save"/> does.
    /// </summary>
    public bool Alter()
    {
        Message = null;
        if (_editingId == Guid.Empty)
        {
            Message = "This screen is not altering an existing price list.";
            return false;
        }
        if (!TryBuildEntry(out var applicableFrom, out var slabs)) return false;

        PriceList altered;
        try
        {
            altered = new PriceListService(_company).AlterList(_editingId, applicableFrom, slabs);
            _storage.Save(_company);
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
            return false;
        }

        RefreshHistory();

        // 🔴 T2-100: THE MESSAGE NAMES THE RECORD THAT WAS ACTUALLY ALTERED, not whatever the pickers show now.
        // It used to read SelectedItem/SelectedLevel. Those are live TwoWay pickers on this screen, while
        // AlterList deliberately keeps the STORED (level, item) of the version being corrected — so an operator
        // who nudged the Item picker mid-alteration overwrote Widget-A and was told "Price list for 'Widget B'
        // … altered", looking at Widget-B's history, which does not contain the change. A message naming the
        // wrong record is how an operator believes a correction landed when it did not. The names are resolved
        // from the ids on the returned row, which are the ids the engine wrote.
        var alteredLevel = _company.FindPriceLevel(altered.PriceLevelId)?.Name ?? altered.PriceLevelId.ToString();
        var alteredItem = _company.FindStockItem(altered.StockItemId)?.Name ?? altered.StockItemId.ToString();
        Message = $"Price list for '{alteredItem}' under '{alteredLevel}' " +
                  $"altered (applicable from {altered.ApplicableFrom:dd-MMM-yyyy}).";
        _onChanged();
        return true;
    }

    /// <summary>
    /// Shared parse for <see cref="Save"/> and <see cref="Alter"/>: the level + item pickers, the day-first
    /// Applicable-From date and the non-blank slab rows. Sets <see cref="Message"/> and returns false on the first
    /// problem. Extracted at census 3.11 so the two verbs cannot drift apart in what they accept.
    /// </summary>
    private bool TryBuildEntry(out DateOnly applicableFrom, out List<PriceListSlab> slabs)
    {
        applicableFrom = default;
        slabs = new List<PriceListSlab>();

        if (SelectedLevel is null)
        {
            Message = "Pick a price level.";
            return false;
        }
        if (SelectedItem is null)
        {
            Message = "Pick an inventory item.";
            return false;
        }
        // WI-5: shared DAY-FIRST parse (was a bare InvariantCulture parse — the MM/dd misread).
        if (!ApexDate.TryParse(ApplicableFromText, out applicableFrom))
        {
            Message = $"Applicable-From: {ApexDate.ErrorFor(ApplicableFromText)}";
            return false;
        }

        foreach (var row in Slabs.Where(r => !r.IsBlank))
        {
            if (!TryParseDecimal(row.FromText, out var from))
            {
                Message = "Each slab needs a numeric From quantity.";
                return false;
            }
            decimal? to = null;
            if (!string.IsNullOrWhiteSpace(row.ToText))
            {
                if (!TryParseDecimal(row.ToText, out var toVal))
                {
                    Message = "A slab To quantity must be numeric (or blank for the open-ended top slab).";
                    return false;
                }
                to = toVal;
            }
            if (!TryParseDecimal(row.RateText, out var rate))
            {
                Message = "Each slab needs a numeric rate.";
                return false;
            }
            var discount = 0m;
            if (!string.IsNullOrWhiteSpace(row.DiscountText) && !TryParseDecimal(row.DiscountText, out discount))
            {
                Message = "A slab discount % must be numeric (or blank for none).";
                return false;
            }

            slabs.Add(new PriceListSlab(from, to, new Money(rate), discount));
        }

        if (slabs.Count == 0)
        {
            Message = "Enter at least one slab (From / Rate).";
            return false;
        }

        return true;
    }

    /// <summary>Rebuilds the append-only history list for the chosen (level, item), newest first (RQ-27).</summary>
    private void RefreshHistory()
    {
        // By ID, not by index — see PayrollMasterHighlight.RestoreTo. Changing the level or the item rebuilds
        // this list with a DIFFERENT set of ids, so the restore finds nothing and the highlight correctly
        // clears: Alt+D can never act on a version the operator has navigated away from.
        var previouslyHighlighted = Highlight.IdBeforeRebuild();

        History.Clear();
        if (SelectedLevel is null || SelectedItem is null) { Highlight.RestoreTo(null); return; }

        foreach (var pl in _company.PriceListsFor(SelectedLevel.Id, SelectedItem.Id)
                     .OrderByDescending(pl => pl.ApplicableFrom))
        {
            var slabs = string.Join("   ", pl.Slabs.Select(FormatSlab));
            var from = ApexDate.Format(pl.ApplicableFrom);
            History.Add(new PriceListVersionRow
            {
                MasterId = pl.Id,
                MasterName = $"{SelectedLevel.Name} / {SelectedItem.Name} applicable from {from}",
                ApplicableFrom = from,
                Slabs = slabs,
            });
        }

        Highlight.RestoreTo(previouslyHighlighted);
    }

    private static string FormatSlab(PriceListSlab s)
    {
        var band = s.ToQty is { } to
            ? $"{IndianFormat.Quantity(s.FromQty)}–{IndianFormat.Quantity(to)}"
            : $"{IndianFormat.Quantity(s.FromQty)}+";
        var disc = s.DiscountPercent > 0m
            ? $" (−{s.DiscountPercent.ToString("0.###", CultureInfo.InvariantCulture)}%)"
            : string.Empty;
        return $"{band} @ {IndianFormat.Amount(s.Rate)}{disc}";
    }

    private static bool TryParseDecimal(string? text, out decimal value)
        => decimal.TryParse(
            (text ?? string.Empty).Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out value);
}
