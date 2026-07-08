namespace Apex.Ledger.Io;

/// <summary>
/// An email address with an optional display name, used in <c>From</c>/<c>To</c>/<c>Cc</c> headers.
/// The <see cref="Address"/> is expected to be an ASCII addr-spec (local@domain); a non-ASCII
/// <see cref="DisplayName"/> is RFC-2047 encoded by the composer.
/// </summary>
public sealed record EmlAddress(string Address, string? DisplayName = null);

/// <summary>
/// One attachment part: the file name shown to the recipient, its MIME type (e.g. <c>application/pdf</c>,
/// <c>text/csv</c>) and the exact bytes to carry (base64-encoded by the composer, decoding back to these
/// same bytes). For Apex this is the already-exported report/invoice PDF (or the chosen export format).
/// </summary>
public sealed record EmlAttachment(string FileName, string MimeType, byte[] Content);

/// <summary>
/// A framework-agnostic description of the email the UI wants to compose (RQ-25/26). The composer turns
/// this into a byte-stable RFC-5322 / MIME multipart-mixed <c>.eml</c>.
///
/// <para><b>No clock, no RNG (ER-8):</b> the UI passes in the <see cref="Date"/> header <i>value</i> (already
/// formatted per RFC 5322, e.g. <c>"Mon, 06 Jul 2026 12:00:00 +0530"</c>) and a deterministic
/// <see cref="MessageId"/> (e.g. derived from the report id + the supplied timestamp). The MIME boundary is
/// fixed inside the composer, so the same message re-composes byte-identical.</para>
///
/// <para>Live SMTP send is DEFERRED (RQ-26): this type carries no transport at all. The UI writes the bytes
/// to a <c>.eml</c> file and/or hands off to the OS mail client.</para>
/// </summary>
public sealed record EmlMessage
{
    public required EmlAddress From { get; init; }
    public IReadOnlyList<EmlAddress> To { get; init; } = System.Array.Empty<EmlAddress>();
    public IReadOnlyList<EmlAddress> Cc { get; init; } = System.Array.Empty<EmlAddress>();
    public string Subject { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;

    /// <summary>The RFC-5322 <c>Date</c> header value, supplied by the UI (the composer has no clock).</summary>
    public required string Date { get; init; }

    /// <summary>A deterministic <c>Message-ID</c> including the angle brackets, supplied by the UI (no RNG).</summary>
    public required string MessageId { get; init; }

    public IReadOnlyList<EmlAttachment> Attachments { get; init; } = System.Array.Empty<EmlAttachment>();
}
