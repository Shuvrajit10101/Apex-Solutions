using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// One editable row in the "Cost Allocation" sub-panel of a voucher line (catalog §6). It captures the
/// three prompts for a single allocation: <b>Category</b> (which cost category), <b>Centre</b> (a centre
/// within that category) and <b>Amount</b> (a magnitude, typed as text). Several rows may hang off one
/// line; their amounts must <b>sum to the line amount</b> ("split across centres"). Parsing/validation is
/// deferred to the parent line — this class only holds the editable state, keeps the Centre picker scoped
/// to the chosen Category, and raises change notifications so the running split total updates as the user
/// types. No Avalonia types ⇒ headlessly unit-testable. Mirrors <see cref="BillAllocationRowViewModel"/>.
/// </summary>
public sealed partial class CostAllocationRowViewModel : ViewModelBase
{
    private readonly Action _onChanged;
    private readonly IReadOnlyList<CostCategory> _categories;
    private readonly IReadOnlyList<CostCentre> _allCentres;

    /// <summary>The cost categories the "Category" picker offers (company order).</summary>
    public IReadOnlyList<CostCategory> Categories => _categories;

    /// <summary>The centres available under the chosen <see cref="SelectedCategory"/> (category-scoped).</summary>
    public ObservableCollection<CostCentre> Centres { get; } = new();

    [ObservableProperty] private CostCategory? _selectedCategory;
    [ObservableProperty] private CostCentre? _selectedCentre;
    [ObservableProperty] private string _amountText = string.Empty;

    public CostAllocationRowViewModel(
        Action onChanged,
        IReadOnlyList<CostCategory> categories,
        IReadOnlyList<CostCentre> allCentres,
        CostCategory? defaultCategory = null)
    {
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _categories = categories ?? throw new ArgumentNullException(nameof(categories));
        _allCentres = allCentres ?? throw new ArgumentNullException(nameof(allCentres));
        _selectedCategory = defaultCategory ?? _categories.FirstOrDefault();
        RefreshCentres();
    }

    /// <summary>Rescopes the Centre picker whenever the Category changes; a stale centre is cleared.</summary>
    partial void OnSelectedCategoryChanged(CostCategory? value)
    {
        RefreshCentres();
        _onChanged();
    }

    partial void OnSelectedCentreChanged(CostCentre? value) => _onChanged();
    partial void OnAmountTextChanged(string value) => _onChanged();

    private void RefreshCentres()
    {
        Centres.Clear();
        if (SelectedCategory is not null)
            foreach (var c in _allCentres.Where(c => c.CategoryId == SelectedCategory.Id))
                Centres.Add(c);

        // Keep a chosen centre only if it still belongs to the (new) category, else clear it.
        if (SelectedCentre is not null && !Centres.Contains(SelectedCentre))
            SelectedCentre = null;
    }

    /// <summary>The parsed amount magnitude (0 when unparsable/blank).</summary>
    public decimal ParsedAmount => TryParseAmount(out var amt) ? amt : 0m;

    /// <summary>True once this row is touched at all (a centre or an amount) — a fully-blank row is ignored.</summary>
    public bool IsBlank => SelectedCentre is null && string.IsNullOrWhiteSpace(AmountText);

    /// <summary>True when this row is a complete allocation: a category, a centre, and a positive amount.</summary>
    public bool IsComplete =>
        SelectedCategory is not null && SelectedCentre is not null && ParsedAmount > 0m;

    /// <summary>Builds the domain <see cref="CostAllocation"/> for this row (caller only invokes when complete).</summary>
    public CostAllocation ToAllocation() =>
        new(SelectedCategory!.Id, SelectedCentre!.Id, new Money(ParsedAmount));

    private bool TryParseAmount(out decimal amount)
        => decimal.TryParse(
            (AmountText ?? string.Empty).Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out amount);
}
