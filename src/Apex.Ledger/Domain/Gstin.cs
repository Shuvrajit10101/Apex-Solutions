namespace Apex.Ledger.Domain;

/// <summary>
/// GSTIN structural + checksum validation (catalog §12; phase4 RQ-3/ER-6; law source L-6). A GSTIN is
/// <b>15 characters</b> = <c>[2-digit State code][10-char PAN][1 entity code][1 default char 'Z'][1 checksum]</c>,
/// where the leading two digits must be a valid GST state code and the 15th character satisfies the
/// <b>Luhn-mod-36</b> checksum over the first 14 characters. Pure, allocation-light, fail-fast — an invalid
/// GSTIN is rejected at the master-save boundary with a clean domain error and is never persisted.
/// </summary>
/// <remarks>
/// The alphabet for the mod-36 checksum is <c>0-9</c> then <c>A-Z</c> (value 0..35). The PAN embedded in
/// positions 3–12 is validated structurally (5 letters + 4 digits + 1 letter). Framework- and DB-agnostic.
/// </remarks>
public static class Gstin
{
    /// <summary>The mod-36 alphabet: digit value = index (0..35). Position n of the string is char value n.</summary>
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>The exact GSTIN length (law L-6).</summary>
    public const int Length = 15;

    /// <summary>
    /// True iff <paramref name="gstin"/> is a structurally valid GSTIN with a correct Luhn-mod-36 checksum
    /// and a recognised leading state code. Case-sensitive on the canonical upper-case form; the caller
    /// should upper-case/trim before validating a user entry (see <see cref="Normalize"/>).
    /// </summary>
    public static bool IsValid(string? gstin)
    {
        if (gstin is null || gstin.Length != Length) return false;

        // Positions (0-based): [0-1] state code, [2-11] PAN, [12] entity code, [13] 'Z', [14] checksum.
        if (!IndianState.IsValidCode(gstin.Substring(0, 2))) return false;

        // PAN structure at [2..11]: 5 letters, 4 digits, 1 letter.
        for (var i = 2; i <= 6; i++) if (!IsUpperLetter(gstin[i])) return false;   // 5 letters
        for (var i = 7; i <= 10; i++) if (!IsDigit(gstin[i])) return false;        // 4 digits
        if (!IsUpperLetter(gstin[11])) return false;                              // 1 letter

        // Entity code [12]: alphanumeric (1-9, A-Z typically); the 14th char [13] is the default 'Z'.
        if (!IsAlphaNum(gstin[12])) return false;
        if (gstin[13] != 'Z') return false;

        // Every character must be in the mod-36 alphabet, and the 15th must be the computed check digit.
        for (var i = 0; i < Length; i++)
            if (Alphabet.IndexOf(gstin[i]) < 0) return false;

        return gstin[14] == ComputeCheckDigit(gstin);
    }

    /// <summary>Validates a GSTIN, throwing <see cref="ArgumentException"/> on any structural/checksum failure.</summary>
    public static void Validate(string? gstin)
    {
        if (!IsValid(gstin))
            throw new ArgumentException(
                $"'{gstin}' is not a valid GSTIN (expected 15 chars = state code + PAN + entity + 'Z' + Luhn-mod-36 checksum).",
                nameof(gstin));
    }

    /// <summary>Upper-cases and trims a user-entered GSTIN to its canonical form for validation/storage.</summary>
    public static string Normalize(string gstin) => (gstin ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>
    /// The Luhn-mod-36 check digit for the first 14 characters of <paramref name="gstin"/> (the 15th is the
    /// check digit itself and is ignored). Algorithm: for each of the 14 chars, take its alphabet value,
    /// multiply alternate positions by a factor that toggles 1,2,1,2,… (from the left), add
    /// <c>quotient + remainder</c> of (product ÷ 36) to a running sum; the check value is
    /// <c>(36 − (sum mod 36)) mod 36</c>, mapped back to the alphabet.
    /// </summary>
    public static char ComputeCheckDigit(string gstin)
    {
        var factor = 2;
        var sum = 0;
        const int codePointChars = 36;

        // Walk the first 14 chars right-to-left; factor toggles 2,1,2,1,… so the char adjacent to the check
        // digit gets factor 2 (standard Luhn-mod-N from the check position leftward).
        for (var i = Length - 2; i >= 0; i--)
        {
            var codePoint = Alphabet.IndexOf(gstin[i]);
            if (codePoint < 0) throw new ArgumentException($"GSTIN contains a character outside the mod-36 alphabet: '{gstin[i]}'.");
            var addend = factor * codePoint;
            factor = factor == 2 ? 1 : 2;
            addend = (addend / codePointChars) + (addend % codePointChars);
            sum += addend;
        }

        var checkCodePoint = (codePointChars - (sum % codePointChars)) % codePointChars;
        return Alphabet[checkCodePoint];
    }

    private static bool IsUpperLetter(char c) => c is >= 'A' and <= 'Z';
    private static bool IsDigit(char c) => c is >= '0' and <= '9';
    private static bool IsAlphaNum(char c) => IsUpperLetter(c) || IsDigit(c);
}
