namespace Apex.Ledger.Domain;

/// <summary>
/// Which of a dated <see cref="ExchangeRate"/>'s three rates to apply (catalog §2 "Rates of Exchange").
/// A quote records a <b>Standard</b> rate (the reference/mid rate), a <b>Selling</b> rate (used when you
/// receive foreign currency, i.e. sales) and a <b>Buying</b> rate (used when you pay foreign currency,
/// i.e. purchases). The chosen rate converts 1 foreign unit into base units.
/// </summary>
public enum ExchangeRateKind
{
    /// <summary>The standard / reference (mid) rate.</summary>
    Standard = 0,

    /// <summary>The selling rate (receiving forex — sales/receipts).</summary>
    Selling = 1,

    /// <summary>The buying rate (paying forex — purchases/payments).</summary>
    Buying = 2,
}

/// <summary>
/// A dated set of exchange rates for a foreign currency (catalog §2 "Rates of Exchange"; plan.md §10 C-1).
/// Each rate is expressed as <b>base units per 1 foreign unit</b> (e.g. StandardRate 83.25 ⇒ ₹83.25 per
/// US$1). A currency accumulates many <see cref="ExchangeRate"/> rows over time; the rate in force for a
/// transaction is the latest-dated row on or before the voucher date.
/// </summary>
/// <remarks>
/// Rates are exact decimals (NFR-3) and are persisted as scaled integers (rate × 1,000,000 = "micros"),
/// never as binary float, so they round-trip losslessly. A zero rate is invalid; a rate that is not
/// supplied (Selling/Buying) falls back to the Standard rate via <see cref="RateOf"/>.
/// </remarks>
public sealed class ExchangeRate
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The foreign <see cref="Currency"/> this rate is for.</summary>
    public Guid CurrencyId { get; }

    /// <summary>The date this rate takes effect (the "as-of" date of the quote).</summary>
    public DateOnly Date { get; }

    /// <summary>Standard (reference/mid) rate: base units per 1 foreign unit. &gt; 0.</summary>
    public decimal StandardRate { get; }

    /// <summary>Selling rate (receiving forex). <c>null</c> ⇒ use <see cref="StandardRate"/>.</summary>
    public decimal? SellingRate { get; }

    /// <summary>Buying rate (paying forex). <c>null</c> ⇒ use <see cref="StandardRate"/>.</summary>
    public decimal? BuyingRate { get; }

    public ExchangeRate(
        Guid id,
        Guid currencyId,
        DateOnly date,
        decimal standardRate,
        decimal? sellingRate = null,
        decimal? buyingRate = null)
    {
        if (standardRate <= 0m)
            throw new ArgumentException("Standard rate must be > 0.", nameof(standardRate));
        if (sellingRate is <= 0m)
            throw new ArgumentException("Selling rate must be > 0 when set.", nameof(sellingRate));
        if (buyingRate is <= 0m)
            throw new ArgumentException("Buying rate must be > 0 when set.", nameof(buyingRate));

        Id = id;
        CurrencyId = currencyId;
        Date = date;
        StandardRate = standardRate;
        SellingRate = sellingRate;
        BuyingRate = buyingRate;
    }

    /// <summary>
    /// The rate for a given <see cref="ExchangeRateKind"/>: the Selling/Buying rate when supplied, else the
    /// Standard rate (a missing directional rate falls back to the Standard rate).
    /// </summary>
    public decimal RateOf(ExchangeRateKind kind) => kind switch
    {
        ExchangeRateKind.Selling => SellingRate ?? StandardRate,
        ExchangeRateKind.Buying => BuyingRate ?? StandardRate,
        _ => StandardRate,
    };
}
