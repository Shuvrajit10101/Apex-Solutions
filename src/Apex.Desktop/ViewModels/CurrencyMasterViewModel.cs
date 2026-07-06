using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A currency row for the existing-currencies list on the Currency master screen.</summary>
public sealed class CurrencyListRow
{
    public string Symbol { get; init; } = string.Empty;
    public string FormalName { get; init; } = string.Empty;
    public string Decimals { get; init; } = string.Empty;

    /// <summary>"Base" for the company base currency, else blank.</summary>
    public string Kind { get; init; } = string.Empty;
}

/// <summary>A rate-of-exchange row for the existing-rates list on the Currency master screen.</summary>
public sealed class ExchangeRateListRow
{
    public string Currency { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public string Standard { get; init; } = string.Empty;
    public string Selling { get; init; } = string.Empty;
    public string Buying { get; init; } = string.Empty;
}

/// <summary>
/// The Currency-creation master ("Masters → Create → Currency", catalog §2/§20 Multi-currency; plan.md
/// §10 C-1). Two stacked forms on one page column:
/// <list type="bullet">
/// <item><b>Currency</b> — a display <b>Symbol</b> ($, €, …), a <b>Formal Name</b> (ISO code, e.g. USD),
///   and the minor-unit <b>Decimal Places</b>; creates a foreign <see cref="Currency"/> on the company.</item>
/// <item><b>Rate of Exchange</b> — a foreign currency + an as-of <b>Date</b> + a <b>Standard</b> rate
///   (base per 1 foreign unit) with optional <b>Selling</b>/<b>Buying</b> rates; creates a dated
///   <see cref="ExchangeRate"/> quote.</item>
/// </list>
/// Both persist the whole company aggregate to its <c>.db</c> via <see cref="CompanyStorage.Save"/> and
/// refresh their lists. MVVM boundary: references the domain + persistence but no Avalonia/UI types, so it
/// is headlessly unit-testable. Mirrors <see cref="LedgerMasterViewModel"/> / <see cref="ScenarioMasterViewModel"/>.
/// </summary>
public sealed partial class CurrencyMasterViewModel : ViewModelBase, IMasterListExportSource
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <inheritdoc/>
    /// <remarks>Snapshots the <see cref="Currencies"/> master list (the screen's primary grid); the dated
    /// Rates grid is a secondary sub-list, not exported here.</remarks>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Currencies",
        new[]
        {
            MasterListColumn.Text("Symbol"), MasterListColumn.Text("Formal Name"),
            MasterListColumn.Text("Decimals"), MasterListColumn.Text("Kind"),
        },
        Currencies.Select(r => (IReadOnlyList<string>)new[] { r.Symbol, r.FormalName, r.Decimals, r.Kind }).ToList());

    /// <summary>The existing currencies, refreshed after each create (base ₹/INR included).</summary>
    public ObservableCollection<CurrencyListRow> Currencies { get; } = new();

    /// <summary>The existing dated rate-of-exchange quotes, refreshed after each create, newest first.</summary>
    public ObservableCollection<ExchangeRateListRow> Rates { get; } = new();

    /// <summary>The foreign currencies a rate quote can be attached to (the base currency needs no rate).</summary>
    public ObservableCollection<Currency> ForeignCurrencies { get; } = new();

    // ---- Currency form ----
    [ObservableProperty] private string _symbol = string.Empty;
    [ObservableProperty] private string _formalName = string.Empty;
    [ObservableProperty] private string _decimalPlacesText = "2";
    [ObservableProperty] private string? _currencyMessage;

    // ---- Rate-of-exchange form ----
    [ObservableProperty] private Currency? _rateCurrency;
    [ObservableProperty] private string _rateDateText;
    [ObservableProperty] private string _standardRateText = string.Empty;
    [ObservableProperty] private string _sellingRateText = string.Empty;
    [ObservableProperty] private string _buyingRateText = string.Empty;
    [ObservableProperty] private string? _rateMessage;

    public CurrencyMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        // A sensible default rate date: the financial-year start.
        _rateDateText = company.FinancialYearStart
            .ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);

        RefreshCurrencies();
        RefreshRates();
    }

    /// <summary>True once at least one foreign currency exists — the rate form needs one to attach a rate to.</summary>
    public bool HasForeignCurrencies => ForeignCurrencies.Count > 0;

    /// <summary>
    /// Ctrl+A on the Currency form: validates the symbol + formal name are non-empty, the formal name is
    /// unique among currencies, and the decimals parse to ≥ 0; then adds a foreign <see cref="Currency"/>
    /// and persists. Refreshes the lists and clears the form for the next entry.
    /// </summary>
    public bool CreateCurrency()
    {
        CurrencyMessage = null;
        var symbol = (Symbol ?? string.Empty).Trim();
        var formal = (FormalName ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(symbol))
        {
            CurrencyMessage = "A currency symbol is required (e.g. $).";
            return false;
        }
        if (string.IsNullOrWhiteSpace(formal))
        {
            CurrencyMessage = "A formal name is required (e.g. USD).";
            return false;
        }
        if (_company.FindCurrencyByName(formal) is not null || _company.FindCurrencyByName(symbol) is not null)
        {
            CurrencyMessage = $"A currency '{formal}' ({symbol}) already exists.";
            return false;
        }
        if (!int.TryParse((DecimalPlacesText ?? string.Empty).Trim(), out var decimals) || decimals < 0)
        {
            CurrencyMessage = "Decimal places must be a whole number ≥ 0.";
            return false;
        }

        var currency = new Currency(Guid.NewGuid(), symbol, formal, decimalPlaces: decimals);
        _company.AddCurrency(currency);
        _storage.Save(_company);

        RefreshCurrencies();
        CurrencyMessage = $"Currency '{formal}' ({symbol}) created.";
        Symbol = string.Empty;
        FormalName = string.Empty;
        DecimalPlacesText = "2";
        // Point the rate form at the just-created currency for the natural "create currency then its rate" flow.
        RateCurrency = ForeignCurrencies.FirstOrDefault(c => c.Id == currency.Id) ?? RateCurrency;
        _onChanged();
        return true;
    }

    /// <summary>
    /// Ctrl+A on the Rate form: validates a currency is chosen, the date parses, and the standard rate is a
    /// number &gt; 0 (with optional selling/buying &gt; 0); then adds a dated <see cref="ExchangeRate"/> and
    /// persists. Refreshes the rate list and clears the rate fields for the next entry.
    /// </summary>
    public bool CreateRate()
    {
        RateMessage = null;

        if (RateCurrency is null)
        {
            RateMessage = "Pick a currency to set a rate for (create a foreign currency first).";
            return false;
        }
        if (!DateOnly.TryParseExact((RateDateText ?? string.Empty).Trim(), "dd-MMM-yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            RateMessage = "Date must be dd-MMM-yyyy (e.g. 01-Apr-2024).";
            return false;
        }
        if (!TryParseRate(StandardRateText, out var standard) || standard <= 0m)
        {
            RateMessage = "Standard rate must be a number > 0 (base ₹ per 1 foreign unit).";
            return false;
        }

        decimal? selling = null, buying = null;
        if (!string.IsNullOrWhiteSpace(SellingRateText))
        {
            if (!TryParseRate(SellingRateText, out var s) || s <= 0m)
            {
                RateMessage = "Selling rate must be a number > 0, or blank.";
                return false;
            }
            selling = s;
        }
        if (!string.IsNullOrWhiteSpace(BuyingRateText))
        {
            if (!TryParseRate(BuyingRateText, out var b) || b <= 0m)
            {
                RateMessage = "Buying rate must be a number > 0, or blank.";
                return false;
            }
            buying = b;
        }

        var rate = new ExchangeRate(Guid.NewGuid(), RateCurrency.Id, date, standard, selling, buying);
        _company.AddExchangeRate(rate);
        _storage.Save(_company);

        RefreshRates();
        RateMessage = $"Rate for {RateCurrency.FormalName} on {date:dd-MMM-yyyy}: ₹{standard:0.####} / 1.";
        StandardRateText = string.Empty;
        SellingRateText = string.Empty;
        BuyingRateText = string.Empty;
        _onChanged();
        return true;
    }

    private static bool TryParseRate(string? text, out decimal value)
        => decimal.TryParse((text ?? string.Empty).Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture, out value);

    private void RefreshCurrencies()
    {
        Currencies.Clear();
        foreach (var c in _company.Currencies
                     .OrderByDescending(c => c.IsBaseCurrency)
                     .ThenBy(c => c.FormalName, StringComparer.OrdinalIgnoreCase))
        {
            Currencies.Add(new CurrencyListRow
            {
                Symbol = c.Symbol,
                FormalName = c.FormalName,
                Decimals = c.DecimalPlaces.ToString(CultureInfo.InvariantCulture),
                Kind = c.IsBaseCurrency ? "Base" : "Foreign",
            });
        }

        // Refresh the rate-form currency picker (foreign currencies only), keeping the selection if possible.
        var previousId = RateCurrency?.Id;
        ForeignCurrencies.Clear();
        foreach (var c in _company.Currencies
                     .Where(c => !c.IsBaseCurrency)
                     .OrderBy(c => c.FormalName, StringComparer.OrdinalIgnoreCase))
            ForeignCurrencies.Add(c);

        RateCurrency = ForeignCurrencies.FirstOrDefault(c => c.Id == previousId)
                       ?? ForeignCurrencies.FirstOrDefault();
        OnPropertyChanged(nameof(HasForeignCurrencies));
    }

    private void RefreshRates()
    {
        Rates.Clear();
        foreach (var r in _company.ExchangeRates
                     .OrderByDescending(r => r.Date)
                     .ThenBy(r => CurrencyName(r.CurrencyId), StringComparer.OrdinalIgnoreCase))
        {
            Rates.Add(new ExchangeRateListRow
            {
                Currency = CurrencyName(r.CurrencyId),
                Date = r.Date.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
                Standard = r.StandardRate.ToString("#,##0.####", CultureInfo.InvariantCulture),
                Selling = r.SellingRate is { } s ? s.ToString("#,##0.####", CultureInfo.InvariantCulture) : "—",
                Buying = r.BuyingRate is { } b ? b.ToString("#,##0.####", CultureInfo.InvariantCulture) : "—",
            });
        }
    }

    private string CurrencyName(Guid currencyId) =>
        _company.FindCurrency(currencyId) is { } c ? $"{c.FormalName} ({c.Symbol})" : "?";
}
