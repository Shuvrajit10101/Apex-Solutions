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
}
