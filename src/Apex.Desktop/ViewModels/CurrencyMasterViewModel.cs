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

/// <summary>A currency row for the existing-currencies list on the Currency master screen.</summary>
public sealed partial class CurrencyListRow : ObservableObject, IMasterListRow
{
    public string Symbol { get; init; } = string.Empty;
    public string FormalName { get; init; } = string.Empty;
    public string Decimals { get; init; } = string.Empty;

    /// <summary>"Base" for the company base currency, else blank.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <inheritdoc/>
    public Guid MasterId { get; init; }

    /// <summary><inheritdoc/>
    /// <para>"USD ($)" — the FORMAL NAME leads because that is the unique one. Two currencies can share a
    /// symbol (USD, CAD, AUD, SGD and HKD are all "$"), so a confirmation built on the symbol alone could name
    /// a different row than the one highlighted.</para></summary>
    public string MasterName => $"{FormalName} ({Symbol})";

    /// <inheritdoc/>
    [ObservableProperty] private bool _isHighlighted;
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
public sealed partial class CurrencyMasterViewModel : ViewModelBase, IMasterListExportSource, IMasterListScreen
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    // ------------------------------------------------- W33 C3 (census 2.11): the shared master-list arm
    //
    // 🔴 THIS SCREEN HAS **TWO** LISTS AND THE SHARED ARM CAN CARRY ONLY ONE. The page stacks the CURRENCIES
    // grid over the RATES OF EXCHANGE grid. `IMasterListScreen` exposes exactly one highlighted row, so the
    // arrows and Alt+D are bound to the CURRENCIES list and the rates list is deliberately NOT reachable by
    // Alt+D. Row 2.11 therefore moves only half way and stays PARTIAL: `Company.RemoveExchangeRate` still has no
    // Desktop caller.
    //
    // The alternative — a "which grid has focus" concept threaded through the shared arm — is a real design
    // change to a contract six other masters depend on, and guessing at it here is how one master ends up gated
    // differently from its siblings. It is recorded as owed rather than half-built.

    /// <inheritdoc/>
    public string MasterKindLabel => "currency";

    /// <summary>The currency this screen was opened over for ALTERATION, or <see cref="Guid.Empty"/> when it is
    /// creating. Set only by <see cref="ForAlter"/>.</summary>
    private Guid _editingId = Guid.Empty;

    /// <inheritdoc/>
    /// <remarks>🔴 Was the constant <c>false</c> until W33 C3 wired <see cref="ForAlter"/>. Now derived, and the
    /// shell reads it twice: Alt+D is refused while it is true, and <c>ActivateSelected</c> uses it to decide
    /// whether Ctrl+A on the currency form means <see cref="CreateCurrency"/> or <see cref="AlterCurrency"/>.
    /// <b>The RATE form's Ctrl+A is unaffected</b> — it has its own button/handler and adds a dated quote either
    /// way, which is what the vendor page describes as available alongside an alteration.</remarks>
    public bool IsAltering => _editingId != Guid.Empty;

    /// <summary>The column caption — "Currency Alteration" while altering, else "Currency Creation".</summary>
    public string Caption => IsAltering ? "Currency Alteration" : "Currency Creation";

    /// <summary>
    /// Opens this screen over an EXISTING currency, for an alteration of its symbol, formal name or decimal places
    /// (census 2.11, W33 C3). Returns <c>null</c> if the id does not resolve <b>or if the id is the BASE
    /// currency</b>.
    ///
    /// <para><b>FIDELITY (R7): VENDOR-ATTESTED.</b> TallyPrime reaches it at <i>Alt+G (Go To) &gt; Alter Master
    /// &gt; Currency &gt; select the currency you want to alter</i>, and the alterable details are the symbol,
    /// formal name, ISO code and decimal places
    /// [help.tallysolutions.com/create-alter-or-delete-currencies/, read 2026-09-25]. <b>We carry no separate ISO
    /// code field</b> — <see cref="FormalName"/> IS the ISO-style code in this product's model
    /// (<c>Currency.FormalName</c>: "Formal / ISO name (e.g. INR, USD, EUR)") — so three of the four attested
    /// fields are present and the fourth does not exist to alter. That is a model difference, recorded, not a
    /// silent omission.</para>
    ///
    /// <para>🔴 <b>THE BASE CURRENCY IS REFUSED HERE AND THAT IS A CORRECTNESS RULE, NOT TIMIDITY.</b> The base
    /// <c>Currency</c> row is a PROJECTION of <c>Company.BaseCurrencySymbol</c> / <c>BaseCurrencyName</c> /
    /// <c>DecimalPlaces</c> — <c>SeedCurrencies.BuildBaseCurrency</c> builds it from exactly those three fields and
    /// nothing re-syncs it afterwards. Renaming the row alone would leave the company profile saying ₹/INR while
    /// the currency master said something else, and the two are printed by different screens. The company profile
    /// (F3 &gt; Alter Company) is the one place that owns those fields and already edits them, which is also how
    /// the vendor separates it — a distinct "Change Base Currency" procedure rather than an Alter Master. The
    /// shell turns this <c>null</c> into a NOTICE naming that route, so the refusal is not a silent no-op.</para>
    ///
    /// <para><b>Altering DecimalPlaces cannot move a figure.</b> Measured, not assumed:
    /// <c>Currency.DecimalPlaces</c> has exactly one reader in the whole product —
    /// <see cref="RefreshCurrencies"/>, which prints it in the list column — so it formats and rounds nothing. A
    /// forex line's base <see cref="Apex.Ledger.Domain.Money"/> is already exact paisa and is untouched.</para>
    /// </summary>
    public static CurrencyMasterViewModel? ForAlter(
        Company company, CompanyStorage storage, Guid currencyId, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (company.FindCurrency(currencyId) is not { } currency) return null;
        if (currency.IsBaseCurrency) return null;

        var vm = new CurrencyMasterViewModel(company, storage, onChanged);
        vm._editingId = currencyId;
        vm.Symbol = currency.Symbol;
        vm.FormalName = currency.FormalName;
        vm.DecimalPlacesText = currency.DecimalPlaces.ToString(CultureInfo.InvariantCulture);
        vm.OnPropertyChanged(nameof(IsAltering));
        vm.OnPropertyChanged(nameof(Caption));
        return vm;
    }

    /// <summary>
    /// Ctrl+A <b>alter</b>: writes the symbol, formal name and decimal places back onto the currency this screen
    /// was opened over. Same three validations <see cref="CreateCurrency"/> applies, with the currency excluded
    /// from its own uniqueness check so re-accepting an unchanged form is a no-op rather than a duplicate error.
    ///
    /// <para>🔴 <b>THE CHECK IS DONE BEFORE ANY FIELD IS WRITTEN</b>, and all three fields are then written
    /// together. Setting <see cref="Apex.Ledger.Domain.Currency.Symbol"/> first and discovering the decimals do
    /// not parse would leave the master half-altered in memory with the operator told it failed.</para>
    /// </summary>
    public bool AlterCurrency()
    {
        CurrencyMessage = null;
        if (_editingId == Guid.Empty)
        {
            CurrencyMessage = "This screen is not altering an existing currency.";
            return false;
        }
        if (_company.FindCurrency(_editingId) is not { } currency)
        {
            CurrencyMessage = "That currency no longer exists.";
            return false;
        }

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
        // 🔴 EXCLUDE SELF BY ID. FindCurrencyByName matches the formal name OR the symbol, case-insensitively, so
        // on an unchanged form it finds THIS currency — a bare null check would refuse the operator their own
        // values back and make "open, look, accept" an error. A DIFFERENT currency owning either string is still
        // refused, which is the rule Create enforces.
        if ((_company.FindCurrencyByName(formal) is { } byName && byName.Id != _editingId)
            || (_company.FindCurrencyByName(symbol) is { } bySymbol && bySymbol.Id != _editingId))
        {
            CurrencyMessage = $"A currency '{formal}' ({symbol}) already exists.";
            return false;
        }
        if (!int.TryParse((DecimalPlacesText ?? string.Empty).Trim(), out var decimals) || decimals < 0)
        {
            CurrencyMessage = "Decimal places must be a whole number ≥ 0.";
            return false;
        }

        currency.Symbol = symbol;
        currency.FormalName = formal;
        currency.DecimalPlaces = decimals;
        _storage.Save(_company);

        RefreshCurrencies();
        RefreshRates();              // the rate list prints the currency NAME — it would otherwise show the old one
        CurrencyMessage = $"Currency '{formal}' ({symbol}) altered.";
        _onChanged();
        return true;
    }

    /// <inheritdoc/>
    public IMasterListRow? HighlightedMasterRow => HighlightedRow;

    /// <inheritdoc/>
    public void ReloadExisting()
    {
        RefreshCurrencies();
        RefreshRates();
    }

    /// <inheritdoc/>
    /// <remarks>Engine-only — the shell saves and reloads after this returns. The refusal is
    /// <see cref="MasterDeletionRules.EnsureCurrencyDeletable"/>'s own message: the base currency is refused
    /// outright, a currency any posted entry line is entered in is refused with the count, and a currency a
    /// ledger or a rate quote still names is refused with the breakdown. A currency has no service of its own
    /// (create goes straight to <c>Company.AddCurrency</c>), so the guard is asked here directly, which is the
    /// same order every service-backed sibling uses: guard first, remove second, never half-applied.</remarks>
    public void DeleteMaster(Guid id)
    {
        var currency = _company.FindCurrency(id)
            ?? throw new InvalidOperationException($"Currency {id} not found.");

        MasterDeletionRules.EnsureCurrencyDeletable(_company, currency);
        _company.RemoveCurrency(currency);
    }

    private PayrollMasterHighlight<CurrencyListRow>? _highlight;

    private PayrollMasterHighlight<CurrencyListRow> Highlight =>
        _highlight ??= new PayrollMasterHighlight<CurrencyListRow>(
            Currencies, () => OnPropertyChanged(nameof(HighlightedRow)));

    /// <summary>The arrow-highlighted existing currency, or null.</summary>
    public CurrencyListRow? HighlightedRow => Highlight.Row;

    /// <inheritdoc/>
    public void MoveHighlight(int direction) => Highlight.Move(direction);

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
        _rateDateText = ApexDate.Format(company.FinancialYearStart);

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
        // WI-5: shared lenient DAY-FIRST parse (was strict dd-MMM-yyyy-only on this screen).
        if (!ApexDate.TryParse(RateDateText, out var date))
        {
            RateMessage = ApexDate.ErrorFor(RateDateText);
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
        // By ID, not by index — see PayrollMasterHighlight.RestoreTo.
        var previouslyHighlighted = Highlight.IdBeforeRebuild();

        Currencies.Clear();
        foreach (var c in _company.Currencies
                     .OrderByDescending(c => c.IsBaseCurrency)
                     .ThenBy(c => c.FormalName, StringComparer.OrdinalIgnoreCase))
        {
            Currencies.Add(new CurrencyListRow
            {
                MasterId = c.Id,
                Symbol = c.Symbol,
                FormalName = c.FormalName,
                Decimals = c.DecimalPlaces.ToString(CultureInfo.InvariantCulture),
                Kind = c.IsBaseCurrency ? "Base" : "Foreign",
            });
        }

        Highlight.RestoreTo(previouslyHighlighted);

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
                Date = ApexDate.Format(r.Date),
                Standard = r.StandardRate.ToString("#,##0.####", CultureInfo.InvariantCulture),
                Selling = r.SellingRate is { } s ? s.ToString("#,##0.####", CultureInfo.InvariantCulture) : "—",
                Buying = r.BuyingRate is { } b ? b.ToString("#,##0.####", CultureInfo.InvariantCulture) : "—",
            });
        }
    }

    private string CurrencyName(Guid currencyId) =>
        _company.FindCurrency(currencyId) is { } c ? $"{c.FormalName} ({c.Symbol})" : "?";
}
