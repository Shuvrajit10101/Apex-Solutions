namespace Apex.Ledger.Domain;

/// <summary>
/// The Job Work Order payload hung off an <see cref="InventoryVoucher"/> (Phase 6 slice 8; RQ-47), mirroring the
/// optional-payload pattern the voucher already uses for additional-cost lines. A Job Work In/Out Order records a
/// commitment — a finished good to be manufactured plus the tracked component lines — and affects <b>neither
/// accounts nor stock</b> (exactly like a Purchase/Sales Order). Its <see cref="Direction"/> says which side of
/// the relationship we are on (worker vs principal), while the per-line <see cref="JobWorkOrderLine.Track"/>
/// carries the actual pending-to-receive/issue behaviour, so the SAME voucher types serve both roles with no
/// hard-coded branch (RQ-50).
/// <para>When the component grid was auto-filled from a Slice-2 <see cref="BillOfMaterials"/> the resolved lines
/// are snapshotted into <see cref="Lines"/> at creation and <see cref="FillComponentsBomId"/> is kept only for
/// provenance (design Open Question 3). <see cref="FillComponentsBomId"/> is <c>null</c> for a manual ("Not
/// Applicable") order.</para>
/// </summary>
public sealed class JobWorkOrder
{
    private readonly List<JobWorkOrderLine> _lines;

    /// <summary>Which side of the job-work relationship this order is (worker In / principal Out).</summary>
    public JobWorkDirection Direction { get; }

    /// <summary>The job order number (e.g. "DKP/789"); required.</summary>
    public string OrderNo { get; }

    /// <summary>Free-text duration of process ("30 days"); optional.</summary>
    public string? DurationOfProcess { get; }

    /// <summary>Free-text nature of processing; optional.</summary>
    public string? NatureOfProcessing { get; }

    /// <summary>The finished-good <see cref="StockItem"/> to be manufactured; required.</summary>
    public Guid FinishedGoodStockItemId { get; }

    /// <summary>Finished-good ordered quantity (&gt; 0), in the item's base unit.</summary>
    public decimal FinishedGoodQuantity { get; }

    /// <summary>Optional finished-good due date.</summary>
    public DateOnly? FinishedGoodDueDate { get; }

    /// <summary>Optional finished-good location (godown).</summary>
    public Guid? FinishedGoodGodownId { get; }

    /// <summary>Optional finished-good per-unit rate (paisa-exact); <c>null</c> when none.</summary>
    public Money? FinishedGoodRate { get; }

    /// <summary>"Tracking Components = Yes" (RQ-47). Defaults true; when off the order carries no component lines.</summary>
    public bool TrackingComponents { get; }

    /// <summary>Provenance of a BOM-filled component list (Slice 2 link); <c>null</c> = Not Applicable / manual.</summary>
    public Guid? FillComponentsBomId { get; }

    /// <summary>The tracked component lines (RQ-47). Non-empty when <see cref="TrackingComponents"/> is on.</summary>
    public IReadOnlyList<JobWorkOrderLine> Lines => _lines;

    public JobWorkOrder(
        JobWorkDirection direction,
        string orderNo,
        Guid finishedGoodStockItemId,
        decimal finishedGoodQuantity,
        IEnumerable<JobWorkOrderLine> lines,
        Money? finishedGoodRate = null,
        DateOnly? finishedGoodDueDate = null,
        Guid? finishedGoodGodownId = null,
        bool trackingComponents = true,
        Guid? fillComponentsBomId = null,
        string? durationOfProcess = null,
        string? natureOfProcessing = null)
    {
        if (string.IsNullOrWhiteSpace(orderNo))
            throw new ArgumentException("A job-work order number is required.", nameof(orderNo));
        if (finishedGoodQuantity <= 0m)
            throw new ArgumentException("A job-work order finished-good quantity must be > 0.", nameof(finishedGoodQuantity));
        if (!Quantities.IsWithinPrecision(finishedGoodQuantity))
            throw new InvalidOperationException(
                $"Job-work order finished-good quantity {finishedGoodQuantity} must be to {Quantities.DecimalPlaces} decimal places.");
        if (finishedGoodRate is { } r)
        {
            if (r.Amount < 0m)
                throw new ArgumentException("A job-work order finished-good rate must be ≥ 0.", nameof(finishedGoodRate));
            if (!r.IsPaisaExact)
                throw new InvalidOperationException(
                    $"Job-work order finished-good rate {r.Amount} must be to the paisa (2 decimal places).");
        }

        _lines = (lines ?? throw new ArgumentNullException(nameof(lines))).ToList();
        if (trackingComponents && _lines.Count == 0)
            throw new InvalidOperationException(
                "A job-work order that tracks components must carry at least one component line (RQ-47).");

        Direction = direction;
        OrderNo = orderNo.Trim();
        FinishedGoodStockItemId = finishedGoodStockItemId;
        FinishedGoodQuantity = finishedGoodQuantity;
        FinishedGoodRate = finishedGoodRate;
        FinishedGoodDueDate = finishedGoodDueDate;
        FinishedGoodGodownId = finishedGoodGodownId;
        TrackingComponents = trackingComponents;
        FillComponentsBomId = fillComponentsBomId;
        DurationOfProcess = durationOfProcess;
        NatureOfProcessing = natureOfProcessing;
    }
}
