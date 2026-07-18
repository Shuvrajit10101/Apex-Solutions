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
    /// underlying base measure (its <see cref="TailUnitId"/>). For a simple unit the quantity is already in
    /// its own measure, so it is returned unchanged. For a compound unit the canonical invariant is
    /// <c>1 × FirstUnit = (numerator/denominator) × TailUnit</c> — e.g. 1 Dozen = 12 Nos — so a quantity
    /// stated in the compound (a count of FIRST units) is scaled UP by the exact integer factor to land in
    /// the smaller TAIL unit (no float drift, RQ-4/DP-6). The stock-movement engine calls this to normalise
    /// a line's quantity to the item's base unit before accumulating on-hand.
    /// </summary>
    /// <remarks>
    /// The direction is fixed by the corpus (R7): the Tally Prime Book (§ Compound Unit) defines
    /// "Doz (Dozen) of 12 Nos (Numbers)" with <b>First Unit = "Dozen"</b> and <b>Second/Tail Unit =
    /// "Numbers"</b>, and the Study Guide's table lists First/Factor/Second as Dozen/12/Pcs, Kg/1000/Grams,
    /// Box/20/Pcs. The FIRST unit is always the LARGER unit and the TAIL the smaller one, so scaling a
    /// count of First units by the factor can only yield TAIL units.
    /// </remarks>
    public decimal QuantityInBaseMeasure(decimal quantity)
    {
        if (!IsCompound) return quantity;
        // numerator/denominator = TAIL units per one FIRST unit ("the 12 in 1 Dozen = 12 Nos"), so
        // multiplying a quantity stated in this compound unit yields a quantity in the TAIL unit.
        return quantity * ConversionNumerator!.Value / ConversionDenominator!.Value;
    }

    /// <summary>
    /// The id of the underlying base measure this unit's quantities normalise to: a compound unit's
    /// <see cref="TailUnitId"/> (the smaller unit <see cref="QuantityInBaseMeasure"/> scales into), or the
    /// unit itself when simple. A voucher line may only state its quantity in a unit whose
    /// <c>BaseMeasureUnitId</c> equals the stock item's own base unit.
    /// </summary>
    public Guid BaseMeasureUnitId => TailUnitId ?? Id;

    /// <summary>
    /// <b>RATE SEMANTICS (WI-10 slice C) — the rate on a line is PER THE UNIT THE LINE IS STATED IN.</b>
    /// "2 Doz-Nos @ ₹10.00" is <b>₹20.00</b>, not ₹240.00 — the conventional invoice reading, and the one
    /// the corpus shows: a Tally invoice line carries an explicit <b>"per"</b> column naming the rate's unit
    /// (<c>tally/719244897-Tally-Book.pdf</c>, "Quantity | Rate per | Amount"), and its worked example
    /// reads Quantity 2 · Rate 10,000 · Amount 20,000 — i.e. <c>Amount = Quantity × Rate</c> with the
    /// quantity in the unit shown, never silently re-expressed in some smaller base unit. (See also
    /// <c>tally/703679456-TALLY-PRIME-WITH-GST-Notes-PDF.pdf</c>: "purchased 10 nos … for 6000 per piece".)
    /// <para>
    /// Converts a per-<b>this</b>-unit <paramref name="rate"/> into the equivalent per-<b>base-measure</b>
    /// rate, the inverse of <see cref="QuantityInBaseMeasure"/>. Valuation works in base units, so a rate
    /// must be divided by exactly the factor the quantity was multiplied by — otherwise value would inflate
    /// by that factor (a 12× error on every such line, flowing straight into GST). The product
    /// <c>QuantityInBaseMeasure(q) × RateInBaseMeasure(r)</c> is therefore <c>q × r</c>: the line total is
    /// invariant under the conversion, which is exactly the property the arithmetic test locks.
    /// </para>
    /// </summary>
    /// <remarks>
    /// The per-base rate can be non-terminating (₹10 per Dozen = ₹0.8333… per Nos). That is deliberate: it
    /// is a derived intermediate, kept at full <see cref="decimal"/> precision so the recomposed line VALUE
    /// rounds to the correct paisa. Only the value is money; the per-base rate is never itself persisted.
    /// </remarks>
    public decimal RateInBaseMeasure(decimal rate)
    {
        if (!IsCompound) return rate;
        return rate * ConversionDenominator!.Value / ConversionNumerator!.Value;
    }

    /// <summary>
    /// The exact inverse of <see cref="RateInBaseMeasure"/>: converts a per-<b>base-measure</b>
    /// <paramref name="baseRate"/> back into the equivalent rate per <b>this</b> unit (₹0.8333…/Nos ⇒
    /// ₹10.00/Doz). Use this whenever a rate DERIVED in base units (a live issue cost, a running average, a
    /// layer cost) is about to be stored on, or displayed against, a line whose quantity is stated in this
    /// compound unit.
    /// <para>
    /// <b>THE RISK CLASS THIS GUARDS (WI-10 defects D1–D4).</b> A site is correct only when the QUANTITY and
    /// the RATE are expressed in the SAME unit. There are exactly two ways to break that, and BOTH misstate
    /// money by precisely the conversion factor:
    /// </para>
    /// <list type="number">
    ///   <item><b>Over-statement (12×)</b> — a base-normalised quantity (24 Nos) paired with a
    ///     per-displayed-unit rate (₹10/Doz) reports ₹240 instead of ₹20. Fixed by
    ///     <see cref="RateInBaseMeasure"/> (defects D1/D2/D3).</item>
    ///   <item><b>Under-statement (12×)</b> — a per-displayed-unit quantity (2 Doz) paired with a per-base
    ///     rate, or equivalently a rate converted TWICE, reports ₹2 instead of ₹24. Fixed by THIS method
    ///     (defect D4).</item>
    /// </list>
    /// Blanket-converting to cure one direction simply creates the other; the only safe rule is to name the
    /// unit each side is in at every site that multiplies them.
    /// </summary>
    public decimal RateFromBaseMeasure(decimal baseRate)
    {
        if (!IsCompound) return baseRate;
        return baseRate * ConversionNumerator!.Value / ConversionDenominator!.Value;
    }

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
