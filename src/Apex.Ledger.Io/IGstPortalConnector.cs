using Apex.Ledger.Domain;

namespace Apex.Ledger.Io;

/// <summary>
/// Transport abstraction for GST-portal interchange (Phase 9 slice 4a; RQ-30). A <b>pure contract</b> that carries bytes
/// and IRP artefacts, never tax logic. e-Invoice uses <see cref="SubmitForIrn"/> / <see cref="CancelIrn"/> now; e-Way
/// (S5) will add its own members; returns/2A/2B/IMS stay offline-only in Phase 9 (a future GSP impl fills the rest).
/// <para>
/// <b>ER-5 by construction:</b> there is deliberately <b>no</b> <c>ComputeIrn</c> / <c>SignQr</c> member — the IRN and the
/// signed QR only ever arrive <i>inbound</i> on <see cref="IrnSubmissionResult"/>. The offline default
/// (<see cref="OfflineJsonConnector"/>) needs <b>zero credentials</b> and touches no secret store (the ER-16 baseline);
/// the live path (<c>CustomerNicDirectConnector</c>) uses only the customer's own NIC creds via
/// <see cref="INicCredentialStore"/>.
/// </para>
/// </summary>
public interface IGstPortalConnector
{
    /// <summary>The transport mode this connector implements.</summary>
    GstConnectorMode Mode { get; }

    /// <summary>
    /// Submits an INV-01 request for an IRN. <b>Offline</b>: retains the request bytes for the user to upload and returns
    /// a non-accepted (Pending) result with no IRN. <b>Live (CustomerNicDirect)</b>: posts to the NIC IRP with the
    /// customer's own creds and returns the signed result. The transport never re-decides policy — the 24-h/full-doc
    /// cancel rule and applicability are enforced by <c>EInvoiceService</c> BEFORE this is called.
    /// </summary>
    IrnSubmissionResult SubmitForIrn(Inv01Request request);

    /// <summary>Cancels an IRN. <b>Offline</b>: stages a cancel request. <b>Live</b>: calls the IRP cancel API.</summary>
    IrnCancelResult CancelIrn(IrnCancelRequest request);

    /// <summary>
    /// Submits an EWB-01 request for a 12-digit e-Way Bill number + validity (Phase 9 slice 5). <b>Offline</b>: retains
    /// the request bytes for the user to upload and returns a non-accepted result with no EWB number (status stays Pending
    /// until the portal write-back is imported). <b>Live (CustomerNicDirect)</b>: posts to the NIC EWB API with the
    /// customer's own creds and returns the generated number/validity. The transport never re-decides policy — Rule-138
    /// applicability + the validity/Part-B rules are enforced by <c>EWayBillService</c> BEFORE this is called.
    /// </summary>
    EwbSubmissionResult SubmitEway(Ewb01Request request);

    /// <summary>Cancels an EWB. <b>Offline</b>: stages a cancel request. <b>Live</b>: calls the EWB cancel API.</summary>
    EwbCancelResult CancelEway(EwbCancelRequest request);

    /// <summary>Extends an EWB's validity. <b>Offline</b>: stages an extend request. <b>Live</b>: calls the EWB extend API.</summary>
    EwbExtendResult ExtendEway(EwbExtendRequest request);

    /// <summary>Submits a consolidated EWB-02 request. <b>Offline</b>: stages the request. <b>Live</b>: calls the CEWB API.</summary>
    EwbSubmissionResult SubmitConsolidatedEway(Ewb02Request request);
}

/// <summary>An INV-01 IRN request: the deterministic request bytes, the uppercased document number, and the source
/// voucher id. Carries NO IRN (the IRN is what the request asks the IRP to mint — ER-5).</summary>
public sealed record Inv01Request(byte[] Json, string DocumentNumberUpper, Guid VoucherId);

/// <summary>What the IRP hands back for an IRN submission — stored verbatim on the record (ER-5). On the offline
/// baseline <see cref="Accepted"/> is false and the IRN fields are null (status stays Pending until the signed response
/// is imported).</summary>
public sealed record IrnSubmissionResult(
    bool Accepted, string? Irn, string? AckNo, DateOnly? AckDate, string? SignedQr, byte[]? SignedJson,
    string? ErrorCode, string? ErrorMessage);

/// <summary>An IRN cancel request (the IRN + a NIC cancel-reason code + optional remarks).</summary>
public sealed record IrnCancelRequest(string Irn, string CancelReasonCode, string? Remarks);

/// <summary>The result of an IRN cancel.</summary>
public sealed record IrnCancelResult(bool Cancelled, string? ErrorCode, string? ErrorMessage);

/// <summary>An EWB-01 request (Phase 9 slice 5): the deterministic request bytes, the uppercased base document number,
/// and the source voucher id. Carries NO EWB number (that is what the request asks the portal to mint — ER-5 twin).</summary>
public sealed record Ewb01Request(byte[] Json, string DocumentNumberUpper, Guid VoucherId);

/// <summary>A consolidated EWB-02 request (Phase 9 slice 5): the deterministic request bytes + the child EWB numbers it
/// consolidates. The CEWB number is inbound only (ER-5 twin).</summary>
public sealed record Ewb02Request(byte[] Json, IReadOnlyList<string> ChildEwbNumbers);

/// <summary>What the portal hands back for an EWB submission — stored verbatim on the record (ER-5 twin). On the offline
/// baseline <see cref="Accepted"/> is false and the number/validity are null (status stays Pending until the write-back
/// is imported).</summary>
public sealed record EwbSubmissionResult(
    bool Accepted, string? EwbNumber, DateTimeOffset? GeneratedAt, DateTimeOffset? ValidUpto,
    string? ErrorCode, string? ErrorMessage);

/// <summary>An EWB cancel request (the EWB number + a NIC cancel-reason code + optional remarks).</summary>
public sealed record EwbCancelRequest(string EwbNumber, string CancelReasonCode, string? Remarks);

/// <summary>The result of an EWB cancel.</summary>
public sealed record EwbCancelResult(bool Cancelled, string? ErrorCode, string? ErrorMessage);

/// <summary>An EWB validity-extension request (the EWB number + the remaining distance + a reason + optional remarks).</summary>
public sealed record EwbExtendRequest(string EwbNumber, int RemainingDistanceKm, string Reason, string? Remarks);

/// <summary>The result of an EWB extension — the portal-computed new validity (inbound only, ER-5 twin).</summary>
public sealed record EwbExtendResult(bool Extended, DateTimeOffset? NewValidUpto, string? ErrorCode, string? ErrorMessage);
