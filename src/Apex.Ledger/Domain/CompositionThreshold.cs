namespace Apex.Ledger.Domain;

/// <summary>
/// The composition-scheme eligibility threshold + tax-on-turnover rate + turnover base, resolved from the home State/UT
/// and the <see cref="CompositionSubType"/> (Phase 9 slice 3; RQ-4; DP-9; §10 + Rule 7). Modelled as <b>static
/// reference data</b> (like <see cref="IndianState"/>) — these are fixed law, not user data — so nothing is persisted
/// or migrated (keeps ER-13 trivially clean). The threshold is <b>advisory</b> (an eligibility warning), never a hard
/// posting gate; a council rate change is a one-line edit here, never a schema change.
/// </summary>
/// <remarks>
/// R7 (A14 re-verify at build): the 8 special-category states, the ₹1.5 cr / ₹75 L / ₹50 L figures, and the 1/5/6 %
/// rates against the current CBIC §10 / Rule 7 notifications.
/// </remarks>
public static class CompositionThreshold
{
    /// <summary>The 8 special-category States/UTs at the reduced ₹75 L composition eligibility threshold (verifier-
    /// confirmed): Uttarakhand 05, Sikkim 11, Arunachal Pradesh 12, Nagaland 13, Manipur 14, Mizoram 15, Tripura 16,
    /// Meghalaya 17. (Assam 18, Himachal Pradesh 02 and J&amp;K 01 sit at the ₹1.5 cr general threshold.)</summary>
    private static readonly HashSet<string> SpecialCategory = new(StringComparer.Ordinal)
        { "05", "11", "12", "13", "14", "15", "16", "17" };

    /// <summary>The preceding-FY aggregate-turnover eligibility threshold (rupees) for a sub-type + home state: §10(2A)
    /// service providers ₹50 L (all states); a goods composition dealer in a special-category state ₹75 L; else the
    /// ₹1.5 cr general cap. Advisory (an eligibility warning), never a hard posting gate.</summary>
    public static Money Threshold(CompositionSubType subType, string? homeStateCode) => subType switch
    {
        CompositionSubType.ServiceProvider => new Money(5_000_000m),                          // §10(2A) ₹50 L (all states)
        _ when homeStateCode is { } s && SpecialCategory.Contains(s) => new Money(7_500_000m), // ₹75 L special-category
        _ => new Money(15_000_000m),                                                          // ₹1.5 cr general
    };

    /// <summary>The composition tax-on-turnover rate (integrated basis points) for a sub-type: Manufacturer/Trader 100
    /// (1%), Restaurant 500 (5%), ServiceProvider 600 (6%). Split half/half into CGST+SGST by the compute-total-then-
    /// split rule (never two independent half-rate roundings — the ±0.01-drift defect this project forbids).</summary>
    public static int RateBasisPoints(CompositionSubType subType) => subType switch
    {
        CompositionSubType.Manufacturer or CompositionSubType.Trader => 100,
        CompositionSubType.Restaurant => 500,
        CompositionSubType.ServiceProvider => 600,
        _ => throw new ArgumentOutOfRangeException(nameof(subType)),
    };

    /// <summary>True iff the sub-type taxes the <b>TOTAL</b> turnover in state (Manufacturer/Restaurant, incl. exempt);
    /// false ⇒ <b>TAXABLE-only</b> supplies (Trader/ServiceProvider). This is the §2.4 "base by dealer type" rule.</summary>
    public static bool TaxesTotalTurnover(CompositionSubType subType) =>
        subType is CompositionSubType.Manufacturer or CompositionSubType.Restaurant;
}
