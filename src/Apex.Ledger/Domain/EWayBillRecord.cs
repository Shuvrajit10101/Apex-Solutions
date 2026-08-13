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

    /// <summary>The <b>as-typed</b> rendered base document number the EWB references (case preserved; the property name
    /// predates the as-typed policy and is retained for the persistence/canonical column mapping).</summary>
    public string DocumentNumberUpper { get; }

    /// <summary>The EWB lifecycle state (Pending initial). <see cref="EWayStatus.Expired"/> is never stored here — it is a
    /// derived view over <see cref="ValidUpto"/> (like a post-dated voucher).</summary>
    public EWayStatus Status { get; private set; }

    // ----- Part A (assembled locally from the source voucher) -----

    /// <summary>NIC <c>supplyType</c> — the CODE <c>I</c> (Inward) or <c>O</c> (Outward), never the description.
    /// Decided by <see cref="Services.EWayBillService.PartACodesFor"/>, which carries the official citations.</summary>
    public string? SupplyType { get; }

    /// <summary>NIC <c>subSupplyType</c> — the NUMERIC code 1–12 (1 Supply, 2 Import, 3 Export, 4 Job Work, 5 For Own
    /// Use, 6 Job work Returns, 7 Sales Return, 8 Others, 9 SKD/CKD/Lots, 10 Line Sales, 11 Recipient Not Known,
    /// 12 Exhibition or Fairs), never the description.</summary>
    public string? SubSupplyType { get; }

    /// <summary>NIC <c>docType</c> — one of exactly five values: <c>INV</c> Tax Invoice, <c>BIL</c> Bill of Supply,
    /// <c>BOE</c> Bill of Entry, <c>CHL</c> Delivery Challan, <c>OTH</c> Others. <b>Not</b> CRN/DBN: those belong to
    /// the separate e-invoice INV-01 <c>DocDtls.Typ</c> domain and are not e-Way values.</summary>
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

        var (supply, sub, doc) = NormaliseLegacyPartA(supplyType, subSupplyType, docType);

        return new EWayBillRecord(id, sourceVoucherId, documentNumberUpper, supply, sub, doc,
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

    /// <summary>
    /// <b>W0-8 — re-derives a legacy Part-A triple into NIC master codes.</b> Records prepared before the Part-A
    /// correction carry human-readable DESCRIPTIONS (<c>"Outward"</c>, <c>"Supply"</c>, <c>"Job Work"</c>,
    /// <c>"Handicraft"</c>) and two document codes that are not e-Way values at all (<c>CRN</c> / <c>DBN</c>, which
    /// belong to the e-invoice INV-01 <c>DocDtls.Typ</c> domain). Those rows sit on disk in the normal offline resting
    /// state — <see cref="EWayStatus.Pending"/>, because an EWB number only ever arrives from the portal (ER-5) — and
    /// they are <b>unfixable in-app</b>: the three fields are get-only with no mutator, <c>PrepareRecord</c> refuses a
    /// second record for the same voucher, and <c>Cancel</c> requires a Generated status. Left alone they would be
    /// filed verbatim by <c>EWayBillJson.BuildEwb01</c>, which is the exact malformed filing W0-2 set out to end.
    ///
    /// <para><b>This is a re-derivation, not a guess.</b> The legacy value set is CLOSED — it is the output of the old
    /// engine's own three switch expressions — so each legacy triple identifies the document it came from, and the
    /// corrected codes follow. Field-wise substitution would NOT do: legacy <c>("Outward","Supply","CRN")</c> would
    /// become <c>O | 1 | CHL</c>, and <c>Outward | Supply</c> permits only a Tax Invoice or a Bill of Supply, never a
    /// challan. So the mapping is combination-aware, keyed on the document:</para>
    /// <list type="bullet">
    /// <item><c>CRN</c> ⇒ a credit note ⇒ <c>I | 7 Sales Return | CHL</c> (job-work leg <c>6</c>).</item>
    /// <item><c>DBN</c> ⇒ a debit note ⇒ <c>O | 8 Others | CHL</c> (job-work leg <c>4</c>).</item>
    /// <item><c>CHL</c> ⇒ a delivery/receipt note ⇒ the same side it was filed on, sub-type <c>4</c>/<c>6</c> for job
    /// work else <c>8 Others</c> (<c>Supply</c> forbids a challan on either side).</item>
    /// <item>anything else ⇒ an invoice movement ⇒ <c>I|O | 1 Supply | INV</c>.</item>
    /// </list>
    ///
    /// <para><b>🔴 Two limits, stated rather than papered over.</b> A legacy row cannot recover (a) <c>BIL</c> for a
    /// composition/exempt sale or (b) <c>3 Export</c> for an overseas place of supply, because neither is recoverable
    /// from the stored triple alone — the old engine never wrote the distinction down. Both re-derive to the ordinary
    /// <c>1 | INV</c>, which is a permitted row and the same value the old record already carried, so nothing is made
    /// worse; re-preparing the record against the corrected engine is the only way to recover them.</para>
    ///
    /// <para>A triple already expressed in NIC codes is returned <b>untouched</b>, and an absent Part-A stays absent —
    /// normalisation never invents a value.</para>
    /// </summary>
    private static (string? Supply, string? Sub, string? Doc) NormaliseLegacyPartA(
        string? supplyType, string? subSupplyType, string? docType)
    {
        // Already in the NIC domains (supplyType ∈ {I,O}, subSupplyType ∈ {1…12}, docType ∈ the five codes) ⇒ verbatim.
        var inDomain = supplyType is "I" or "O"
            && subSupplyType is "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8" or "9" or "10" or "11" or "12"
            && docType is "INV" or "BIL" or "BOE" or "CHL" or "OTH";
        if (inDomain) return (supplyType, subSupplyType, docType);

        // No Part-A at all (an older import that never carried the fields) ⇒ leave it absent.
        if (supplyType is null && subSupplyType is null && docType is null) return (null, null, null);

        var inward = string.Equals(supplyType, "Inward", StringComparison.Ordinal) || supplyType == "I";
        var jobWork = string.Equals(subSupplyType, "Job Work", StringComparison.Ordinal);

        return docType switch
        {
            "CRN" => ("I", jobWork ? "6" : "7", "CHL"),
            "DBN" => ("O", jobWork ? "4" : "8", "CHL"),
            "CHL" => inward ? ("I", jobWork ? "6" : "8", "CHL") : ("O", jobWork ? "4" : "8", "CHL"),
            _ => inward ? ("I", "1", "INV") : ("O", "1", "INV"),
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
