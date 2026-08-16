namespace Apex.Ledger.Domain;

/// <summary>
/// <b>The one six-digit Indian PIN-code rule</b>, shared by every postal block the product persists or prints.
/// A valid PIN is exactly six ASCII digits whose first digit is 1–9 (India Post allots the leading digit 1–8;
/// 9 is the APO/FPO series), so a leading zero is rejected.
/// <para><b>Why this type exists.</b> The rule lived only inside <see cref="PartyMailingDetails.EnsureValid"/>,
/// so the <b>recipient</b> PIN on a tax invoice was validated and the <b>supplier</b> PIN was not —
/// <c>Company.Pin</c> had no validation call site anywhere in <c>src/</c>. That asymmetry stopped being
/// cosmetic when the supplier block began printing the PIN (W0-2a): a canonical import carrying
/// <c>pin="abcdef"</c> would print <c>PIN: abcdef</c> on a statutory document. Both sides now call this.</para>
/// </summary>
public static class IndianPinCode
{
    /// <summary>True when <paramref name="pin"/> is blank (unset is not invalid) or a well-formed Indian PIN.</summary>
    public static bool IsValidOrBlank(string? pin)
    {
        if (string.IsNullOrWhiteSpace(pin)) return true;

        var p = pin.Trim();
        if (p.Length != 6) return false;
        if (p[0] < '1' || p[0] > '9') return false;

        foreach (var ch in p)
            if (!char.IsAsciiDigit(ch)) return false;

        return true;
    }
}
