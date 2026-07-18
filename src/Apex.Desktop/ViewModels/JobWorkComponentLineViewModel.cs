using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Apex.Ledger;
using Apex.Ledger.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using Apex.Desktop.Services;

namespace Apex.Desktop.ViewModels;

/// <summary>One track-direction option (Pending to Receive / Pending to Issue) for a Job Work Order
/// component line (Phase 6 slice 8; RQ-47). The <see cref="Track"/> — not the order's direction — is the
/// load-bearing IN/OUT expression (RQ-50).</summary>
public sealed class JobWorkTrackOption
{
    public JobWorkComponentTrack Track { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>
/// One editable tracked-component line in the Job Work Order-entry grid (Phase 6 slice 8; RQ-47): the picked
/// component <see cref="StockItem"/>, its <see cref="JobWorkComponentTrack"/> (Pending to Receive / Issue), the
/// component godown, a quantity + optional per-unit rate typed as text and an optional due date. Parsing and
/// validation are deferred to the parent <see cref="JobWorkOrderEntryViewModel"/>; this class only holds the
/// editable state and raises change notifications so the parent's Accept-enabled recomputes as the user types.
///
/// <para>MVVM boundary: references only the domain, no Avalonia/UI types, so it is headlessly unit-testable.</para>
/// </summary>
public sealed partial class JobWorkComponentLineViewModel : ViewModelBase
{
    private readonly Action _onChanged;

    /// <summary>The stock items the item picker chooses from (shared list, set by the parent).</summary>
    public IReadOnlyList<StockItem> StockItems { get; }

    /// <summary>The godowns the godown picker chooses from (shared list, set by the parent).</summary>
    public IReadOnlyList<Godown> Godowns { get; }

    /// <summary>The two track options (Pending to Receive / Pending to Issue).</summary>
    public ObservableCollection<JobWorkTrackOption> TrackOptions { get; }

    [ObservableProperty] private StockItem? _selectedItem;
    [ObservableProperty] private JobWorkTrackOption? _selectedTrack;
    [ObservableProperty] private Godown? _selectedGodown;
    [ObservableProperty] private string _quantityText = string.Empty;
    [ObservableProperty] private string _rateText = string.Empty;
    [ObservableProperty] private string _dueText = string.Empty;

    public JobWorkComponentLineViewModel(
        IReadOnlyList<StockItem> stockItems,
        IReadOnlyList<Godown> godowns,
        ObservableCollection<JobWorkTrackOption> trackOptions,
        JobWorkComponentTrack defaultTrack,
        Action onChanged)
    {
        StockItems = stockItems ?? throw new ArgumentNullException(nameof(stockItems));
        Godowns = godowns ?? throw new ArgumentNullException(nameof(godowns));
        TrackOptions = trackOptions ?? throw new ArgumentNullException(nameof(trackOptions));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        foreach (var o in TrackOptions)
            if (o.Track == defaultTrack) { _selectedTrack = o; break; }

        // Default the godown to the Main Location so the common single-godown case needs no picking.
        foreach (var g in godowns)
            if (g.IsMainLocation) { _selectedGodown = g; break; }
    }

    partial void OnSelectedItemChanged(StockItem? value) => _onChanged();
    partial void OnSelectedTrackChanged(JobWorkTrackOption? value) => _onChanged();
    partial void OnSelectedGodownChanged(Godown? value) => _onChanged();
    partial void OnQuantityTextChanged(string value) => _onChanged();
    partial void OnRateTextChanged(string value) => _onChanged();
    partial void OnDueTextChanged(string value) => _onChanged();

    /// <summary>The parsed quantity (0 when blank/unparsable).</summary>
    public decimal ParsedQuantity => TryParse(QuantityText, out var q) ? q : 0m;

    /// <summary>The parsed rate (null when blank; 0 or more otherwise).</summary>
    public decimal? ParsedRate =>
        string.IsNullOrWhiteSpace(RateText) ? null : (TryParse(RateText, out var r) ? r : null);

    /// <summary>True when a rate was typed (so the parent must validate it is paisa-exact + ≥ 0).</summary>
    public bool HasRate => !string.IsNullOrWhiteSpace(RateText);

    /// <summary>The parsed due date (WI-5 shared day-first parser; null when blank/unparsable).</summary>
    public DateOnly? ParsedDue =>
        ApexDate.TryParse(DueText, out var d) ? d : (DateOnly?)null;

    /// <summary>True when a due date was TYPED but cannot be read (WI-5) — the parent refuses rather than dropping it.</summary>
    public bool HasUnreadableDue =>
        !string.IsNullOrWhiteSpace(DueText) && ParsedDue is null;

    /// <summary>True once the row has been touched at all — a wholly blank trailing row is ignored.</summary>
    public bool IsBlank =>
        SelectedItem is null
        && string.IsNullOrWhiteSpace(QuantityText)
        && string.IsNullOrWhiteSpace(RateText)
        && string.IsNullOrWhiteSpace(DueText);

    /// <summary>
    /// True when the row is fully and validly specified: an item + a godown + a track picked, and a quantity
    /// that parses &gt; 0 within precision. A typed rate must be paisa-exact + ≥ 0. This is the parent's Accept
    /// gate for the line.
    /// </summary>
    public bool IsComplete
    {
        get
        {
            if (SelectedItem is null || SelectedGodown is null || SelectedTrack is null) return false;
            if (!TryParse(QuantityText, out var qty) || qty <= 0m) return false;
            if (!Quantities.IsWithinPrecision(qty)) return false;
            if (HasRate)
            {
                if (ParsedRate is not { } r || r < 0m) return false;
                if (!new Money(r).IsPaisaExact) return false;
            }
            return true;
        }
    }

    private static bool TryParse(string? text, out decimal value)
        => decimal.TryParse(
            (text ?? string.Empty).Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out value);
}
