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

    // ───────────────────────────────────────────────────────────────────────────────────────────────────────
    // v67 (census row 2.11) — THE CURRENCY FORMATTING OPTIONS. The vendor page lists SIX; the sixth is
    // DecimalPlaces above and is not duplicated here. Captions verbatim from
    // help.tallysolutions.com/create-alter-or-delete-currencies/ ("Create, Alter, or Delete Currencies"),
    // opened BY CONTENT on 2026-10-16. 🔴 The row's cell recorded FOUR options and a 404'd page; both are
    // corrected — there are six and the slug resolves. Each default is this build's pre-v67 behaviour, so
    // nothing is back-filled (ER-13).
    // ⚠️ STORAGE AND ELECTION in v67: these record the per-currency choice. Wiring every amount formatter and
    // every amount-in-words call site to honour them is NOT done here, and is said plainly so no reader takes
    // a persisted option for a rendered one.
    // ───────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// v67 (census 2.11): <i>"Set the option <b>Show amounts in millions</b> to Yes or No , as needed."</i>
    /// <c>false</c> = the lakh/crore grouping this build has always used.
    /// </summary>
    public bool ShowAmountsInMillions { get; set; }

    /// <summary>
    /// v67 (census 2.11): <i>"<b>Suffix symbol to amount</b> : Yes , to ensure that the symbol appears after
    /// the amount."</i> <c>false</c> = the symbol prefixes the amount, as before.
    /// </summary>
    public bool SuffixSymbolToAmount { get; set; }

    /// <summary>
    /// v67 (census 2.11): <i>"<b>Add space between amount and symbol</b> : Yes ."</i> <c>false</c> = no space,
    /// as before.
    /// </summary>
    public bool SpaceBetweenAmountAndSymbol { get; set; }

    /// <summary>
    /// v67 (census 2.11): <i>"Enter the <b>Word representing amount after decimal</b> . For example, Cent."</i>
    /// <c>null</c> = this build's existing minor-unit word, so nothing is back-filled.
    /// </summary>
    public string? DecimalWord { get; set; }

    /// <summary>
    /// v67 (census 2.11): <i>"Enter the <b>No. of decimal places for amounts in words</b> ."</i>, under the
    /// page's own rule <i>"This number should be equal to or lesser than the number specified in Number of
    /// decimal places field."</i>
    ///
    /// <para>🔴 <b><c>null</c> IS NOT ZERO, AND THAT IS WHY THIS IS NULLABLE RATHER THAN DEFAULTED.</b>
    /// <c>null</c> means "fall back to <see cref="DecimalPlaces"/>", which is exactly what amount-in-words did
    /// before v67; <c>0</c> is the operator's deliberate choice to write no minor unit in words at all. A
    /// <c>DEFAULT 0</c> column would have silently converted every existing book to the second, which is a
    /// back-fill dressed as a default.</para>
    ///
    /// <para>⚠️ <b>THE VENDOR'S ≤ RULE IS NOT ENFORCED HERE.</b> It is a screen-level validation on a field
    /// this slice does not yet surface; enforcing it in the constructor would reject import and round-trip
    /// data the vendor itself accepts on an altered currency. Left to the master screen that builds it.</para>
    /// </summary>
    public int? DecimalPlacesForWords { get; set; }

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
