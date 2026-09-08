using System.Globalization;
using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// Census 3.6 — the ONE place that turns a stored base-unit quantity into its <b>Alternate Unit</b> expression,
/// and back (schema v60).
///
/// <para><b>R7 — ATTESTED.</b> <c>help.tallysolutions.com/manage-stock-item-tally/</c>: the stock item takes a base
/// unit in <i>Units</i>, then "<i>The <b>Alternate units</b> field appears</i>" and the operator "<i>provide[s] the
/// conversion factor between the simple or compound units and alternative units</i>".</para>
///
/// <para>🔴 <b>WHY THIS IS A SERVICE AND NOT A COUPLE OF INLINE MULTIPLICATIONS.</b> The brief for this row named
/// the exact hazard: once an item has two units, every quantity in the product has two possible expressions and a
/// report or an invoice that mixes them prints a wrong figure that still looks like a quantity. This build removes
/// the hazard by construction rather than by discipline — <see cref="StockItem.AlternateUnitId"/> stores no
/// quantity at all, every stock table keeps the single base-unit column it always had, and <b>every</b> alternate
/// figure anywhere in the product is produced here, from the stored base quantity, at the moment it is displayed.
/// There is no second stored number to drift.</para>
///
/// <para><b>The factor's direction, stated once so no call site has to guess.</b>
/// <see cref="StockItem.AlternateUnitConversion"/> is <b>base units per ONE alternate unit</b> — the vendor's
/// "1 Box = 10 Nos" is <c>10</c> with base <i>Nos</i> and alternate <i>Box</i>. So base → alternate DIVIDES and
/// alternate → base MULTIPLIES. Getting this backwards is the one arithmetic error this feature can make, which
/// is why it is written down here and asserted in both directions by the tests.</para>
///
/// <para>Framework- and DB-agnostic; every method is pure. Culture-invariant: the formatter below uses
/// <see cref="CultureInfo.InvariantCulture"/> explicitly, because the gate also runs on ubuntu and macos where the
/// ambient culture can render a decimal point as a comma.</para>
/// </summary>
public static class AlternateUnitConversion
{
    /// <summary>
    /// True iff <paramref name="item"/> carries a usable alternate unit — BOTH an alternate unit id AND a strictly
    /// positive factor. The two are enforced together by <see cref="InventoryService"/>, so a half-set item cannot
    /// be saved; this predicate is the read-side belt-and-suspenders, and it is what every caller must ask before
    /// showing an alternate figure.
    /// </summary>
    public static bool HasAlternateUnit(StockItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.AlternateUnitId is not null
            && item.AlternateUnitConversion is { } f
            && f > 0m;
    }

    /// <summary>
    /// Converts a quantity expressed in the item's BASE unit into its ALTERNATE unit. Returns <c>null</c> when the
    /// item has no alternate unit — a caller that renders <c>null</c> as "0" would be asserting the item holds
    /// nothing, so the absence is returned rather than a number.
    /// </summary>
    public static decimal? ToAlternate(StockItem item, decimal baseQuantity)
        => HasAlternateUnit(item) ? baseQuantity / item.AlternateUnitConversion!.Value : null;

    /// <summary>
    /// Converts a quantity keyed in the item's ALTERNATE unit into the BASE unit this product actually stores.
    /// Returns <c>null</c> when the item has no alternate unit, so a caller cannot silently store an alternate
    /// figure as though it were a base one.
    /// </summary>
    public static decimal? ToBase(StockItem item, decimal alternateQuantity)
        => HasAlternateUnit(item) ? alternateQuantity * item.AlternateUnitConversion!.Value : null;

    /// <summary>
    /// The vendor-shaped one-line statement of an item's conversion — <c>"1 Box = 10 Nos"</c> — or <c>null</c> when
    /// the item has no alternate unit or either unit is unresolvable in <paramref name="company"/>. Built here so
    /// the master screen, the item list and any future report all print the same sentence.
    /// </summary>
    public static string? DescribeConversion(StockItem item, Company company)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (!HasAlternateUnit(item)) return null;

        var baseUnit = company.FindUnit(item.BaseUnitId);
        var altUnit = company.FindUnit(item.AlternateUnitId!.Value);
        if (baseUnit is null || altUnit is null) return null;

        var factor = item.AlternateUnitConversion!.Value;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"1 {altUnit.Symbol} = {Trim(factor)} {baseUnit.Symbol}");
    }

    /// <summary>Renders an exact decimal without trailing zeros, invariantly — "10" not "10.000000".</summary>
    private static string Trim(decimal value)
    {
        var s = value.ToString("0.######", CultureInfo.InvariantCulture);
        return s.Length == 0 ? "0" : s;
    }
}
