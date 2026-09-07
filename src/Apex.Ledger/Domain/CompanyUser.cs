using System;

namespace Apex.Ledger.Domain;

/// <summary>
/// One row of the vendor's <b>"Users for Company"</b> screen (census 16.2): a user name, the
/// <see cref="SecurityLevel"/> they hold, and the <b>one-way verifier</b> for their password.
///
/// <para>🔴 <b><see cref="StoredPasswordVerifier"/> IS NOT A PASSWORD AND CANNOT BECOME ONE.</b> It holds the
/// storage string of a <see cref="Security.PasswordHash"/> —
/// <c>PBKDF2-SHA256$&lt;iterations&gt;$&lt;salt&gt;$&lt;hash&gt;</c> — and there is no member on this type, in the
/// persistence adapter, or anywhere in the application that returns a password. Set it only through
/// <see cref="SetPassword"/>; read it only through <see cref="VerifyPassword"/>. <b>A lost password cannot be
/// recovered</b>, only replaced by someone holding an Owner level — and if the last Owner's password is lost the
/// company is locked irrecoverably. See <see cref="Security.PasswordHash"/> for the vendor citation on that
/// consequence and for the full storage specification.</para>
///
/// <para>⚠️ <b>A <c>null</c> verifier means "no password set", which is a real state</b> the reference product
/// permits, and it VERIFIES ONLY THE EMPTY STRING — never "anything matches". <see cref="HasPassword"/> says
/// which state a row is in without revealing anything about the password.</para>
///
/// <para>🔴 <b>This type has NO canonical-export member and must never gain one</b> — the canonical JSON/XML
/// backup deliberately carries no user row and no password field, the same structural absence the NIC credential
/// columns have (ER-16). <c>CanonicalUserAbsenceTests</c> is the guard.</para>
/// </summary>
public sealed class CompanyUser
{
    /// <summary>Stable identity; what the <c>company_users</c> row is keyed by (schema v56).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The user name shown on the Users for Company screen. Matched case-insensitively when signing in;
    /// uniqueness within a company is enforced by <see cref="SecurityControl.AddUser"/>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The <see cref="SecurityLevel.Id"/> this user holds. Exactly one — the vendor's screen assigns one
    /// role per user row.</summary>
    public Guid SecurityLevelId { get; set; }

    /// <summary>
    /// 🔴 The one-way PBKDF2-HMAC-SHA256 verifier, or <c>null</c> when no password is set. Never a password,
    /// never reversible, never logged. See the type remarks and <see cref="Security.PasswordHash"/>.
    /// </summary>
    public string? StoredPasswordVerifier { get; private set; }

    /// <summary>When the current password was set (UTC), or <c>null</c> when none is set. Feeds
    /// <see cref="PasswordPolicy.IsExpired"/>; carries nothing about the password itself.</summary>
    public DateTimeOffset? PasswordSetOn { get; private set; }

    /// <summary>Whether this user may sign in. A disabled user keeps their row (and so keeps being a countable
    /// Owner for the last-Owner safeguard) but is refused at the door.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>True when a password has been set on this user. Reveals nothing about it — not its length, not
    /// its strength, not when it was chosen beyond <see cref="PasswordSetOn"/>.</summary>
    public bool HasPassword => StoredPasswordVerifier is not null;

    public CompanyUser() { }

    public CompanyUser(string name, Guid securityLevelId)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        SecurityLevelId = securityLevelId;
    }

    /// <summary>
    /// Sets (or replaces) this user's password. The plaintext is hashed immediately and <b>is not retained by this
    /// object in any form</b>. <paramref name="setOn"/> is a parameter rather than a clock read so the domain stays
    /// deterministic and testable.
    /// </summary>
    /// <param name="password">The new password. Empty is permitted (the reference product allows a user with no
    /// password); <c>null</c> is not — use <see cref="ClearPassword"/> for "no password set".</param>
    /// <param name="setOn">The moment to record as when it was set.</param>
    /// <param name="iterations">The PBKDF2 work factor; defaults to
    /// <see cref="Security.PasswordHash.DefaultIterations"/>. Tests lower it to stay fast.</param>
    public void SetPassword(
        string password, DateTimeOffset setOn, int iterations = Security.PasswordHash.DefaultIterations)
    {
        ArgumentNullException.ThrowIfNull(password);
        StoredPasswordVerifier = Security.PasswordHash.Derive(password, iterations).ToStorageString();
        PasswordSetOn = setOn;
    }

    /// <summary>Removes the password entirely, returning the user to the "no password set" state.</summary>
    public void ClearPassword()
    {
        StoredPasswordVerifier = null;
        PasswordSetOn = null;
    }

    /// <summary>
    /// 🔴 Restores a verifier read back from storage. <b>Refuses anything that is not a well-formed verifier</b> —
    /// a corrupted row must not silently become a user nothing can sign in as, nor one anything can.
    /// </summary>
    /// <exception cref="FormatException"><paramref name="storedVerifier"/> is neither <c>null</c> nor a
    /// well-formed <see cref="Security.PasswordHash"/> storage string.</exception>
    public void RestorePasswordVerifier(string? storedVerifier, DateTimeOffset? setOn)
    {
        if (storedVerifier is null)
        {
            ClearPassword();
            return;
        }

        if (!Security.PasswordHash.TryParse(storedVerifier, out _))
            throw new FormatException(
                "Stored password verifier for user '" + Name + "' is not a well-formed "
                + Security.PasswordHash.AlgorithmLabel + " record.");

        StoredPasswordVerifier = storedVerifier;
        PasswordSetOn = setOn;
    }

    /// <summary>
    /// Constant-time check of <paramref name="candidate"/> against this user's verifier. A user with NO password
    /// set matches the empty string and nothing else — never "everything".
    /// </summary>
    public bool VerifyPassword(string? candidate) =>
        StoredPasswordVerifier is null
            ? candidate is { Length: 0 }
            : Security.PasswordHash.Verify(StoredPasswordVerifier, candidate);
}
