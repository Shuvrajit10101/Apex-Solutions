namespace Apex.Ledger.Domain;

/// <summary>
/// A Unit of Measure (catalog §9; requirements RQ-3/RQ-4). A unit is either <b>Simple</b> — a symbol
/// (e.g. "Nos"), a formal name, a GST <b>UQC</b> placeholder (inert until a later GST slice) and how many
/// <see cref="DecimalPlaces"/> (0–4) its quantities carry — or <b>Compound</b> — a first (base) unit ×
/// an exact integer conversion factor + a tail unit (e.g. "Dozen = 12 Nos", "Box = 20 Nos",
/// "Kg = 1000 g"). Conversion is exact integer arithmetic (numerator/denominator) so it is reversible for
/// display in either unit with no float drift.
/// </summary>
/// <remarks>
/// The <see cref="Id"/> is the stable key; the <see cref="Symbol"/> is not, so an Alter renames in place.
/// A compound unit references its first and tail simple units by id — both must already exist and be
/// distinct (RQ-4). A simple unit carries the decimal precision; a compound unit inherits precision from
/// its component simple units, so its own <see cref="DecimalPlaces"/> is 0.
/// </remarks>
public sealed class Unit
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>Display symbol (e.g. "Nos", "Kg", "Dozen"); unique within a company, required.</summary>
    public string Symbol { get; set; }

    /// <summary>Formal / descriptive name (e.g. "Numbers", "Kilograms"); required.</summary>
    public string FormalName { get; set; }

    /// <summary>True ⇒ a compound unit (first × factor + tail); false ⇒ a simple unit.</summary>
    public bool IsCompound { get; }

    /// <summary>
    /// GST Unit Quantity Code placeholder (e.g. "NOS", "KGS") — captured but inert until the GST slice.
    /// Simple units only; <c>null</c> for a compound unit.
    /// </summary>
    public string? UnitQuantityCode { get; set; }

    /// <summary>Decimal places (0–4) a simple unit's quantities round to; 0 for a compound unit.</summary>
    public int DecimalPlaces { get; set; }

    /// <summary>The first (base) simple unit of a compound unit; <c>null</c> for a simple unit.</summary>
    public Guid? FirstUnitId { get; }

    /// <summary>The tail simple unit of a compound unit; <c>null</c> for a simple unit.</summary>
    public Guid? TailUnitId { get; }

    /// <summary>
    /// Conversion factor numerator (how many tail units are in one first unit) — the "12" in
    /// "1 Dozen = 12 Nos". <c>null</c> for a simple unit; &gt; 0 for a compound unit.
    /// </summary>
    public int? ConversionNumerator { get; }

    /// <summary>
    /// Conversion factor denominator, defaulting to 1 (so the factor is <c>numerator/denominator</c>
    /// tail-per-first). <c>null</c> for a simple unit; &gt; 0 for a compound unit.
    /// </summary>
    public int? ConversionDenominator { get; }

    private Unit(
        Guid id,
        string symbol,
        string formalName,
        bool isCompound,
        string? unitQuantityCode,
        int decimalPlaces,
        Guid? firstUnitId,
        Guid? tailUnitId,
        int? conversionNumerator,
        int? conversionDenominator)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Unit symbol is required.", nameof(symbol));
        if (string.IsNullOrWhiteSpace(formalName))
            throw new ArgumentException("Unit formal name is required.", nameof(formalName));

        Id = id;
        Symbol = symbol.Trim();
        FormalName = formalName.Trim();
        IsCompound = isCompound;
        UnitQuantityCode = string.IsNullOrWhiteSpace(unitQuantityCode) ? null : unitQuantityCode.Trim();
        DecimalPlaces = decimalPlaces;
        FirstUnitId = firstUnitId;
        TailUnitId = tailUnitId;
        ConversionNumerator = conversionNumerator;
        ConversionDenominator = conversionDenominator;
    }

    /// <summary>
    /// Creates a <b>simple</b> unit: symbol + formal name + optional UQC + decimal places (0–4, RQ-3).
    /// </summary>
    public static Unit Simple(
        Guid id,
        string symbol,
        string formalName,
        int decimalPlaces = 0,
        string? unitQuantityCode = null)
    {
        if (decimalPlaces is < 0 or > 4)
            throw new ArgumentException("A unit's decimal places must be between 0 and 4.", nameof(decimalPlaces));

        return new Unit(id, symbol, formalName, isCompound: false, unitQuantityCode, decimalPlaces,
            firstUnitId: null, tailUnitId: null, conversionNumerator: null, conversionDenominator: null);
    }

    /// <summary>
    /// Creates a <b>compound</b> unit: <paramref name="firstUnitId"/> × (<paramref name="conversionNumerator"/>
    /// / <paramref name="conversionDenominator"/>) + <paramref name="tailUnitId"/> (e.g. Dozen = 12 Nos).
    /// The factor must be &gt; 0 and the first unit must differ from the tail unit (RQ-4).
    /// </summary>
    public static Unit Compound(
        Guid id,
        string symbol,
        string formalName,
        Guid firstUnitId,
        Guid tailUnitId,
        int conversionNumerator,
        int conversionDenominator = 1)
    {
        if (firstUnitId == tailUnitId)
            throw new ArgumentException("A compound unit's first and tail units must be different.", nameof(tailUnitId));
        if (conversionNumerator <= 0)
            throw new ArgumentException("Conversion factor numerator must be > 0.", nameof(conversionNumerator));
        if (conversionDenominator <= 0)
            throw new ArgumentException("Conversion factor denominator must be > 0.", nameof(conversionDenominator));

        return new Unit(id, symbol, formalName, isCompound: true, unitQuantityCode: null, decimalPlaces: 0,
            firstUnitId, tailUnitId, conversionNumerator, conversionDenominator);
    }

    /// <summary>
    /// Converts a <paramref name="quantity"/> expressed in <b>this</b> unit into a quantity in the unit's
    /// underlying base measure (its <see cref="FirstUnitId"/>). For a simple unit the quantity is already in
    /// its own measure, so it is returned unchanged. For a compound unit "1 first = (numerator/denominator)"
    /// of the base measure — e.g. 1 Dozen = 12 Nos — so the quantity is scaled by the exact integer factor
    /// (no float drift, RQ-4/DP-6). The stock-movement engine calls this to normalise a line's quantity to
    /// the item's base unit before accumulating on-hand.
    /// </summary>
    public decimal QuantityInBaseMeasure(decimal quantity)
    {
        if (!IsCompound) return quantity;
        // numerator/denominator = base-measure units per one of THIS compound unit.
        return quantity * ConversionNumerator!.Value / ConversionDenominator!.Value;
    }

    /// <summary>The id of the underlying base measure this unit's quantities normalise to: a compound unit's
    /// <see cref="FirstUnitId"/>, or the unit itself when simple.</summary>
    public Guid BaseMeasureUnitId => FirstUnitId ?? Id;

    /// <summary>
    /// Rehydrates a unit from persisted fields (the SQLite adapter). Chooses simple or compound by
    /// <paramref name="isCompound"/> and applies the same invariants as the factory methods.
    /// </summary>
    public static Unit FromStorage(
        Guid id,
        string symbol,
        string formalName,
        bool isCompound,
        string? unitQuantityCode,
        int decimalPlaces,
        Guid? firstUnitId,
        Guid? tailUnitId,
        int? conversionNumerator,
        int? conversionDenominator)
    {
        return isCompound
            ? Compound(id, symbol, formalName,
                firstUnitId ?? throw new ArgumentNullException(nameof(firstUnitId), "A compound unit needs a first unit."),
                tailUnitId ?? throw new ArgumentNullException(nameof(tailUnitId), "A compound unit needs a tail unit."),
                conversionNumerator ?? throw new ArgumentNullException(nameof(conversionNumerator), "A compound unit needs a factor."),
                conversionDenominator ?? 1)
            : Simple(id, symbol, formalName, decimalPlaces, unitQuantityCode);
    }
}
