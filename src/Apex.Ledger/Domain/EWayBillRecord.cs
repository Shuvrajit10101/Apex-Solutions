namespace Apex.Ledger.Domain;

/// <summary>
/// A per-voucher <b>e-Way Bill artefact</b> (Phase 9 slice 5; RQ-6; ER-5) — the record of one goods movement's EWB-01
/// (Part A + Part B) lifecycle. The outbound-artefact twin of <see cref="EInvoiceRecord"/>: a mutable
/// value-object-with-identity whose state advances <c>Pending → Generated → Cancelled/Failed</c>, plus an
/// <see cref="EWayStatus.Expired"/> state the validity engine <b>derives</b> (never written into the aggregate).
/// <para>
/// <b>Design north star (ER-5 twin):</b> the 12-digit <see cref="EwbNumber"/> and the <see cref="ValidUpto"/> are NEVER
/// computed locally — there is <b>no ctor path and no method that derives them</b>. They can only arrive from the portal,
/// through <see cref="RecordPortalResponse"/> (what the connector handed back). The structural absence of any
/// number-generation surface is the guarantee. Rehydration from the trusted store/import copies the portal-issued values
/// verbatim via <see cref="Rehydrate"/> — again never deriving them.
/// </para>
/// </summary>
/// <remarks>Framework-, DB- and clock-free (the cancel window and validity are checked with caller-supplied dates —
/// <see cref="Services.EWayValidity"/> owns the arithmetic). Part A is assembled locally from the posted voucher; Part B
/// is user-entered before submission; the EWB number + validity are inbound-only.</remarks>
public sealed class EWayBillRecord
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The source goods-movement voucher this EWB was raised for.</summary>
    public Guid SourceVoucherId { get; }

    /// <summary>The <b>uppercased</b> base document number the EWB references.</summary>
    public string DocumentNumberUpper { get; }

    /// <summary>The EWB lifecycle state (Pending initial). <see cref="EWayStatus.Expired"/> is never stored here — it is a
    /// derived view over <see cref="ValidUpto"/> (like a post-dated voucher).</summary>
    public EWayStatus Status { get; private set; }

    // ----- Part A (assembled locally from the source voucher) -----

    /// <summary>Outward / Inward supply type (NIC supplyType).</summary>
    public string? SupplyType { get; }

    /// <summary>Supply / Job Work / Export / … (NIC subSupplyType).</summary>
    public string? SubSupplyType { get; }

    /// <summary>INV / CRN / DBN / BIL / … (NIC docType).</summary>
    public string? DocType { get; }

    /// <summary>The Rule-138 consignment value in integer paisa (computed off the posted lines, §1.3), stored for audit.</summary>
    public long ConsignmentValuePaisa { get; }

    // ----- Part B (transport — user-entered before generation) -----

    /// <summary>The transporter id / TRANSIN (15-char); <c>null</c> until Part-B is entered.</summary>
    public string? TransporterId { get; private set; }

    /// <summary>The transport mode; <c>null</c> until Part-B is entered.</summary>
    public EWayTransportMode? Mode { get; private set; }

    /// <summary>The vehicle number; <c>null</c> until Part-B is entered.</summary>
    public string? VehicleNumber { get; private set; }

    /// <summary>The approximate distance (km) driving the validity engine.</summary>
    public int DistanceKm { get; private set; }

    /// <summary>The transport document number (LR/RR/AWB/BL); <c>null</c> until Part-B is entered.</summary>
    public string? TransportDocNo { get; private set; }

    /// <summary>The ship-from 2-digit state code (Part A).</summary>
    public string? ShipFromStateCode { get; private set; }

    /// <summary>The ship-to 2-digit state code (Part A).</summary>
    public string? ShipToStateCode { get; private set; }

    /// <summary>Over-Dimensional-Cargo / multimodal-ship flag ⇒ the 20-km/day validity rule.</summary>
    public bool IsOverDimensionalCargo { get; private set; }

    // ----- Forward-compat: Ship-To GSTIN + closure, gated to eff. 01-Aug-2026 (§2.5, DP-12) -----

    /// <summary>The Ship-To GSTIN — mandatory from 01-Aug-2026 (inert/optional before that date).</summary>
    public string? ShipToGstin { get; private set; }

    /// <summary>Whether a voluntary EWB "closure" was requested (gated to 01-Aug-2026).</summary>
    public bool ClosureRequested { get; private set; }

    /// <summary>The date the EWB was closed; <c>null</c> unless closed.</summary>
    public DateOnly? ClosedOn { get; private set; }

    // ----- FROM the portal (never local, ER-5 twin) -----

    /// <summary>The 12-digit EWB number — <b>FROM the portal</b>, never computed here; <c>null</c> until Generated.</summary>
    public string? EwbNumber { get; private set; }

    /// <summary>The portal generation timestamp; <c>null</c> until Generated. Anchors the 24-h cancel window + validity.</summary>
    public DateTimeOffset? GeneratedAt { get; private set; }

    /// <summary>The portal-computed validity end; <c>null</c> until Generated. Never derived locally (ER-5).</summary>
    public DateTimeOffset? ValidUpto { get; private set; }

    /// <summary>The date the EWB was cancelled; <c>null</c> unless Cancelled.</summary>
    public DateOnly? CancelledOn { get; private set; }

    /// <summary>The NIC cancel-reason code; <c>null</c> unless Cancelled.</summary>
    public string? CancelReasonCode { get; private set; }

    /// <summary>The portal error code on a Failed submission; <c>null</c> otherwise.</summary>
    public string? ErrorCode { get; private set; }

    /// <summary>The portal error message on a Failed submission; <c>null</c> otherwise.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>Creates a <b>fresh</b> Pending EWB record with its locally-assembled Part A. There is deliberately NO
    /// parameter that sets an EWB number / validity (ER-5 twin).</summary>
    public EWayBillRecord(
        Guid id, Guid sourceVoucherId, string documentNumberUpper, string? supplyType, string? subSupplyType,
        string? docType, long consignmentValuePaisa, string? shipFromStateCode, string? shipToStateCode,
        string? shipToGstin = null)
    {
        if (string.IsNullOrWhiteSpace(documentNumberUpper))
            throw new ArgumentException("e-Way Bill document number is required.", nameof(documentNumberUpper));
        if (consignmentValuePaisa < 0)
            throw new ArgumentException("e-Way Bill consignment value must be ≥ 0.", nameof(consignmentValuePaisa));

        Id = id;
        SourceVoucherId = sourceVoucherId;
        DocumentNumberUpper = documentNumberUpper;
        Status = EWayStatus.Pending;
        SupplyType = supplyType;
        SubSupplyType = subSupplyType;
        DocType = docType;
        ConsignmentValuePaisa = consignmentValuePaisa;
        ShipFromStateCode = shipFromStateCode;
        ShipToStateCode = shipToStateCode;
        ShipToGstin = shipToGstin;
    }

    /// <summary>Rehydrates a persisted/imported record verbatim from the trusted store (Phase 9 slice 5). The
    /// portal-issued EWB number / validity are <b>copied</b>, never derived (ER-5). Validates the invariant that a
    /// Generated record carries an EWB number AND a validity, so a malformed import fails fast in pre-flight ⇒
    /// all-or-nothing (RQ-23).</summary>
    public static EWayBillRecord Rehydrate(
        Guid id, Guid sourceVoucherId, string documentNumberUpper, EWayStatus status,
        string? supplyType, string? subSupplyType, string? docType, long consignmentValuePaisa,
        string? transporterId, EWayTransportMode? mode, string? vehicleNumber, int distanceKm, string? transportDocNo,
        string? shipFromStateCode, string? shipToStateCode, bool isOverDimensionalCargo,
        string? shipToGstin, bool closureRequested, DateOnly? closedOn,
        string? ewbNumber, DateTimeOffset? generatedAt, DateTimeOffset? validUpto,
        DateOnly? cancelledOn, string? cancelReasonCode, string? errorCode = null, string? errorMessage = null)
    {
        if (status == EWayStatus.Generated && string.IsNullOrWhiteSpace(ewbNumber))
            throw new ArgumentException("A Generated e-Way Bill record requires a portal-issued EWB number.", nameof(ewbNumber));
        if (status == EWayStatus.Generated && validUpto is null)
            throw new ArgumentException("A Generated e-Way Bill record requires a portal-issued validity.", nameof(validUpto));
        if (status == EWayStatus.Cancelled && string.IsNullOrWhiteSpace(ewbNumber))
            throw new ArgumentException("A Cancelled e-Way Bill record requires the EWB number it cancelled.", nameof(ewbNumber));

        return new EWayBillRecord(id, sourceVoucherId, documentNumberUpper, supplyType, subSupplyType, docType,
            consignmentValuePaisa, shipFromStateCode, shipToStateCode, shipToGstin)
        {
            Status = status,
            TransporterId = transporterId,
            Mode = mode,
            VehicleNumber = vehicleNumber,
            DistanceKm = distanceKm,
            TransportDocNo = transportDocNo,
            IsOverDimensionalCargo = isOverDimensionalCargo,
            ClosureRequested = closureRequested,
            ClosedOn = closedOn,
            EwbNumber = ewbNumber,
            GeneratedAt = generatedAt,
            ValidUpto = validUpto,
            CancelledOn = cancelledOn,
            CancelReasonCode = cancelReasonCode,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
        };
    }

    /// <summary>Records the user-entered Part-B transport detail (mode / vehicle / distance / transport-doc / ODC).
    /// The EWB number / validity are NOT set here (ER-5).</summary>
    internal void SetPartB(
        string? transporterId, EWayTransportMode? mode, string? vehicleNumber, int distanceKm, string? transportDocNo,
        bool isOverDimensionalCargo)
    {
        if (distanceKm < 0)
            throw new ArgumentException("e-Way Bill distance must be ≥ 0 km.", nameof(distanceKm));
        TransporterId = transporterId;
        Mode = mode;
        VehicleNumber = vehicleNumber;
        DistanceKm = distanceKm;
        TransportDocNo = transportDocNo;
        IsOverDimensionalCargo = isOverDimensionalCargo;
    }

    /// <summary>Records the portal's response — stores the 12-digit EWB number + generation timestamp + validity verbatim
    /// (ER-5) and flips to <see cref="EWayStatus.Generated"/>. The values are supplied by the caller (never derived here).
    /// Accepts ONLY a record still awaiting a number — <see cref="EWayStatus.Pending"/> (the offline baseline) or
    /// <see cref="EWayStatus.Failed"/> (a retry). A Cancelled/Generated record throws.</summary>
    internal void RecordPortalResponse(string ewbNumber, DateTimeOffset generatedAt, DateTimeOffset validUpto)
    {
        if (Status is not (EWayStatus.Pending or EWayStatus.Failed))
            throw new InvalidOperationException(
                $"A portal response can be recorded only on a Pending or Failed e-Way Bill; this record is {Status} " +
                "(a cancelled EWB cannot be resurrected and a generated EWB cannot be overwritten).");
        if (string.IsNullOrWhiteSpace(ewbNumber))
            throw new ArgumentException("The portal response must carry an EWB number.", nameof(ewbNumber));
        Status = EWayStatus.Generated;
        EwbNumber = ewbNumber;
        GeneratedAt = generatedAt;
        ValidUpto = validUpto;
        ErrorCode = null;
        ErrorMessage = null;
    }

    /// <summary>Records a 24-h full-document cancel (no partial). Callers enforce the window; this only flips state.</summary>
    internal void MarkCancelled(DateOnly on, string reasonCode)
    {
        Status = EWayStatus.Cancelled;
        CancelledOn = on;
        CancelReasonCode = reasonCode;
    }

    /// <summary>Records a portal rejection.</summary>
    internal void MarkFailed(string errorCode, string errorMessage)
    {
        Status = EWayStatus.Failed;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>Records a portal-granted validity extension — stores the new (portal-computed) validity verbatim (ER-5).
    /// Only a Generated EWB can be extended.</summary>
    internal void MarkExtended(DateTimeOffset newValidUpto)
    {
        if (Status != EWayStatus.Generated)
            throw new InvalidOperationException("Only a Generated e-Way Bill can be extended.");
        ValidUpto = newValidUpto;
    }

    /// <summary>Records a voluntary closure (gated to 01-Aug-2026 by the service). Advisory — no state transition.</summary>
    internal void MarkClosed(DateOnly on)
    {
        ClosureRequested = true;
        ClosedOn = on;
    }
}
