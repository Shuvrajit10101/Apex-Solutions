namespace Apex.Ledger.Domain;

/// <summary>
/// An Indian State / Union Territory with its official 2-digit <b>GST state code</b> (catalog §12; phase4
/// RQ-2/RQ-11). The state code is the first two digits of a GSTIN and is the place-of-supply key for
/// intra-vs-inter determination. <see cref="IsUnionTerritory"/> marks a UT (without legislature) whose
/// "State" tax leg is <b>UTGST</b> — functionally parallel to SGST at the same rate (phase4 RQ-6/L-5);
/// Phase 4 treats SGST and UTGST as the single <see cref="GstTaxHead.State"/> head.
/// </summary>
/// <remarks>
/// This is static reference data (the official GST state-code list); it is not per-company config. The code
/// is a 2-character zero-padded string ("07" for Delhi) so it matches the leading two chars of a GSTIN
/// verbatim. Framework- and DB-agnostic.
/// </remarks>
public sealed class IndianState
{
    /// <summary>The official 2-digit GST state code, zero-padded (e.g. "07", "27").</summary>
    public string Code { get; }

    /// <summary>The State / UT name (e.g. "Delhi", "Maharashtra").</summary>
    public string Name { get; }

    /// <summary>True for a Union Territory whose state leg is UTGST (folded into <see cref="GstTaxHead.State"/>).</summary>
    public bool IsUnionTerritory { get; }

    private IndianState(string code, string name, bool isUnionTerritory)
    {
        Code = code;
        Name = name;
        IsUnionTerritory = isUnionTerritory;
    }

    /// <summary>
    /// The official GST state/UT code list (source: CBIC GST state code list). Codes 01–38 plus 97/99 for
    /// "Other Territory" / centre jurisdiction. UT flags mark the union territories (place of UTGST).
    /// </summary>
    public static readonly IReadOnlyList<IndianState> All = new[]
    {
        new IndianState("01", "Jammu and Kashmir", isUnionTerritory: true),
        new IndianState("02", "Himachal Pradesh", false),
        new IndianState("03", "Punjab", false),
        new IndianState("04", "Chandigarh", isUnionTerritory: true),
        new IndianState("05", "Uttarakhand", false),
        new IndianState("06", "Haryana", false),
        new IndianState("07", "Delhi", isUnionTerritory: true),
        new IndianState("08", "Rajasthan", false),
        new IndianState("09", "Uttar Pradesh", false),
        new IndianState("10", "Bihar", false),
        new IndianState("11", "Sikkim", false),
        new IndianState("12", "Arunachal Pradesh", false),
        new IndianState("13", "Nagaland", false),
        new IndianState("14", "Manipur", false),
        new IndianState("15", "Mizoram", false),
        new IndianState("16", "Tripura", false),
        new IndianState("17", "Meghalaya", false),
        new IndianState("18", "Assam", false),
        new IndianState("19", "West Bengal", false),
        new IndianState("20", "Jharkhand", false),
        new IndianState("21", "Odisha", false),
        new IndianState("22", "Chhattisgarh", false),
        new IndianState("23", "Madhya Pradesh", false),
        new IndianState("24", "Gujarat", false),
        new IndianState("26", "Dadra and Nagar Haveli and Daman and Diu", isUnionTerritory: true),
        new IndianState("27", "Maharashtra", false),
        new IndianState("29", "Karnataka", false),
        new IndianState("30", "Goa", false),
        new IndianState("31", "Lakshadweep", isUnionTerritory: true),
        new IndianState("32", "Kerala", false),
        new IndianState("33", "Tamil Nadu", false),
        new IndianState("34", "Puducherry", isUnionTerritory: true),
        new IndianState("35", "Andaman and Nicobar Islands", isUnionTerritory: true),
        new IndianState("36", "Telangana", false),
        new IndianState("37", "Andhra Pradesh", false),
        new IndianState("38", "Ladakh", isUnionTerritory: true),
        new IndianState("97", "Other Territory", false),
    };

    /// <summary>The valid GST state codes as a fast lookup set.</summary>
    private static readonly Dictionary<string, IndianState> ByCode =
        All.ToDictionary(s => s.Code, StringComparer.Ordinal);

    /// <summary>True iff <paramref name="code"/> is a valid 2-digit GST state code.</summary>
    public static bool IsValidCode(string? code) => code is not null && ByCode.ContainsKey(code);

    /// <summary>The state for a 2-digit code, or <c>null</c> if the code is not a valid GST state code.</summary>
    public static IndianState? FromCode(string? code) =>
        code is not null && ByCode.TryGetValue(code, out var s) ? s : null;

    /// <summary>The state for a name (case-insensitive), or <c>null</c> if unknown.</summary>
    public static IndianState? FromName(string? name) =>
        name is null ? null :
        All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
}
