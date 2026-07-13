namespace Apex.Ledger.Domain;

/// <summary>
/// A <b>Payroll Unit</b> (Phase 8 slice 1; Study Guide pp.196–197) — the unit attendance / production is stated
/// in. It is either <b>Simple</b> — a symbol (e.g. "Days", "Hrs", "Month") + formal name + how many
/// <see cref="DecimalPlaces"/> (0–4) its values carry — or <b>Compound</b> — a first (base) unit × an exact
/// integer conversion factor + a tail unit (e.g. "Hrs of 60 Min", "Month of 26 Days"). Conversion is exact
/// integer arithmetic so it is reversible with no float drift. Mirrors <see cref="Unit"/> minus the GST UQC
/// (payroll units carry no GST classification).
/// </summary>
/// <remarks>
/// The <see cref="Id"/> is the stable key; the <see cref="Symbol"/> is not, so an Alter renames in place. A
/// compound unit references its first and tail simple units by id — both must exist and be distinct. Not seeded
/// on company creation (ER-13). Framework- and DB-agnostic.
/// </remarks>
public sealed class PayrollUnit
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>Display symbol (e.g. "Days", "Hrs"); unique within a company, required.</summary>
    public string Symbol { get; set; }

    /// <summary>Formal / descriptive name (e.g. "Days", "Hours"); required.</summary>
    public string FormalName { get; set; }

    /// <summary>True ⇒ a compound unit (first × factor + tail); false ⇒ a simple unit.</summary>
    public bool IsCompound { get; }

    /// <summary>Decimal places (0–4) a simple unit's values round to; 0 for a compound unit.</summary>
    public int DecimalPlaces { get; set; }

    /// <summary>The first (base) simple unit of a compound unit; <c>null</c> for a simple unit.</summary>
    public Guid? FirstUnitId { get; }

    /// <summary>The tail simple unit of a compound unit; <c>null</c> for a simple unit.</summary>
    public Guid? TailUnitId { get; }

    /// <summary>Conversion factor numerator (tail units per one first unit) — the "60" in "Hrs of 60 Min".
    /// <c>null</c> for a simple unit; &gt; 0 for a compound unit.</summary>
    public int? ConversionNumerator { get; }

    /// <summary>Conversion factor denominator (defaulting to 1). <c>null</c> for a simple unit; &gt; 0 for a
    /// compound unit.</summary>
    public int? ConversionDenominator { get; }

    private PayrollUnit(
        Guid id,
        string symbol,
        string formalName,
        bool isCompound,
        int decimalPlaces,
        Guid? firstUnitId,
        Guid? tailUnitId,
        int? conversionNumerator,
        int? conversionDenominator)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Payroll unit symbol is required.", nameof(symbol));
        if (string.IsNullOrWhiteSpace(formalName))
            throw new ArgumentException("Payroll unit formal name is required.", nameof(formalName));

        Id = id;
        Symbol = symbol.Trim();
        FormalName = formalName.Trim();
        IsCompound = isCompound;
        DecimalPlaces = decimalPlaces;
        FirstUnitId = firstUnitId;
        TailUnitId = tailUnitId;
        ConversionNumerator = conversionNumerator;
        ConversionDenominator = conversionDenominator;
    }

    /// <summary>Creates a <b>simple</b> payroll unit: symbol + formal name + decimal places (0–4).</summary>
    public static PayrollUnit Simple(Guid id, string symbol, string formalName, int decimalPlaces = 0)
    {
        if (decimalPlaces is < 0 or > 4)
            throw new ArgumentException("A payroll unit's decimal places must be between 0 and 4.", nameof(decimalPlaces));

        return new PayrollUnit(id, symbol, formalName, isCompound: false, decimalPlaces,
            firstUnitId: null, tailUnitId: null, conversionNumerator: null, conversionDenominator: null);
    }

    /// <summary>Creates a <b>compound</b> payroll unit: <paramref name="firstUnitId"/> ×
    /// (<paramref name="conversionNumerator"/> / <paramref name="conversionDenominator"/>) +
    /// <paramref name="tailUnitId"/> (e.g. "Hrs of 60 Min"). The factor must be &gt; 0 and the first unit must
    /// differ from the tail.</summary>
    public static PayrollUnit Compound(
        Guid id,
        string symbol,
        string formalName,
        Guid firstUnitId,
        Guid tailUnitId,
        int conversionNumerator,
        int conversionDenominator = 1)
    {
        if (firstUnitId == tailUnitId)
            throw new ArgumentException("A compound payroll unit's first and tail units must be different.", nameof(tailUnitId));
        if (conversionNumerator <= 0)
            throw new ArgumentException("Conversion factor numerator must be > 0.", nameof(conversionNumerator));
        if (conversionDenominator <= 0)
            throw new ArgumentException("Conversion factor denominator must be > 0.", nameof(conversionDenominator));

        return new PayrollUnit(id, symbol, formalName, isCompound: true, decimalPlaces: 0,
            firstUnitId, tailUnitId, conversionNumerator, conversionDenominator);
    }

    /// <summary>Rehydrates a payroll unit from persisted fields (the SQLite adapter / import).</summary>
    public static PayrollUnit FromStorage(
        Guid id,
        string symbol,
        string formalName,
        bool isCompound,
        int decimalPlaces,
        Guid? firstUnitId,
        Guid? tailUnitId,
        int? conversionNumerator,
        int? conversionDenominator)
    {
        return isCompound
            ? Compound(id, symbol, formalName,
                firstUnitId ?? throw new ArgumentNullException(nameof(firstUnitId), "A compound payroll unit needs a first unit."),
                tailUnitId ?? throw new ArgumentNullException(nameof(tailUnitId), "A compound payroll unit needs a tail unit."),
                conversionNumerator ?? throw new ArgumentNullException(nameof(conversionNumerator), "A compound payroll unit needs a factor."),
                conversionDenominator ?? 1)
            : Simple(id, symbol, formalName, decimalPlaces);
    }
}
