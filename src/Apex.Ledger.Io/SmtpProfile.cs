namespace Apex.Ledger.Io;

/// <summary>
/// A capture-only SMTP server profile (RQ-27) for a <i>later</i> phase to wire live transport. It records
/// only the connection identity: host, port, whether TLS/SSL is used, and the sender identity
/// (from-address + optional from-name).
///
/// <para><b>Security (R13):</b> there is deliberately NO password field. A credential — if ever needed — lives
/// in the OS secret store / environment, never in the repo, the value object, or the committed DB. Live SMTP
/// send is DEFERRED: nothing consumes this profile to open a socket yet.</para>
/// </summary>
public sealed record SmtpProfile
{
    public string Host { get; init; } = string.Empty;

    /// <summary>The submission port (typically 587 for STARTTLS, 465 for implicit TLS, 25 plain).</summary>
    public int Port { get; init; } = 587;

    /// <summary>Whether the connection uses TLS/SSL.</summary>
    public bool UseTls { get; init; } = true;

    /// <summary>The envelope/from address that outgoing mail is sent as.</summary>
    public string FromAddress { get; init; } = string.Empty;

    /// <summary>An optional display name paired with <see cref="FromAddress"/>.</summary>
    public string? FromName { get; init; }

    /// <summary>True when the profile has the minimum needed to attempt a (future) send: a host and a from.</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Host) &&
        Port is > 0 and <= 65535 &&
        !string.IsNullOrWhiteSpace(FromAddress);
}
