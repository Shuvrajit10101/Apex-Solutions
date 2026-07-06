using System.Globalization;
using System.Text;

namespace Apex.Ledger.Io;

/// <summary>
/// Converts an exact decimal rupee amount to its English words using the <b>Indian numbering system</b>
/// (units / tens / hundreds / thousand / lakh / crore), paisa-accurate — the "amount in words" line that a
/// GST tax invoice (Rule 46) and most vouchers must print. Pure, deterministic and culture-invariant: no
/// clock, no RNG, no culture-sensitive formatting; the same amount always yields the same string.
///
/// <para>Format: <c>"Rupees One Lakh Twenty Three Thousand Four Hundred Fifty and Sixty Paise Only"</c>.
/// A whole-rupee amount omits the paise clause: <c>"Rupees Five Only"</c>. A sub-rupee amount omits the
/// rupee clause: <c>"Sixty Paise Only"</c> (still terminated by "Only"). Zero renders
/// <c>"Rupees Zero Only"</c>. A negative amount is prefixed <c>"Minus "</c> on its magnitude.</para>
///
/// <para>The paise is the amount's fractional part snapped to 2 places (away-from-zero); the integer rupee
/// part is grouped in the Indian pattern (the rightmost group is 3 digits, every group left of it is 2
/// digits) so it names crore / lakh / thousand / hundred correctly for any magnitude.</para>
/// </summary>
public static class IndianAmountInWords
{
    private static readonly string[] Ones =
    {
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine",
        "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen",
        "Eighteen", "Nineteen",
    };

    private static readonly string[] Tens =
    {
        "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety",
    };

    /// <summary>
    /// The words for an amount, using the fixed currency words "Rupees" / "Paise" (catalog §17 invoice print;
    /// GST Rule 46). See the class remarks for the exact format.
    /// </summary>
    public static string Convert(decimal amount) => Convert(amount, "Rupees", "Paise");

    /// <summary>
    /// The words for <paramref name="amount"/> naming the whole part with <paramref name="majorUnit"/> (e.g.
    /// "Rupees") and the two-place fractional part with <paramref name="minorUnit"/> (e.g. "Paise"). Always
    /// terminated with "Only". Negative amounts are prefixed "Minus ".
    /// </summary>
    public static string Convert(decimal amount, string majorUnit, string minorUnit)
    {
        var sb = new StringBuilder();

        if (amount < 0m)
        {
            sb.Append("Minus ");
            amount = -amount;
        }

        // Snap to the paisa (2 dp, away-from-zero) so the fractional part is an exact 0..99 paise count.
        amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        long rupees = (long)Math.Truncate(amount);
        int paise = (int)Math.Round((amount - rupees) * 100m, 0, MidpointRounding.AwayFromZero);

        bool hasRupees = rupees != 0;
        bool hasPaise = paise != 0;

        if (hasRupees || !hasPaise)
        {
            // Whole-rupee clause (also emitted for a zero amount so it reads "Rupees Zero Only").
            sb.Append(majorUnit).Append(' ').Append(IntegerToWords(rupees));
        }

        if (hasPaise)
        {
            if (hasRupees || sb.Length > "Minus ".Length) // a rupee clause (or Minus prefix) precedes the paise
                sb.Append(" and ");
            sb.Append(TwoDigitToWords(paise)).Append(' ').Append(minorUnit);
        }

        sb.Append(" Only");
        return sb.ToString();
    }

    /// <summary>
    /// A non-negative integer to Indian-system words (Crore / Lakh / Thousand / Hundred). 0 ⇒ "Zero".
    /// Splits the number into the Indian groups: the lowest 3 digits (hundreds), then successive 2-digit
    /// groups for thousand, lakh, crore, and beyond (arab / kharab named generically as higher crores).
    /// </summary>
    internal static string IntegerToWords(long n)
    {
        if (n == 0) return Ones[0];

        var parts = new List<string>();

        // Crore and above: everything past 7 digits is expressed in crores (Indian convention groups by 2
        // beyond a crore — lakh-crore etc. — but "N Crore" is the faithful, unambiguous rendering).
        long crore = n / 10_000_000L;
        long remainder = n % 10_000_000L;

        long lakh = remainder / 100_000L;
        remainder %= 100_000L;

        long thousand = remainder / 1_000L;
        remainder %= 1_000L;

        long hundred = remainder / 100L;
        long belowHundred = remainder % 100L;

        if (crore > 0) parts.Add(IntegerToWords(crore) + " Crore");
        if (lakh > 0) parts.Add(TwoDigitToWords((int)lakh) + " Lakh");
        if (thousand > 0) parts.Add(TwoDigitToWords((int)thousand) + " Thousand");
        if (hundred > 0) parts.Add(Ones[hundred] + " Hundred");
        if (belowHundred > 0) parts.Add(TwoDigitToWords((int)belowHundred));

        return string.Join(' ', parts);
    }

    /// <summary>Words for a value 0..99 (no "Zero" — callers only pass a non-zero group).</summary>
    private static string TwoDigitToWords(int n)
    {
        if (n < 20) return Ones[n];
        int tens = n / 10;
        int ones = n % 10;
        return ones == 0 ? Tens[tens] : Tens[tens] + " " + Ones[ones];
    }
}
