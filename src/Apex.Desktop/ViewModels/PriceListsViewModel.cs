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
public sealed class PriceListVersionRow
{
    public string ApplicableFrom { get; init; } = string.Empty;
    public string Slabs { get; init; } = string.Empty;
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
public sealed partial class PriceListsViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

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
        _applicableFromText = applicable.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);

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
        if (!DateOnly.TryParse((ApplicableFromText ?? string.Empty).Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var applicableFrom))
        {
            Message = "Enter a valid Applicable-From date (e.g. 01-Apr-2026).";
            return false;
        }

        var slabs = new List<PriceListSlab>();
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

        try
        {
            var service = new PriceListService(_company);
            service.AddOrReviseList(SelectedLevel.Id, SelectedItem.Id, applicableFrom, slabs);
            _storage.Save(_company);
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
            return false;
        }

        RefreshHistory();
        Message = $"Price list for '{SelectedItem.Name}' under '{SelectedLevel.Name}' " +
                  $"saved (applicable from {applicableFrom:dd-MMM-yyyy}).";

        // Reset the slab grid for the next entry (keep the level/item/date so a quick revision is easy).
        Slabs.Clear();
        AddSlabRow();
        _onChanged();
        return true;
    }

    /// <summary>Rebuilds the append-only history list for the chosen (level, item), newest first (RQ-27).</summary>
    private void RefreshHistory()
    {
        History.Clear();
        if (SelectedLevel is null || SelectedItem is null) return;

        foreach (var pl in _company.PriceListsFor(SelectedLevel.Id, SelectedItem.Id)
                     .OrderByDescending(pl => pl.ApplicableFrom))
        {
            var slabs = string.Join("   ", pl.Slabs.Select(FormatSlab));
            History.Add(new PriceListVersionRow
            {
                ApplicableFrom = pl.ApplicableFrom.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
                Slabs = slabs,
            });
        }
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
