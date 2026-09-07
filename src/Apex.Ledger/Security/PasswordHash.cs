using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Apex.Ledger.Security;

/// <summary>
/// 🔴 <b>THE ONE-WAY PASSWORD VERIFIER (census 16.2 Security Control, slice S1).</b> A salted
/// <b>PBKDF2-HMAC-SHA256</b> derivation of a login password, plus the constant-time check that verifies a
/// candidate against it. <b>Nothing here can turn a stored value back into a password</b>, and that is the whole
/// point of the type: the reference product does not recover a forgotten password and neither do we.
///
/// <para><b>🔴 R13 — EXACTLY HOW A PASSWORD IS STORED, stated here because the report and the reviewer both have
/// to be able to check it against the code:</b>
/// <list type="bullet">
/// <item><b>Algorithm</b> — PBKDF2 with HMAC-SHA256 (<see cref="Rfc2898DeriveBytes.Pbkdf2(byte[],byte[],int,HashAlgorithmName,int)"/>,
/// in-BCL, no package, identical on Windows / Linux / macOS).</item>
/// <item><b>Salt</b> — <see cref="SaltBytes"/> = 16 bytes (128 bit) from
/// <see cref="RandomNumberGenerator.GetBytes(int)"/>, generated <b>per password</b>. Never shared between users,
/// never derived from the user name, never a constant.</item>
/// <item><b>Work factor</b> — <see cref="DefaultIterations"/> = <b>600,000</b> iterations, and the number
/// actually used is stored <b>alongside</b> the hash so it can be raised later without invalidating a single
/// existing row (an old row simply verifies at its own recorded count).</item>
/// <item><b>Derived key</b> — <see cref="KeyBytes"/> = 32 bytes (256 bit).</item>
/// <item><b>Stored form</b> — ONE text field,
/// <c>PBKDF2-SHA256$&lt;iterations&gt;$&lt;salt-base64&gt;$&lt;hash-base64&gt;</c>. <b>Nothing else about the
/// password is persisted</b>: no length, no hint, no reversible copy, no plaintext in any log, message or
/// exception. <see cref="ToString"/> deliberately returns the same storage string (it contains no secret), and
/// no member of this type ever returns or accepts a decrypted password.</item>
/// <item><b>Verification</b> — re-derive with the STORED salt and the STORED iteration count, then compare with
/// <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte},ReadOnlySpan{byte})"/>. Never <c>==</c>,
/// never <c>SequenceEqual</c>, never a string comparison — those leak the length of the matching prefix through
/// timing.</item>
/// <item><b>No pepper.</b> A pepper committed to this repository would be a secret in the repository, which R13
/// forbids outright. If one is ever wanted it must come from the environment and its ABSENCE must be a hard
/// refusal to start, never a silent fallback to a constant.</item>
/// </list></para>
///
/// <para>🔴 <b>DO NOT COPY <c>SqliteNicCredentialStore</c> FOR THIS.</b> That store AES-encrypts a portal
/// credential under a hard-coded application pepper — its own comment calls it "obfuscation-grade placeholder
/// protection". That is <i>reversible encryption of a recoverable secret</i>, which is right for a credential the
/// application must later replay to a third party and <b>categorically wrong for a login password</b>. It is the
/// nearest crypto in this tree and it is the wrong pattern; this type exists so nobody reaches for it.</para>
///
/// <para>🔴 <b>THE PRODUCT CONSEQUENCE, SAID PLAINLY: A LOST OWNER PASSWORD LOCKS THE COMPANY IRRECOVERABLY.</b>
/// There is no reset path, no master key and no back door, because any of the three would be exactly the
/// recoverable storage this type refuses. The reference product behaves the same way and says so — vendor,
/// help.tallysolutions.com/tally-prime/access-control-data-security/data-security-faq/: if both the Admin and the
/// user password are forgotten the only remedy is to <i>"restore earlier backup in which you have not mentioned
/// the password"</i>. <see cref="Domain.SecurityControl"/> keeps at least one Owner in existence, and the Users
/// screen states the consequence, but neither can undo a forgotten password.</para>
///
/// <para><b>Cross-platform and culture-proof by construction.</b> Base64 and the invariant-culture integer format
/// are the only text produced; parsing uses <see cref="NumberStyles.None"/> with
/// <see cref="CultureInfo.InvariantCulture"/>, so a book written under <c>ar-SA</c> digits or a Turkish
/// <c>tr-TR</c> casing rule reads back byte-identically anywhere. The password itself is measured as UTF-8.</para>
/// </summary>
public sealed class PasswordHash
{
    /// <summary>The algorithm tag that opens every stored string. Present so a future scheme can be added without
    /// ambiguity — a stored value whose tag is not this one is REFUSED, never guessed at.</summary>
    public const string AlgorithmLabel = "PBKDF2-SHA256";

    /// <summary>The field separator inside the stored string. <c>$</c> cannot occur in Base64 or in an invariant
    /// decimal integer, so the format is unambiguous.</summary>
    public const char FieldSeparator = '$';

    /// <summary>The work factor a NEW password is derived at: <b>600,000</b> PBKDF2-HMAC-SHA256 iterations.
    /// Raising this constant is safe at any time — every existing row carries the count it was derived at.</summary>
    public const int DefaultIterations = 600_000;

    /// <summary>The per-password salt length in bytes: 16 (128 bit).</summary>
    public const int SaltBytes = 16;

    /// <summary>The derived-key length in bytes: 32 (256 bit).</summary>
    public const int KeyBytes = 32;

    /// <summary>The smallest work factor this type will accept when parsing or deriving. A stored row claiming
    /// fewer is refused rather than verified weakly — a tampered iteration count is the cheapest way to turn a
    /// strong hash into a fast one.</summary>
    public const int MinimumIterations = 100_000;

    private readonly byte[] _salt;
    private readonly byte[] _key;

    /// <summary>The PBKDF2 iteration count THIS hash was derived at (not necessarily <see cref="DefaultIterations"/>
    /// — an older row keeps its own).</summary>
    public int Iterations { get; }

    private PasswordHash(int iterations, byte[] salt, byte[] key)
    {
        Iterations = iterations;
        _salt = salt;
        _key = key;
    }

    /// <summary>
    /// Derives a new hash for <paramref name="password"/> with a freshly generated random salt. Two calls with the
    /// SAME password produce DIFFERENT stored strings — that is the salt doing its job, and a test asserts it.
    /// </summary>
    /// <param name="password">The password to hash. May be empty (the reference product permits a user with no
    /// password); may not be <c>null</c>. It is measured as UTF-8 and is never stored, logged or echoed.</param>
    /// <param name="iterations">The work factor; defaults to <see cref="DefaultIterations"/>. Tests pass a smaller
    /// one to stay fast — production callers must not.</param>
    public static PasswordHash Derive(string password, int iterations = DefaultIterations)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (iterations < MinimumIterations)
            throw new ArgumentOutOfRangeException(
                nameof(iterations),
                $"A password work factor below {MinimumIterations} iterations is refused.");

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, KeyBytes);
        return new PasswordHash(iterations, salt, key);
    }

    /// <summary>
    /// True when <paramref name="password"/> is the password this hash was derived from. Re-derives with the
    /// stored salt and stored iteration count and compares in CONSTANT TIME. A <c>null</c> candidate is false —
    /// never an exception, because the caller is a login path and an exception there is an oracle.
    /// </summary>
    public bool Verify(string? password)
    {
        if (password is null) return false;

        var candidate = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), _salt, Iterations, HashAlgorithmName.SHA256, KeyBytes);

        // 🔴 FixedTimeEquals, never == / SequenceEqual / string compare. A short-circuiting comparison leaks the
        // length of the matching prefix through timing, which turns an offline-strength hash into an online guess.
        return CryptographicOperations.FixedTimeEquals(candidate, _key);
    }

    /// <summary>
    /// The single TEXT value that goes in the database:
    /// <c>PBKDF2-SHA256$&lt;iterations&gt;$&lt;salt-base64&gt;$&lt;hash-base64&gt;</c>. It carries no plaintext
    /// and no reversible form; the iteration count travels with the hash so the work factor can be raised later.
    /// </summary>
    public string ToStorageString() =>
        string.Concat(
            AlgorithmLabel, FieldSeparator,
            Iterations.ToString(CultureInfo.InvariantCulture), FieldSeparator,
            Convert.ToBase64String(_salt), FieldSeparator,
            Convert.ToBase64String(_key));

    /// <summary>Same as <see cref="ToStorageString"/> — the stored form is not a secret, so there is no risk in a
    /// debugger or a log showing it, and no second representation to drift.</summary>
    public override string ToString() => ToStorageString();

    /// <summary>
    /// Reads a stored string back. <b>Refuses anything malformed rather than accepting it silently</b> — a row
    /// that cannot be parsed must not become a hash that no password matches OR one that every password matches.
    /// </summary>
    /// <exception cref="FormatException">The value is not a well-formed <see cref="AlgorithmLabel"/> record.</exception>
    public static PasswordHash Parse(string stored) =>
        TryParse(stored, out var hash)
            ? hash!
            : throw new FormatException(
                "Stored password verifier is not a well-formed " + AlgorithmLabel + " record.");

    /// <summary>
    /// Non-throwing <see cref="Parse"/>. Returns false — with <paramref name="hash"/> <c>null</c> — for a null,
    /// blank, wrong-tag, wrong-arity, non-Base64, wrong-length or under-worked value. The failure message
    /// deliberately never quotes the offending value: a malformed verifier is still credential-adjacent data.
    /// </summary>
    public static bool TryParse(string? stored, out PasswordHash? hash)
    {
        hash = null;
        if (string.IsNullOrWhiteSpace(stored)) return false;

        var parts = stored.Split(FieldSeparator);
        if (parts.Length != 4) return false;
        if (!string.Equals(parts[0], AlgorithmLabel, StringComparison.Ordinal)) return false;

        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations))
            return false;
        if (iterations < MinimumIterations) return false;

        byte[] salt, key;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            key = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length != SaltBytes || key.Length != KeyBytes) return false;

        hash = new PasswordHash(iterations, salt, key);
        return true;
    }

    /// <summary>
    /// Convenience for a login path: true when <paramref name="candidate"/> verifies against the stored string
    /// <paramref name="stored"/>. A <c>null</c>/blank/malformed stored value verifies NOTHING — it is not "no
    /// password set, so let them in", which would be the worst possible reading of a corrupted row.
    /// </summary>
    public static bool Verify(string? stored, string? candidate) =>
        TryParse(stored, out var hash) && hash!.Verify(candidate);
}
