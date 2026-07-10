namespace Apex.Ledger.Domain;

/// <summary>
/// TAN (Tax Deduction and Collection Account Number) structural validation (Phase 7 slice 1; mirrors
/// <see cref="Gstin"/>/<see cref="Pan"/>). A TAN is <b>10 characters</b> = <c>[4 letters][5 digits][1 letter]</c>
/// (e.g. <c>MUMA12345B</c>): the first three letters are a jurisdiction/city code, the fourth is the first letter
/// of the deductor's name, then a 5-digit unique number and a trailing alphabetic check letter. Pure,
/// allocation-light, fail-fast — an invalid TAN is rejected at the master-save boundary and never persisted.
/// </summary>
/// <remarks>Structural only (no checksum digit is publicly defined for TAN). Framework- and DB-agnostic.</remarks>
public static class Tan
{
    /// <summary>The exact TAN length.</summary>
    public const int Length = 10;

    /// <summary>True iff <paramref name="tan"/> is a structurally valid TAN (4 letters + 5 digits + 1 letter).</summary>
    public static bool IsValid(string? tan)
    {
        if (tan is null || tan.Length != Length) return false;
        for (var i = 0; i <= 3; i++) if (!IsUpperLetter(tan[i])) return false; // 4 letters
        for (var i = 4; i <= 8; i++) if (!IsDigit(tan[i])) return false;       // 5 digits
        return IsUpperLetter(tan[9]);                                          // 1 letter
    }

    /// <summary>Validates a TAN, throwing <see cref="ArgumentException"/> on any structural failure.</summary>
    public static void Validate(string? tan)
    {
        if (!IsValid(tan))
            throw new ArgumentException(
                $"'{tan}' is not a valid TAN (expected 10 chars = 4 letters + 5 digits + 1 letter, e.g. MUMA12345B).",
                nameof(tan));
    }

    /// <summary>Upper-cases and trims a user-entered TAN to its canonical form for validation/storage.</summary>
    public static string Normalize(string tan) => (tan ?? string.Empty).Trim().ToUpperInvariant();

    private static bool IsUpperLetter(char c) => c is >= 'A' and <= 'Z';
    private static bool IsDigit(char c) => c is >= '0' and <= '9';
}
