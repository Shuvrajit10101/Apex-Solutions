namespace Apex.Ledger.Domain;

/// <summary>
/// A per-voucher <b>e-invoice IRP artefact</b> (Phase 9 slice 4a; RQ-5; ER-5) — the record of one covered outward
/// document's Invoice-Reference-Number lifecycle. Mirrors the voucher-linked <see cref="RcmDocument"/> shape but is a
/// mutable value-object-with-identity because its state advances (Pending → Generated → Cancelled/Failed).
/// <para>
/// <b>Design north star (ER-5):</b> the 64-char <see cref="Irn"/> and the IRP-signed <see cref="SignedQr"/> are NEVER
/// computed or signed locally — there is <b>no ctor path and no method that derives them</b>. They can only arrive from
/// the IRP, through <see cref="RecordIrpResponse"/> (the engine passes what the connector handed back). The structural
/// absence of any hashing/signing surface is the guarantee. Rehydration from the trusted store/import copies the
/// IRP-issued values verbatim via <see cref="Rehydrate"/> — again never deriving them.
/// </para>
/// </summary>
/// <remarks>Framework-, DB- and clock-free (the cancel window is checked with a caller-supplied <c>today</c>).</remarks>
public sealed class EInvoiceRecord
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The source outward accounting voucher this e-invoice was raised for.</summary>
    public Guid SourceVoucherId { get; }

    /// <summary>The <b>uppercased</b> document number used to build the IRN request (normalised before submission so the
    /// request, the stored artefact and any later cancel all reference the identical doc-no; IRP is case-insensitive from
    /// 01-Jun-2025).</summary>
    public string DocumentNumberUpper { get; }

    /// <summary>The IRP lifecycle state (NotApplicable/Pending/Generated/Cancelled/Failed).</summary>
    public EInvoiceStatus Status { get; private set; }

    /// <summary>The 64-char Invoice Reference Number — <b>FROM the IRP</b>, never computed here; <c>null</c> until Generated.</summary>
    public string? Irn { get; private set; }

    /// <summary>The IRP acknowledgement number; <c>null</c> until Generated.</summary>
    public string? AckNo { get; private set; }

    /// <summary>The IRP acknowledgement date; <c>null</c> until Generated. Anchors the 24-h cancel window.</summary>
    public DateOnly? AckDate { get; private set; }

    /// <summary>The IRP-signed QR string, stored verbatim; <c>null</c> until Generated. Never computed locally (ER-5).</summary>
    public string? SignedQr { get; private set; }

    /// <summary>The IRP-signed INV-01 response payload, stored verbatim; <c>null</c> until Generated.</summary>
    public byte[]? SignedJson { get; private set; }

    /// <summary>The date the IRN was cancelled; <c>null</c> unless Cancelled.</summary>
    public DateOnly? CancelledOn { get; private set; }

    /// <summary>The NIC cancel-reason code; <c>null</c> unless Cancelled.</summary>
    public string? CancelReasonCode { get; private set; }

    /// <summary>The IRP error code on a Failed submission; <c>null</c> otherwise.</summary>
    public string? ErrorCode { get; private set; }

    /// <summary>The IRP error message on a Failed submission; <c>null</c> otherwise.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>Creates a <b>fresh</b> request record for a covered voucher — status <see cref="EInvoiceStatus.Pending"/>,
    /// no IRN. There is deliberately NO parameter that sets an IRN/QR (ER-5).</summary>
    public EInvoiceRecord(Guid id, Guid sourceVoucherId, string documentNumberUpper)
    {
        if (string.IsNullOrWhiteSpace(documentNumberUpper))
            throw new ArgumentException("e-Invoice document number is required.", nameof(documentNumberUpper));

        Id = id;
        SourceVoucherId = sourceVoucherId;
        DocumentNumberUpper = documentNumberUpper;
        Status = EInvoiceStatus.Pending;
    }

    /// <summary>Rehydrates a persisted/imported record verbatim from the trusted store (Phase 9 slice 4a). The IRP-issued
    /// IRN/QR are <b>copied</b>, never derived (ER-5). Validates the invariant that a Generated record carries an IRN, so a
    /// malformed import (Generated with no IRN) fails fast in pre-flight ⇒ all-or-nothing (RQ-23).</summary>
    public static EInvoiceRecord Rehydrate(
        Guid id, Guid sourceVoucherId, string documentNumberUpper, EInvoiceStatus status,
        string? irn, string? ackNo, DateOnly? ackDate, string? signedQr, byte[]? signedJson,
        DateOnly? cancelledOn, string? cancelReasonCode, string? errorCode = null, string? errorMessage = null)
    {
        if (status == EInvoiceStatus.Generated && string.IsNullOrWhiteSpace(irn))
            throw new ArgumentException("A Generated e-invoice record requires an IRP-issued IRN.", nameof(irn));
        if (status == EInvoiceStatus.Cancelled && string.IsNullOrWhiteSpace(irn))
            throw new ArgumentException("A Cancelled e-invoice record requires the IRP-issued IRN it cancelled.", nameof(irn));

        return new EInvoiceRecord(id, sourceVoucherId, documentNumberUpper)
        {
            Status = status,
            Irn = irn,
            AckNo = ackNo,
            AckDate = ackDate,
            SignedQr = signedQr,
            SignedJson = signedJson,
            CancelledOn = cancelledOn,
            CancelReasonCode = cancelReasonCode,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
        };
    }

    /// <summary>Marks the record Pending (the request was staged/submitted). Idempotent on a fresh record.</summary>
    internal void MarkPending() => Status = EInvoiceStatus.Pending;

    /// <summary>Records the IRP's signed response — stores the IRN/Ack/QR/signed-JSON verbatim (ER-5) and flips to
    /// <see cref="EInvoiceStatus.Generated"/>. The values are supplied by the caller (never derived here). Accepts ONLY
    /// a record still awaiting an IRN — <see cref="EInvoiceStatus.Pending"/> (the offline baseline) or
    /// <see cref="EInvoiceStatus.Failed"/> (a retry after an IRP rejection). A <see cref="EInvoiceStatus.Cancelled"/>
    /// IRN can never be resurrected, and a <see cref="EInvoiceStatus.Generated"/> IRN can never be silently overwritten,
    /// so either state throws.</summary>
    internal void RecordIrpResponse(string irn, string ackNo, DateOnly ackDate, string signedQr, byte[] signedJson)
    {
        if (Status is not (EInvoiceStatus.Pending or EInvoiceStatus.Failed))
            throw new InvalidOperationException(
                $"An IRP response can be recorded only on a Pending or Failed e-invoice; this record is {Status} " +
                "(a cancelled IRN cannot be resurrected and a generated IRN cannot be overwritten).");
        if (string.IsNullOrWhiteSpace(irn))
            throw new ArgumentException("The IRP response must carry an IRN.", nameof(irn));
        Status = EInvoiceStatus.Generated;
        Irn = irn;
        AckNo = ackNo;
        AckDate = ackDate;
        SignedQr = signedQr;
        SignedJson = signedJson;
        ErrorCode = null;
        ErrorMessage = null;
    }

    /// <summary>Records a 24-h full-document cancel (no partial). Callers enforce the window; this only flips state.</summary>
    internal void MarkCancelled(DateOnly on, string reasonCode)
    {
        Status = EInvoiceStatus.Cancelled;
        CancelledOn = on;
        CancelReasonCode = reasonCode;
    }

    /// <summary>Records an IRP rejection.</summary>
    internal void MarkFailed(string errorCode, string errorMessage)
    {
        Status = EInvoiceStatus.Failed;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }
}
