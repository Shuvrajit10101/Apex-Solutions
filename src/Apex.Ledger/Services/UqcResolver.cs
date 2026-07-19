using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// How one item line's quantity is to be <b>DECLARED</b> on a statutory document — the UQC code that labels it,
/// the quantity expressed in that code's unit, and the unit rate re-expressed per that same unit.
/// </summary>
/// <param name="Code">
/// The GST Unit Quantity Code to emit. Three cases:
/// <list type="number">
///   <item>A <b>valid code</b> — either the line unit's own (the preferred path) or the item's base-unit code
///     (the fallback, when the base rate converts exactly).</item>
///   <item><c>"OTH"</c> — the department's code for a unit absent from the master list. Emitted when the line's
///     unit maps to no valid UQC AND its per-base rate is not paisa-exact, so converting to base could not be
///     represented without breaking the <c>quantity x rate = value</c> footing.</item>
///   <item><c>null</c> — the item's base unit carries no UQC at all (the pre-existing "unmapped unit" case,
///     reachable only on a line that states no unit of its own). Each writer keeps its long-standing fallback:
///     GSTR-1 leaves the column blank, the NIC writers emit <c>"OTH"</c>. This is deliberately NOT the same as
///     case 2 — a null blanks a filed column, so the two must never be conflated.</item>
/// </list>
/// </param>
/// <param name="Quantity">The quantity stated in <paramref name="Code"/>'s unit.</param>
/// <param name="Rate">The unit rate stated PER <paramref name="Code"/>'s unit.</param>
/// <param name="BaseQuantity">
/// The SAME physical quantity expressed in the stock item's base unit (24, where <see cref="Quantity"/> is 2 DOZ).
/// Always populated, so a consumer that aggregates lines of differing units has one commensurable measure to fall
/// back to without re-deriving the conversion.
/// </param>
/// <param name="BaseCode">The item's base-unit UQC — the label <see cref="BaseQuantity"/> is stated in.</param>
/// <param name="DeclaredUnitId">
/// The <b>identity of the simple unit whose count <paramref name="Quantity"/> states</b> — the item's base unit on
/// every path that declares in the base unit, and the compound's FIRST unit on the two paths that declare in the
/// line's own unit (a compound quantity is a count of first units either way).
///
/// <para><b>Why an identity and not just <paramref name="Code"/>.</b> An aggregator that spans lines must decide
/// whether two quantities may be ADDED. <c>"OTH"</c> is the department's catch-all for a unit absent from the
/// master list, so it is a <i>label for a gap</i>, not a unit: two lines stated in DIFFERENT unmapped units both
/// declare <c>"OTH"</c>, and an aggregator comparing labels sums 5 Crates and 3 Pallets into "8 OTH". This member
/// is what makes them distinguishable. Never compare <paramref name="Code"/> alone — call
/// <see cref="UqcResolver.AreCommensurable"/>.</para>
/// </param>
/// <param name="BaseUnitId">
/// The identity of the item's base unit — the unit <paramref name="BaseQuantity"/> counts, and the label
/// <paramref name="BaseCode"/> names. Lets an aggregator decide whether even the DEGRADE target is commensurable
/// (two items sharing an HSN but measured in unrelated bases are not).
/// </param>
public readonly record struct UqcDeclaration(
    string? Code, decimal Quantity, decimal Rate, decimal BaseQuantity, string? BaseCode,
    Guid? DeclaredUnitId, Guid? BaseUnitId);

/// <summary>
/// <b>Resolves the statutory unit declaration of an item line (WI-10 Gap 2, follow-on).</b>
///
/// <para><b>The defect this exists to prevent.</b> Before item-invoice line units, a line's quantity was ALWAYS
/// in the item's base unit, so labelling it with the item's base UQC was correct by construction. Once a line
/// may state "2 Doz" of a Nos-measured item, a writer that emits <c>il.Quantity</c> beside the BASE UQC declares
/// <b>2 NOS</b> for a consignment in which <b>24 NOS</b> physically move. The money stays right (₹20 either way),
/// so no money assertion fails — which is exactly why three statutory writers carried the defect undetected:
/// <c>Gstr1</c> Table-12 HSN summary, <c>EInvoiceJson</c> INV-01 and <c>EWayBillJson</c> EWB-01.</para>
///
/// <para><b>The rule.</b> Declare the line's OWN unit when it maps to a valid UQC — which is what
/// <c>VoucherPrintProjector</c> already does for the printed invoice, so the printed document, the e-invoice, the
/// e-way bill and GSTR-1 all describe the SAME physical quantity. Where it does not map, fall back to the base
/// unit with the quantity converted into it. <b>Converting the quantity without the rate (or vice versa) is the
/// 12× money defect</b> that <see cref="Unit.RateInBaseMeasure"/> documents, so this type converts BOTH together
/// and hands the caller a matched (quantity, rate) pair — a caller can never convert one and forget the other.</para>
///
/// <para><b>Why the fallback is conditional.</b> Converting is only possible when the per-base rate lands
/// paisa-exact. ₹10 per Crate of 12 is ₹0.8333…/Nos, and the NIC unit-price field is integer paisa — so a
/// converted declaration would have to round, and <c>quantity × unit_price</c> would stop recomposing the
/// assessable amount by an amount that grows with the quantity (half a paisa per base unit). That residual is
/// irreducible, and deriving the assessable amount instead is inadmissible: it is the figure actually TAXED and
/// must reconcile to the posted Sales leg. So where the conversion is not representable the line is declared in
/// its OWN unit under <c>"OTH"</c> — which foots exactly and, as a bonus, makes all four documents agree.</para>
///
/// <para><b>Why the FIRST unit's UQC.</b> A compound unit is <c>1 × First = factor × Tail</c> ("1 Doz = 12 Nos"),
/// and a quantity stated in the compound is a count of FIRST units — so the First unit's UQC (DOZ) is precisely
/// what labels it. A compound unit carries no UQC of its own (<see cref="Unit.UnitQuantityCode"/> is null for a
/// compound), so there is nothing else it could correctly be.</para>
/// </summary>
public static class UqcResolver
{
    /// <summary>
    /// The <b>controlled statutory vocabulary</b> of GST Unit Quantity Codes. A code outside this set must never
    /// be emitted on a return or a NIC payload — the portal rejects the filing.
    ///
    /// <para><b>Source (R7).</b> Transcribed from the official NIC e-invoice portal's published master codes —
    /// <c>https://einvoice1.gst.gov.in/Others/MasterCodes</c> (UQC master, 45 codes) — which is the list the IRP
    /// and the e-way bill system actually validate <c>Unit</c> against, and therefore the authority that governs
    /// two of the three writers here. <b>Retrieved and re-verified against that primary source on 2026-07-19:</b>
    /// the page returned exactly these 45 codes, character for character. Re-check on a NIC master-code revision;
    /// this set is a dated snapshot of a list the department can extend, not an immutable constant.</para>
    ///
    /// <para><b>Do not re-source this from a secondary aggregator.</b> Third-party UQC listings circulate with
    /// transcription drift, and a wrong code is a rejected filing while a MISSING code silently routes a line down
    /// the base-unit fallback instead of failing loudly. Only the NIC master is authoritative here.</para>
    ///
    /// <para>The corpus corroborates the CONCEPT and the mapping workflow but publishes
    /// no list: the Study Guide (<c>tally/696054070-TALLY-PRIME-STUDY-GUIDE.pdf</c>) defines UQC as "a uniform
    /// measuring unit used by department … you can map your unit of measurement to the UQC given by the
    /// department", and the GST notes (<c>tally/703679456-TALLY-PRIME-WITH-GST-Notes-PDF.pdf</c>) show selections
    /// "NOS-NUMBERS" and "BOX-BOX" — all three of which appear below, as does the "PCS" the Study Guide uses.</para>
    ///
    /// <para>Kept as an explicit set rather than inferred: an unrecognised code is not a formatting nit, it is a
    /// rejected return, so the fallback path (declare the base unit, convert the quantity) must be reachable.</para>
    /// </summary>
    public static readonly IReadOnlySet<string> ValidCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "BAG", "BAL", "BDL", "BKL", "BOU", "BOX", "BTL", "BUN", "CAN", "CBM",
        "CCM", "CMS", "CTN", "DOZ", "DRM", "GGK", "GMS", "GRS", "GYD", "KGS",
        "KLR", "KME", "LTR", "MLT", "MTR", "MTS", "NOS", "OTH", "PAC", "PCS",
        "PRS", "QTL", "ROL", "SET", "SQF", "SQM", "SQY", "TBS", "TGM", "THD",
        "TON", "TUB", "UGS", "UNT", "YDS",
    };

    /// <summary>
    /// The subset of <see cref="ValidCodes"/> that are <b>CATCH-ALLS</b> — a code that labels a <i>gap</i> in the
    /// department's master list rather than naming a unit. <c>"OTH"</c> ("Others") is the published one.
    ///
    /// <para><b>Why this has to exist.</b> Every other code identifies a unit, so two rows carrying it are
    /// genuinely addable. A catch-all identifies nothing: 5 Crates and 3 Pallets are BOTH declared <c>"OTH"</c>,
    /// so <b>equality of two catch-all codes is not evidence of sameness</b> and summing on it files "8" for a
    /// consignment of 81. Both reachable ways in — the fallback in <see cref="Declare"/> synthesizes <c>"OTH"</c>,
    /// and a user may LEGITIMATELY map a unit master to it because the department publishes it — collide on the
    /// same label, so this cannot be a <c>!= "OTH"</c> guard on the fallback path alone.</para>
    ///
    /// <para><b>Source (R7).</b> Same dated NIC master-code snapshot as <see cref="ValidCodes"/>; re-check both
    /// together on a NIC revision. A newly published catch-all is then a one-line addition here rather than a hunt
    /// for <c>"OTH"</c> string literals across the aggregators. Kept as a named set for exactly that reason.</para>
    /// </summary>
    public static readonly IReadOnlySet<string> CatchAllCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "OTH",
    };

    /// <summary>
    /// True when <paramref name="code"/> is a UQC the portal accepts. Comparison is case-insensitive on input and
    /// the caller receives the canonical UPPERCASE form from <see cref="Declare"/>; whitespace-only and
    /// <c>null</c> are not valid.
    /// </summary>
    public static bool IsValid(string? code) =>
        !string.IsNullOrWhiteSpace(code) && ValidCodes.Contains(code.Trim().ToUpperInvariant());

    /// <summary>
    /// True when <paramref name="code"/> is a <see cref="CatchAllCodes">catch-all</see> — a label two
    /// DIFFERENT units can both carry, so its equality proves nothing about commensurability.
    /// </summary>
    public static bool IsCatchAll(string? code) =>
        !string.IsNullOrWhiteSpace(code) && CatchAllCodes.Contains(code.Trim().ToUpperInvariant());

    /// <summary>
    /// <b>The commensurability predicate — may two declared quantities be ADDED and stated under one label?</b>
    /// Every aggregator that spans lines or periods must ask this; comparing UQC codes alone is unsound the moment
    /// one of them is a catch-all.
    ///
    /// <para>Yes on either ground:</para>
    /// <list type="number">
    ///   <item><b>Same unit identity.</b> The quantities count the same physical unit, whatever it is labelled —
    ///     the only ground available when the label is a catch-all, or absent entirely.</item>
    ///   <item><b>Same real UQC.</b> Two DIFFERENT unit masters legitimately mapped to the same non-catch-all code
    ///     (a "Dozen" master and a "Dz" master, both DOZ) genuinely count the same unit, and this is often the only
    ///     sound justification available for summing them. Distrusting every label would degrade that correct sum
    ///     onto the base-unit path, which is itself unsound when the bases differ — a second defect, not a
    ///     conservative choice.</item>
    /// </list>
    ///
    /// <para><b>Neither a null identity nor a null/blank code is evidence of sameness</b> — two unknowns are not
    /// known to be equal. (The superseded label comparison treated <c>null == null</c> as agreement.) In
    /// particular <c>AreCommensurable(null, null, null, null)</c> is <b>FALSE, deliberately and not by
    /// oversight</b> — the predicate is not reflexive on a wholly unknown pair, and both aggregators run it on
    /// their SEEDING iteration, so a lone row with neither identity nor code flags itself. See the call sites in
    /// <c>Gstr1</c>/<c>Gstr9</c>, where that is load-bearing rather than incidental.</para>
    ///
    /// <para><b>Label equality is case-INSENSITIVE</b>, matching <see cref="IsValid"/> and <see cref="IsCatchAll"/>.
    /// The codes reaching here are not uniformly canonical: <c>Declare</c> returns <c>firstCode</c> upper-cased but
    /// passes the item's base UQC through RAW (ER-13 forbids re-casing a line that carries no unit), so a company
    /// storing a base UQC lowercase would otherwise compare "DOZ" against "doz" and refuse a genuinely
    /// commensurable pair. Refusing over-degrades rather than over-sums, so the old behaviour was safe — but it
    /// was an inconsistency, not a decision. <b>The catch-all guard is unaffected by this and cannot be bypassed
    /// by casing:</b> <see cref="IsCatchAll"/> canonicalises its input, so "OTH", "oth" and any mix are all
    /// refused on the label ground.</para>
    /// </summary>
    public static bool AreCommensurable(Guid? unitA, string? codeA, Guid? unitB, string? codeB)
    {
        if (unitA is { } a && unitB is { } b && a == b) return true;
        if (string.IsNullOrWhiteSpace(codeA) || string.IsNullOrWhiteSpace(codeB)) return false;
        return string.Equals(codeA, codeB, StringComparison.OrdinalIgnoreCase) && !IsCatchAll(codeA);
    }

    /// <summary>
    /// Resolves how <paramref name="line"/> must be declared, given the <paramref name="quantity"/> the caller
    /// intends to declare (GSTR-1 declares the ACTUAL quantity; the NIC writers declare the BILLED quantity, so
    /// the caller passes the one its document uses).
    ///
    /// <para>Order: a line with <b>no unit</b> is already in the item's base unit and is returned completely
    /// untouched — every pre-v46 line therefore declares byte-identically to before (ER-13). A <b>simple</b> line
    /// unit is the item's base unit (the validator requires <c>BaseMeasureUnitId == item.BaseUnitId</c>), so it too
    /// is untouched. A <b>compound</b> line unit declares its FIRST unit's UQC when that is a valid code; else it
    /// falls back to the base UQC with quantity AND rate converted together, but only where the converted rate is
    /// paisa-exact — where it is not, the line is declared in its own unit under <c>"OTH"</c> so the
    /// <c>quantity × rate = value</c> footing survives.</para>
    /// </summary>
    public static UqcDeclaration Declare(Company company, VoucherInventoryLine line, decimal quantity)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(line);

        var item = company.FindStockItem(line.StockItemId);
        // Deliberately RAW, not canonicalised: the base-unit code is what every writer already emitted, and
        // re-casing it would change the output of a line that carries no unit at all (ER-13).
        var baseCode = company.FindUnit(item?.BaseUnitId ?? Guid.Empty)?.UnitQuantityCode;
        var rate = line.Rate.Amount;

        // No line unit ⇒ the quantity is already in the item's base unit. Identical to pre-v46 (ER-13).
        if (line.UnitId is not { } unitId)
            return new UqcDeclaration(baseCode, quantity, rate, quantity, baseCode, item?.BaseUnitId, item?.BaseUnitId);

        var unit = company.FindUnit(unitId);
        // An unresolvable unit cannot be converted through, so declaring a converted quantity is impossible;
        // the validator rejects such a line at post time, so this is a defensive floor, not a live path.
        if (unit is null || !unit.IsCompound)
            return new UqcDeclaration(baseCode, quantity, rate, quantity, baseCode, item?.BaseUnitId, item?.BaseUnitId);

        var baseQuantity = unit.QuantityInBaseMeasure(quantity);

        // The line quantity is a count of FIRST units ("2 Doz"), so the FIRST unit's UQC is what labels it.
        var firstCode = Canonical(company.FindUnit(unit.FirstUnitId ?? Guid.Empty)?.UnitQuantityCode);
        if (IsValid(firstCode))
            return new UqcDeclaration(firstCode, quantity, rate, baseQuantity, baseCode, unit.FirstUnitId, item?.BaseUnitId);

        // Unmapped compound: declaring the BASE unit means scaling the quantity UP by the factor and dividing the
        // rate by the SAME factor — both together, or the declared money moves by the factor.
        //
        // But the per-base rate can be NON-TERMINATING (₹10/Crate of 12 = ₹0.8333…/Nos). The NIC unit-price field
        // is integer paisa, so such a rate must be rounded, and then qty x unit_price no longer recomposes the
        // assessed amount: 600 Nos @ ₹0.83 foots to ₹498.00 against an ass_amt of ₹500.00. The residual is
        // IRREDUCIBLE — the exact integer paisa price would be 1000 x 50000 / 600000 = 83.33…, so no rounding rule
        // can fix it — and it scales with the quantity (bounded by half a paisa per base unit, so ~₹500 on 100,000
        // units), which can hard-reject the invoice at the IRP. Deriving ass_amt instead is inadmissible: that is
        // the amount actually TAXED and must reconcile to the posted Sales leg and to GSTR-1.
        //
        // So convert only when the conversion is REPRESENTABLE. Otherwise declare the line's own quantity and its
        // raw entered rate — which foot exactly — under "OTH", the department's own code for a unit absent from
        // the master list. Returned EXPLICITLY, never null: null means "the base unit carries no UQC at all" and
        // makes GSTR-1 blank the column, so conflating the two would silently blank a filed field. This also makes
        // all four documents agree on "50" where they previously split 50 Crate / 600 NOS.
        var baseRate = unit.RateInBaseMeasure(rate);
        if (new Money(baseRate).IsPaisaExact)
            return new UqcDeclaration(baseCode, baseQuantity, baseRate, baseQuantity, baseCode,
                item?.BaseUnitId, item?.BaseUnitId);

        // The synthesized code is a CATCH-ALL, so the DeclaredUnitId carried beside it is the only thing that
        // distinguishes this line from any other unmapped-unit line sharing the HSN. See AreCommensurable.
        return new UqcDeclaration("OTH", quantity, rate, baseQuantity, baseCode, unit.FirstUnitId, item?.BaseUnitId);
    }

    private static string? Canonical(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
}
