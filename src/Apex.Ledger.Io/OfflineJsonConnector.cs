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

    /// <summary>Stages the EWB-01 request for offline upload; returns non-accepted (no EWB number yet — the caller retains
    /// the request bytes and imports the portal write-back separately, so the number still arrives inbound — ER-5).</summary>
    public EwbSubmissionResult SubmitEway(Ewb01Request request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new EwbSubmissionResult(
            Accepted: false, EwbNumber: null, GeneratedAt: null, ValidUpto: null, ErrorCode: null, ErrorMessage: null);
    }

    /// <summary>Stages an offline EWB cancel request; finalised when the write-back is imported.</summary>
    public EwbCancelResult CancelEway(EwbCancelRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new EwbCancelResult(Cancelled: false, ErrorCode: null, ErrorMessage: null);
    }

    /// <summary>Stages an offline EWB extend request; finalised when the write-back is imported.</summary>
    public EwbExtendResult ExtendEway(EwbExtendRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new EwbExtendResult(Extended: false, NewValidUpto: null, ErrorCode: null, ErrorMessage: null);
    }

    /// <summary>Stages the consolidated EWB-02 request for offline upload; returns non-accepted (no CEWB number yet).</summary>
    public EwbSubmissionResult SubmitConsolidatedEway(Ewb02Request request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new EwbSubmissionResult(
            Accepted: false, EwbNumber: null, GeneratedAt: null, ValidUpto: null, ErrorCode: null, ErrorMessage: null);
    }

    /// <summary>Imports a portal GSTR-2B/2A statement offline (Phase 9 slice 6; RQ-12) — deterministically parses the
    /// supplied JSON bytes with <b>zero credentials</b> (ER-16 baseline). A malformed / wrong-recipient file fails fast.</summary>
    public Gstr2bImportResult FetchStatement(Gstr2bFetchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Gstr2bJsonParser.Parse(request.Json, request.StatementType, request.RecipientGstin);
    }
}
