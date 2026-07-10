namespace Apex.Ledger.Domain;

/// <summary>
/// PAN (Permanent Account Number) structural validation (Phase 7 slice 1; mirrors <see cref="Gstin"/>/
/// <see cref="Tan"/>). A PAN is <b>10 characters</b> = <c>[5 letters][4 digits][1 letter]</c> (e.g.
/// <c>AAPFU0939F</c>): the fourth letter encodes the holder type (P=Individual, C=Company, H=HUF, F=Firm, …) and
/// the fifth is the first letter of the surname/name. Pure, allocation-light, fail-fast — an invalid PAN is
/// rejected at the master-save boundary and never persisted. A missing PAN (no-PAN deductee) is handled at
/// compute time (§206AA 20% / §206CC 2×/5%), not here.
/// </summary>
/// <remarks>Structural only (no publicly-defined checksum). Framework- and DB-agnostic.</remarks>
public static class Pan
{
    /// <summary>The exact PAN length.</summary>
    public const int Length = 10;

    /// <summary>True iff <paramref name="pan"/> is a structurally valid PAN (5 letters + 4 digits + 1 letter).</summary>
    public static bool IsValid(string? pan)
    {
        if (pan is null || pan.Length != Length) return false;
        for (var i = 0; i <= 4; i++) if (!IsUpperLetter(pan[i])) return false; // 5 letters
        for (var i = 5; i <= 8; i++) if (!IsDigit(pan[i])) return false;       // 4 digits
        return IsUpperLetter(pan[9]);                                          // 1 letter
    }

    /// <summary>Validates a PAN, throwing <see cref="ArgumentException"/> on any structural failure.</summary>
    public static void Validate(string? pan)
    {
        if (!IsValid(pan))
            throw new ArgumentException(
                $"'{pan}' is not a valid PAN (expected 10 chars = 5 letters + 4 digits + 1 letter, e.g. AAPFU0939F).",
                nameof(pan));
    }

    /// <summary>Upper-cases and trims a user-entered PAN to its canonical form for validation/storage.</summary>
    public static string Normalize(string pan) => (pan ?? string.Empty).Trim().ToUpperInvariant();

    private static bool IsUpperLetter(char c) => c is >= 'A' and <= 'Z';
    private static bool IsDigit(char c) => c is >= '0' and <= '9';
}
