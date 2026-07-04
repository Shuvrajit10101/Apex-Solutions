namespace Apex.Ledger.Domain;

/// <summary>
/// The forex detail hung off an <see cref="EntryLine"/> posted in a foreign currency (catalog §2/§20
/// Multi-currency; plan.md §10 C-1). It records the <see cref="CurrencyId"/> the line was entered in, the
/// <see cref="ForexAmount"/> (the magnitude in that foreign currency) and the <see cref="Rate"/> of
/// exchange used (base units per 1 foreign unit). The line's base <see cref="EntryLine.Amount"/> stays the
/// authoritative paisa-exact value — it equals <see cref="ForexAmount"/> × <see cref="Rate"/> (enforced at
/// posting) — so the ledger engine and every existing report treat a foreign line exactly like any base
/// line. A base-currency line carries no <see cref="ForexInfo"/> at all.
/// </summary>
/// <remarks>
/// Immutable value object with no identity. <see cref="ForexAmount"/> is a magnitude (&gt; 0), matching the
/// line amount's magnitude convention; <see cref="Rate"/> is an exact decimal (NFR-3), persisted as a
/// scaled integer (rate × 1,000,000), never as binary float.
/// </remarks>
public sealed class ForexInfo
{
    /// <summary>The foreign <see cref="Currency"/> the line was entered in.</summary>
    public Guid CurrencyId { get; }

    /// <summary>The amount in the foreign currency (a magnitude, &gt; 0).</summary>
    public Money ForexAmount { get; }

    /// <summary>Rate of exchange used: base units per 1 foreign unit. &gt; 0.</summary>
    public decimal Rate { get; }

    public ForexInfo(Guid currencyId, Money forexAmount, decimal rate)
    {
        if (forexAmount.Amount <= 0m)
            throw new ArgumentException("Forex amount must be > 0.", nameof(forexAmount));
        if (rate <= 0m)
            throw new ArgumentException("Exchange rate must be > 0.", nameof(rate));

        CurrencyId = currencyId;
        ForexAmount = forexAmount;
        Rate = rate;
    }

    /// <summary>
    /// The base-currency value implied by this forex line: <see cref="ForexAmount"/> × <see cref="Rate"/>,
    /// <b>rounded to the paisa</b> (normal/away-from-zero). The raw product of a non-round rate can carry a
    /// sub-paisa tail (e.g. US$100 × 83.3333 = ₹8 333.33 but US$100 × 83.33335 = ₹8 333.335), which the paisa
    /// store cannot persist; snapping to the paisa here makes this the authoritative, paisa-exact base value
    /// that the line's base <see cref="EntryLine.Amount"/> must match (enforced at posting).
    /// </summary>
    public Money BaseValue => Money.ForexBase(ForexAmount, Rate);
}
