namespace Apex.Ledger.Domain;

/// <summary>
/// A <b>per-month override</b> on a <see cref="PtSlabBand"/> (Phase 8 slice 6) — some states charge a higher PT in a
/// single balancing month (Maharashtra/Karnataka charge ₹300 in <b>February</b> so the top band's twelve months
/// total exactly ₹2,500) — modelled as the generic <c>{month, amount}</c> pair. <see cref="Month"/> is 1–12; the
/// <see cref="Amount"/> replaces the band's ordinary <see cref="PtSlabBand.MonthlyAmount"/> in that month. Pure data,
/// framework-/DB-/clock-free.
/// </summary>
public sealed class PtMonthOverride
{
    /// <summary>The calendar month (1 = January … 12 = December) the override applies to.</summary>
    public int Month { get; }

    /// <summary>The PT amount charged in <see cref="Month"/> (replaces the band's <see cref="PtSlabBand.MonthlyAmount"/>).</summary>
    public Money Amount { get; }

    public PtMonthOverride(int month, Money amount)
    {
        if (month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), "A PT month override must be a calendar month 1–12.");
        if (amount.Amount < 0m)
            throw new ArgumentException("A PT month override amount cannot be negative.", nameof(amount));
        Month = month;
        Amount = amount;
    }
}

/// <summary>
/// One <b>band</b> of a <see cref="PtSlab"/> table (Phase 8 slice 6) — a monthly PT-wage range mapped to a
/// <b>flat rupee amount</b> (PT is flat-amount-by-band, <b>not</b> a percentage). <see cref="FromWage"/> is the
/// band's inclusive lower bound; <see cref="ToWage"/> is its inclusive upper bound (<c>null</c> = open-ended top,
/// i.e. ∞). A band may carry <see cref="MonthOverrides"/> (typically a single February over-charge on the top band).
/// Comparison is against the whole-rupee PT-wages. Pure data.
/// </summary>
public sealed class PtSlabBand
{
    private readonly List<PtMonthOverride> _monthOverrides;

    /// <summary>The inclusive lower bound of the monthly PT-wage band (₹0 for the first band).</summary>
    public Money FromWage { get; }

    /// <summary>The inclusive upper bound of the band; <c>null</c> = open-ended top band (∞).</summary>
    public Money? ToWage { get; }

    /// <summary>The flat PT amount charged when the PT-wages fall in this band (before any month override).</summary>
    public Money MonthlyAmount { get; }

    /// <summary>Per-month overrides (order-preserved), e.g. a single February over-charge; empty for a plain band.</summary>
    public IReadOnlyList<PtMonthOverride> MonthOverrides => _monthOverrides;

    public PtSlabBand(Money fromWage, Money? toWage, Money monthlyAmount, IEnumerable<PtMonthOverride>? monthOverrides = null)
    {
        if (fromWage.Amount < 0m)
            throw new ArgumentException("A PT band lower bound cannot be negative.", nameof(fromWage));
        if (toWage is { } t && t.Amount < fromWage.Amount)
            throw new ArgumentException("A PT band upper bound cannot be below its lower bound.", nameof(toWage));
        if (monthlyAmount.Amount < 0m)
            throw new ArgumentException("A PT band amount cannot be negative.", nameof(monthlyAmount));
        FromWage = fromWage;
        ToWage = toWage;
        MonthlyAmount = monthlyAmount;
        _monthOverrides = new List<PtMonthOverride>(monthOverrides ?? Array.Empty<PtMonthOverride>());
    }

    /// <summary>Whether <paramref name="wholeRupeeWages"/> falls within this band (<c>from ≤ w ≤ to</c>, the top
    /// band open-ended). Compare against the whole-rupee PT-wages (the brief compares PT-wages in whole rupees).</summary>
    public bool Contains(decimal wholeRupeeWages) =>
        wholeRupeeWages >= FromWage.Amount && (ToWage is not { } t || wholeRupeeWages <= t.Amount);

    /// <summary>The PT amount charged in calendar <paramref name="month"/> (1–12): the matching
    /// <see cref="MonthOverrides"/> amount when one exists for that month, else the ordinary
    /// <see cref="MonthlyAmount"/>.</summary>
    public Money AmountForMonth(int month)
    {
        foreach (var o in _monthOverrides)
            if (o.Month == month) return o.Amount;
        return MonthlyAmount;
    }
}

/// <summary>
/// A <b>state PT slab table</b> (Phase 8 slice 6; catalog §14) — the ordered <see cref="Bands"/> for one
/// <see cref="StateCode"/> and (only for Maharashtra) one <see cref="GenderScope"/>. PT is a state subject, so the
/// table is fully data-driven and editable per company; the seeded set (Maharashtra men/women, Karnataka, West
/// Bengal — see <c>ProfessionalTax.SeedSlabTables</c>) is a starting point, not law. The dedicated
/// <c>ProfessionalTax</c> engine selects the single band containing the PT-wages and reads that band's amount for
/// the month. An empty table (or none for the state) means PT = ₹0. Pure data, framework-/DB-/clock-free.
/// </summary>
public sealed class PtSlab
{
    private readonly List<PtSlabBand> _bands;

    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The 2-digit GST state code (e.g. "27" Maharashtra, "29" Karnataka, "19" West Bengal) this table
    /// belongs to (<see cref="IndianState.Code"/>).</summary>
    public string StateCode { get; }

    /// <summary>The gender scope — <see cref="PtGenderScope.Any"/> for a gender-agnostic state, or Male/Female for
    /// Maharashtra's two tables.</summary>
    public PtGenderScope GenderScope { get; }

    /// <summary>The bands, low-to-high (order-preserved as seeded/edited).</summary>
    public IReadOnlyList<PtSlabBand> Bands => _bands;

    public PtSlab(Guid id, string stateCode, PtGenderScope genderScope, IEnumerable<PtSlabBand> bands)
    {
        if (string.IsNullOrWhiteSpace(stateCode))
            throw new ArgumentException("A PT slab table needs a state code.", nameof(stateCode));
        Id = id;
        StateCode = stateCode.Trim();
        GenderScope = genderScope;
        _bands = new List<PtSlabBand>(bands ?? throw new ArgumentNullException(nameof(bands)));
    }

    /// <summary>The single band containing <paramref name="wholeRupeeWages"/> (the first match in band order), or
    /// <c>null</c> when no band contains it (⇒ PT ₹0).</summary>
    public PtSlabBand? SelectBand(decimal wholeRupeeWages)
    {
        foreach (var band in _bands)
            if (band.Contains(wholeRupeeWages)) return band;
        return null;
    }
}
