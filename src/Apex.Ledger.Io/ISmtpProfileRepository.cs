namespace Apex.Ledger.Io;

/// <summary>
/// Read/write access to a company's <b>SMTP server profile</b> (RQ-27). The profile is capture-only —
/// host / port / TLS / from-address / from-name — for a <i>later</i> phase to wire live transport. It is
/// stored as a singleton per company (at most one row); <see cref="Save"/> upserts it and <see cref="Get"/>
/// returns it, or <c>null</c> when the company has never saved one.
///
/// <para><b>Security (R13):</b> no password/secret is ever part of this port or the row it persists. A
/// credential — if ever needed — lives in the OS secret store / environment, never in the DB. Live SMTP send
/// is DEFERRED: nothing consumes a persisted profile to open a socket yet.</para>
/// </summary>
public interface ISmtpProfileRepository
{
    /// <summary>Upserts <paramref name="profile"/> for <paramref name="companyId"/> (one profile per company):
    /// creates it, or overwrites the existing profile.</summary>
    void Save(Guid companyId, SmtpProfile profile);

    /// <summary>Gets the company's SMTP profile, or <c>null</c> when none has been saved.</summary>
    SmtpProfile? Get(Guid companyId);

    /// <summary>Deletes the company's SMTP profile. No-op when absent.</summary>
    void Delete(Guid companyId);
}
