using System.Globalization;
using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The pure projection of a voucher's <b>human "Voucher No."</b> from its persisted <c>int</c> sequence number
/// (numbering-design-v2 §2.1, §9 S1). The stored <c>int</c> stays the identity/sequence seed; the displayed number
/// is <c>Prefix ++ leftPad(number, Width) ++ Suffix</c>, where the Prefix/Suffix are the date-selected affix rows on
/// the <see cref="VoucherType"/>. With an empty config (no affixes, <see cref="VoucherType.NumberWidth"/> 0) the
/// result is byte-identical to <c>number.ToString()</c> (the ER-13 render guard), so wiring it to a render site in a
/// later slice changes nothing for an unconfigured type. Deterministic, allocation-cheap, culture-invariant.
/// </summary>
public static class VoucherNumberFormatter
{
    /// <summary>
    /// Renders the human voucher number for <paramref name="number"/> on <paramref name="date"/> under
    /// <paramref name="type"/>'s numbering config. Returns <c>""</c> when numbering is <see cref="NumberingMethod.None"/>
    /// or <paramref name="number"/> ≤ 0 (matches today's empty output for an unnumbered voucher). The pad NEVER
    /// truncates a number wider than <see cref="VoucherType.NumberWidth"/>.
    /// </summary>
    public static string Render(VoucherType type, int number, DateOnly date)
    {
        if (type.Numbering == NumberingMethod.None || number <= 0)
            return "";

        var prefix = SelectAffix(type.Prefixes, date);
        var suffix = SelectAffix(type.Suffixes, date);

        var digits = number.ToString(CultureInfo.InvariantCulture);
        var core = type.NumberWidth > 0
            ? digits.PadLeft(type.NumberWidth, type.PrefillWithZero ? '0' : ' ')
            : digits;

        return prefix + core + suffix;
    }

    // The Particulars of the affix row with the greatest ApplicableFrom that is on/before the voucher date; on a tie
    // the greatest Id wins (a total order, so selection is deterministic regardless of storage order). "" if no row
    // is in force.
    private static string SelectAffix(IReadOnlyList<VoucherNumberAffix> rows, DateOnly date)
    {
        var picked = rows
            .Where(r => r.ApplicableFrom <= date)
            .OrderBy(r => r.ApplicableFrom)
            .ThenBy(r => r.Id)
            .LastOrDefault();
        return picked?.Particulars ?? "";
    }
}
