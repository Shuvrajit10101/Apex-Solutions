using System;
using System.Net.Http;
using Apex.Desktop.Services;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// Phase-9 slice-4a <b>CustomerNicDirect seam</b> gate (RQ-30; ER-16). The optional live e-invoicing connector is
/// <b>wired but deferred</b>: it implements <see cref="IGstPortalConnector"/> and depends on
/// <see cref="INicCredentialStore"/> + an injected <see cref="HttpClient"/>, but with no configured creds/client it
/// <b>fails fast</b> with a clear "not configured" message — never a silent success, never a plaintext-credential read.
/// (This test lives in Apex.Desktop.Tests because the connector lives in the Desktop composition layer per the brief's
/// layering — the pure Apex.Ledger.Io never references it.)
/// </summary>
public sealed class CustomerNicDirectConnectorTests
{
    private sealed class FakeCredentialStore : INicCredentialStore
    {
        private readonly bool _has;
        public FakeCredentialStore(bool has) => _has = has;
        public bool HasCredentials(string gstin) => _has;
        public NicApiCredentials Get(string gstin) => new("c", "s", "u", "p");
        public void Store(string gstin, NicApiCredentials creds) { }
        public void Clear(string gstin) { }
    }

    [Fact]
    public void Mode_is_customer_nic_direct_and_it_implements_the_connector_contract()
    {
        IGstPortalConnector connector = new CustomerNicDirectConnector(new FakeCredentialStore(has: false), "27AAPFU0939F1ZV");
        Assert.Equal(GstConnectorMode.CustomerNicDirect, connector.Mode);
    }

    [Fact]
    public void With_no_configured_creds_or_client_it_fails_fast_never_silently_succeeds()
    {
        var connector = new CustomerNicDirectConnector(new FakeCredentialStore(has: false), "27AAPFU0939F1ZV");
        var req = new Inv01Request(new byte[] { 1 }, "INV-1", Guid.NewGuid());

        var ex = Assert.Throws<InvalidOperationException>(() => connector.SubmitForIrn(req));
        Assert.Contains("not configured", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidOperationException>(() => connector.CancelIrn(new IrnCancelRequest("IRN", "1", null)));
    }

    [Fact]
    public void With_creds_but_no_http_client_it_still_fails_fast()
    {
        // Creds present but the live HTTP path is deferred (no client wired) ⇒ still not configured.
        var connector = new CustomerNicDirectConnector(new FakeCredentialStore(has: true), "27AAPFU0939F1ZV", httpClient: null);
        Assert.Throws<InvalidOperationException>(() =>
            connector.SubmitForIrn(new Inv01Request(new byte[] { 1 }, "INV-1", Guid.NewGuid())));
    }
}
