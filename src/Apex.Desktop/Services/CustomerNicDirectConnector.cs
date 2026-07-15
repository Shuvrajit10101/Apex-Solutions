using System;
using System.Net.Http;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;

namespace Apex.Desktop.Services;

/// <summary>
/// The optional <b>live</b> e-invoicing transport (Phase 9 slice 4a; RQ-30) — a direct HTTP path to the NIC IRP using
/// the customer's OWN NIC API credentials (₹0 to NIC). Built as a <b>seam wired but deferred</b>: it depends on
/// <see cref="INicCredentialStore"/> (the customer's creds, protected-at-rest — ER-16) and an injected
/// <see cref="HttpClient"/>, but the live IRP call is NOT implemented in Phase 9. Until the customer's creds and the NIC
/// endpoints are provisioned it <b>fails fast</b> with a clear "live e-invoicing not configured" message — never a
/// silent success, never a plaintext-credential read.
/// <para>
/// The <b>pure core never references this type</b> (it lives in the Desktop composition layer): the engine is reached
/// only through <see cref="IGstPortalConnector"/>, and an offline taxpayer behaves as if this connector did not exist.
/// The app NEVER holds a GSP/vendor credential nor the customer's portal password/DSC (ER-16); the only secret is the
/// customer's own NIC API credential, materialised from <see cref="INicCredentialStore"/> at the point of a call.
/// </para>
/// </summary>
public sealed class CustomerNicDirectConnector : IGstPortalConnector
{
    private const string NotConfigured =
        "Live e-invoicing (CustomerNicDirect) is not configured: provide the customer's own NIC API credentials and " +
        "endpoint before using the live path, or use the offline JSON default.";

    private readonly INicCredentialStore _credentials;
    private readonly string _gstin;
    private readonly HttpClient? _httpClient;

    /// <summary>Wires the live seam to the customer's credential store + their GSTIN + an injected HTTP client. The live
    /// NIC call is deferred; with no configured creds or client this connector fails fast.</summary>
    public CustomerNicDirectConnector(INicCredentialStore credentials, string gstin, HttpClient? httpClient = null)
    {
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _gstin = gstin ?? throw new ArgumentNullException(nameof(gstin));
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public GstConnectorMode Mode => GstConnectorMode.CustomerNicDirect;

    /// <inheritdoc />
    public IrnSubmissionResult SubmitForIrn(Inv01Request request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureConfigured();
        // Deferred: the live NIC IRP call is a build-time detail for a later slice (the customer's own creds from
        // _credentials.Get(_gstin) would be used ONLY at the point of this call, never stored in the aggregate).
        throw new NotSupportedException(NotConfigured);
    }

    /// <inheritdoc />
    public IrnCancelResult CancelIrn(IrnCancelRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureConfigured();
        throw new NotSupportedException(NotConfigured);
    }

    /// <inheritdoc />
    public EwbSubmissionResult SubmitEway(Ewb01Request request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureConfigured();
        // Deferred: the live NIC EWB call is a later slice's build-time detail; the customer's own creds from
        // _credentials.Get(_gstin) would be used ONLY at the point of this call, never stored in the aggregate (ER-16).
        throw new NotSupportedException(NotConfigured);
    }

    /// <inheritdoc />
    public EwbCancelResult CancelEway(EwbCancelRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureConfigured();
        throw new NotSupportedException(NotConfigured);
    }

    /// <inheritdoc />
    public EwbExtendResult ExtendEway(EwbExtendRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureConfigured();
        throw new NotSupportedException(NotConfigured);
    }

    /// <inheritdoc />
    public EwbSubmissionResult SubmitConsolidatedEway(Ewb02Request request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureConfigured();
        throw new NotSupportedException(NotConfigured);
    }

    /// <inheritdoc />
    public Gstr2bImportResult FetchStatement(Gstr2bFetchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        // GSTR-2A/2B stay OFFLINE-ONLY in Phase 9 (RQ-30/DP-25): even the live NIC path has no inbound returns fetch —
        // that is a future-GSP capability. Only e-Invoice / e-Way have a live path.
        throw new NotSupportedException(
            "Live GSTR-2B/2A fetch is not available: import the portal-downloaded JSON via the offline path.");
    }

    private void EnsureConfigured()
    {
        if (_httpClient is null || !_credentials.HasCredentials(_gstin))
            throw new InvalidOperationException(NotConfigured);
    }
}
