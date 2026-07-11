using System;
using System.Globalization;
using Apex.Ledger;

namespace Apex.Desktop.Services;

/// <summary>
/// Indian-numbering-system money formatting (lakh/crore grouping) for the
/// right-aligned amount columns, e.g. 105000 → "1,05,000.00". A zero renders blank to
/// match the empty-cell convention in report grids.
/// </summary>
public static class IndianFormat
{
    private static readonly CultureInfo Indian = CreateIndianCulture();

    private static CultureInfo CreateIndianCulture()
    {
        // Build an invariant-based culture with the Indian digit-grouping (3;2;2) so the
        // format is deterministic regardless of the host machine's locale.
        var ci = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        ci.NumberFormat.CurrencyGroupSizes = new[] { 3, 2 };
        ci.NumberFormat.NumberGroupSizes = new[] { 3, 2 };
        ci.NumberFormat.NumberGroupSeparator = ",";
        ci.NumberFormat.NumberDecimalSeparator = ".";
        return ci;
    }

    /// <summary>Formats a decimal as "1,05,000.00"; empty string for exactly zero.</summary>
    public static string Amount(decimal value)
        => value == 0m ? string.Empty : value.ToString("#,##0.00", Indian);

    /// <summary>Formats <see cref="Money"/> with Indian grouping; empty for zero.</summary>
    public static string Amount(Money money) => Amount(money.Amount);

    /// <summary>Always renders a value (even zero) with Indian grouping — for totals rows.</summary>
    public static string AmountAlways(decimal value) => value.ToString("#,##0.00", Indian);

    /// <summary>Always renders a <see cref="Money"/> value (even zero).</summary>
    public static string AmountAlways(Money money) => AmountAlways(money.Amount);

    /// <summary>
    /// Formats a stock quantity with Indian grouping and up to six decimals (trailing zeros trimmed),
    /// e.g. 1050 → "1,050", 12.5 → "12.5". Exactly zero renders "0" (quantities are meaningful at zero,
    /// unlike money cells which blank out). Used by the inventory report grids.
    /// </summary>
    public static string Quantity(decimal value) => value.ToString("#,##0.######", Indian);

    /// <summary>
    /// Formats a value as WHOLE rupees "1,05,000" (rounded half-up, no paisa); empty string for exactly zero.
    /// The statutory TDS/TCS returns and their exception/outstanding reports are stated in whole rupees, so the
    /// Phase-7 slice-8 report grids render every amount (and the nearest-rupee interest) this way.
    /// </summary>
    public static string Rupees(decimal value)
        => value == 0m ? string.Empty : Math.Round(value, MidpointRounding.AwayFromZero).ToString("#,##0", Indian);

    /// <summary>Whole-rupee format of a <see cref="Money"/>; empty for zero.</summary>
    public static string Rupees(Money money) => Rupees(money.Amount);

    /// <summary>Always renders a whole-rupee value (even zero) — for the statutory report grand-total rows.</summary>
    public static string RupeesAlways(decimal value)
        => Math.Round(value, MidpointRounding.AwayFromZero).ToString("#,##0", Indian);

    /// <summary>Always renders a whole-rupee <see cref="Money"/> value (even zero).</summary>
    public static string RupeesAlways(Money money) => RupeesAlways(money.Amount);

    /// <summary>
    /// The round-half-up whole-rupee value each report cell displays (the numeric behind <see cref="Rupees(decimal)"/>).
    /// Exposed so a grand-total row can FOOT to the displayed (per-row rounded) amounts — summing these instead of
    /// rounding the paisa-exact Σ, which can differ by ₹1 when rows carry paisa (e.g. a GST-inclusive TCS base).
    /// </summary>
    public static decimal WholeRupees(decimal value) => Math.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>The round-half-up whole-rupee value of a <see cref="Money"/> (see <see cref="WholeRupees(decimal)"/>).</summary>
    public static decimal WholeRupees(Money money) => WholeRupees(money.Amount);
}
