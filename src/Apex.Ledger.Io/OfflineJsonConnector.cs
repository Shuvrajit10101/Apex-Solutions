using Apex.Ledger.Domain;

namespace Apex.Ledger.Io;

/// <summary>
/// The <b>zero-credential offline default</b> connector (Phase 9 slice 4a; RQ-30; ER-16 baseline). It only materialises
/// / stages INV-01 request bytes — no live I/O, no credential store. <see cref="SubmitForIrn"/> returns a non-accepted
/// (Pending) result; the caller keeps <see cref="Inv01Request.Json"/> for file export, and the IRP's signed response is
/// later imported and applied via <c>EInvoiceService.RecordIrpResponse</c> (so the IRN/QR still only ever arrive
/// inbound — ER-5). Constructable with <b>no arguments</b> — it needs, and holds, no secret.
/// </summary>
public sealed class OfflineJsonConnector : IGstPortalConnector
{
    /// <inheritdoc />
    public GstConnectorMode Mode => GstConnectorMode.OfflineJson;

    /// <summary>Stages the INV-01 request for offline upload; returns Pending (no IRN yet — the caller retains the
    /// request bytes and imports the IRP-signed response separately).</summary>
    public IrnSubmissionResult SubmitForIrn(Inv01Request request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new IrnSubmissionResult(
            Accepted: false, Irn: null, AckNo: null, AckDate: null, SignedQr: null, SignedJson: null,
            ErrorCode: null, ErrorMessage: null);
    }

    /// <summary>Stages an offline cancel request; the cancel is finalised when the signed cancel response is imported.</summary>
    public IrnCancelResult CancelIrn(IrnCancelRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new IrnCancelResult(Cancelled: false, ErrorCode: null, ErrorMessage: null);
    }
}
