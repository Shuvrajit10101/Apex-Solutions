using System.Text;

namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>only</b> channel through which a NIC-IRP API credential flows (Phase 9 slice 4a; RQ-30; ER-16). The optional
/// live e-invoicing path (<see cref="GstConnectorMode.CustomerNicDirect"/>) uses the customer's OWN NIC API creds; those
/// creds are stored <b>protected-at-rest</b> and are <b>structurally excluded from serialization</b>: they are never a
/// <see cref="GstConfig"/> property, never a DTO member, and never appear in the canonical JSON/XML export. This
/// abstraction keeps the pure canonical mapper unable to serialise a secret even by mistake — the mapper has no
/// reference to it. Plaintext is materialised ONLY at the point of an outbound HTTP call, from
/// <see cref="Get"/>.
/// <para>
/// The app NEVER holds a GSP/vendor credential, nor the customer's portal password/DSC (ER-16). The offline default
/// (<see cref="GstConnectorMode.OfflineJson"/>) needs <b>zero</b> credentials and touches no store — so
/// <see cref="HasCredentials"/> is false and nothing is ever written.
/// </para>
/// </summary>
public interface INicCredentialStore
{
    /// <summary>True iff a NIC credential is stored (protected) for the given GSTIN.</summary>
    bool HasCredentials(string gstin);

    /// <summary>Materialises the plaintext credential for the given GSTIN — called ONLY at the point of an outbound HTTP
    /// call. Throws when none is stored.</summary>
    NicApiCredentials Get(string gstin);

    /// <summary>Stores the credential for the given GSTIN, protected-at-rest (never plaintext, never exported).</summary>
    void Store(string gstin, NicApiCredentials creds);

    /// <summary>Clears any stored credential for the given GSTIN.</summary>
    void Clear(string gstin);
}

/// <summary>
/// A customer's NIC-IRP API credential set (Phase 9 slice 4a; ER-16) — the ONLY secret in the e-invoicing feature. It
/// is deliberately <b>not</b> part of the <see cref="Company"/> aggregate, any DTO, or the canonical model: it flows
/// only through <see cref="INicCredentialStore"/>. This is the customer's own NIC API credential (₹0 to NIC), never a
/// GSP/vendor credential and never the portal login password or DSC.
/// </summary>
public sealed record NicApiCredentials(string ClientId, string ClientSecret, string ApiUsername, string ApiPassword)
{
    /// <summary>
    /// Redacts the two secret members (<see cref="ClientSecret"/>, <see cref="ApiPassword"/>) from the record's
    /// synthesized <see cref="object.ToString"/> so a stray log/debug of the credential set can never leak them in
    /// plaintext (ER-16). The two non-secret members (<see cref="ClientId"/>, <see cref="ApiUsername"/>) stay visible so
    /// the string remains a usable diagnostic. Redaction is display-only — the synthesized <c>Equals</c>/<c>GetHashCode</c>
    /// still compare/hash the real values.
    /// </summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("ClientId = ").Append(ClientId);
        builder.Append(", ClientSecret = <redacted>");
        builder.Append(", ApiUsername = ").Append(ApiUsername);
        builder.Append(", ApiPassword = <redacted>");
        return true;
    }
}
