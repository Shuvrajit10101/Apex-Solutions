namespace Apex.Ledger.Domain;

/// <summary>
/// A currency master (catalog §2/§20 Multi-currency; plan.md §10 C-1). The company's <b>base currency</b>
/// (₹/INR) is itself exposed as a <see cref="Currency"/> with <see cref="IsBaseCurrency"/> set, seeded on
/// company create from the existing <see cref="Company"/> base-currency fields; additional currencies
/// (USD, EUR, …) are created as needed. A currency carries its display <see cref="Symbol"/>, its
/// <see cref="FormalName"/> (ISO-style code, e.g. "USD"), and how many <see cref="DecimalPlaces"/> its
/// minor unit uses.
/// </summary>
/// <remarks>
/// This is a framework- and DB-agnostic master with a stable surrogate id; a rename (symbol/formal name)
/// does not change identity. Exchange rates are a separate master (<see cref="ExchangeRate"/>) so a
/// currency can be dated at many rates over time. Exactly one currency per company is the base.
/// </remarks>
public sealed class Currency
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>Display symbol (e.g. "₹", "$", "€"); required.</summary>
    public string Symbol { get; set; }

    /// <summary>Formal / ISO name (e.g. "INR", "USD", "EUR"); required.</summary>
    public string FormalName { get; set; }

    /// <summary>Minor-unit decimal places (2 for most currencies); ≥ 0.</summary>
    public int DecimalPlaces { get; set; }

    /// <summary>
    /// True for the company's single base currency (₹/INR). A base-currency ledger/line needs no forex
    /// data — the base <see cref="Money"/> amount is already the exact paisa value all reports use.
    /// </summary>
    public bool IsBaseCurrency { get; }

    public Currency(Guid id, string symbol, string formalName, int decimalPlaces = 2, bool isBaseCurrency = false)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Currency symbol is required.", nameof(symbol));
        if (string.IsNullOrWhiteSpace(formalName))
            throw new ArgumentException("Currency formal name is required.", nameof(formalName));
        if (decimalPlaces < 0)
            throw new ArgumentException("Decimal places must be ≥ 0.", nameof(decimalPlaces));

        Id = id;
        Symbol = symbol.Trim();
        FormalName = formalName.Trim();
        DecimalPlaces = decimalPlaces;
        IsBaseCurrency = isBaseCurrency;
    }
}
