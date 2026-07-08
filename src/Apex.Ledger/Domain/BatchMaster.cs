namespace Apex.Ledger.Domain;

/// <summary>
/// A <b>batch / lot</b> master — a first-class record of one lot of a stock item (Phase 6 Cluster 1;
/// requirements RQ-1, RQ-4..RQ-6, DP-8), replacing the bare <c>batch_label</c> text that Phase 3 carried on
/// allocations. It captures the batch number, an optional manufacturing date, an optional expiry (as an
/// absolute date and/or a resolvable <see cref="ExpiryPeriod"/>), an optional inward-layer godown, and an
/// optional <b>per-batch inward cost layer</b> (quantity × rate) whose rate is <b>authoritative</b> for
/// issues from this batch (DP-8): a batch's own inward rate can differ from the item's overall average.
/// </summary>
/// <remarks>
/// <para><b>Per-item uniqueness (RQ-1).</b> A batch number is unique <i>within an item</i>, never globally —
/// two different items may reuse the same batch number. The schema enforces this with a UNIQUE
/// (stock_item_id, batch_no) index; <see cref="Services.BatchService"/> enforces it at create time.</para>
/// <para><b>Expiry resolution (RQ-4).</b> Expiry may be entered as an absolute <see cref="ExpiryDate"/> or a
/// relative <see cref="ExpiryPeriod"/> (e.g. "12 Months") from <see cref="ManufacturingDate"/>.
/// <see cref="ResolvedExpiryDate"/> is the single concrete date the engine uses: the explicit
/// <see cref="ExpiryDate"/> when set, else the period resolved from the mfg date, else <c>null</c>.</para>
/// <para><b>Cost layer (RQ-6/DP-8).</b> When <see cref="InwardQuantity"/> and <see cref="InwardRate"/> are set
/// they describe the batch's opening cost layer (paisa-exact rate). <c>null</c> means no cost layer is
/// recorded on the master yet — the batch's cost is then taken from its movement history.</para>
/// <para>The <see cref="Id"/> is the stable key; the <see cref="BatchNumber"/> is not, so an Alter renames in
/// place. Money is exact decimal rupees in-memory (integer paisa at the boundary); quantity is exact.</para>
/// </remarks>
public sealed class BatchMaster
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The <see cref="StockItem"/> this batch belongs to; required.</summary>
    public Guid StockItemId { get; }

    /// <summary>The batch / lot number; unique within the item (RQ-1), required.</summary>
    public string BatchNumber { get; set; }

    /// <summary>Optional manufacturing date (RQ-2 Track-Mfg); the base for a period-based expiry.</summary>
    public DateOnly? ManufacturingDate { get; set; }

    /// <summary>Optional absolute expiry date (RQ-2 Use-Expiry). When set it wins over <see cref="ExpiryPeriod"/>.</summary>
    public DateOnly? ExpiryDate { get; set; }

    /// <summary>Optional relative expiry period resolved from <see cref="ManufacturingDate"/> (RQ-4).</summary>
    public ExpiryPeriod? ExpiryPeriod { get; set; }

    /// <summary>Optional <see cref="Godown"/> the inward cost layer sits in (RQ-5); <c>null</c> ⇒ unspecified.</summary>
    public Guid? GodownId { get; set; }

    /// <summary>Per-batch inward quantity (the cost layer's quantity, RQ-6/DP-8); <c>null</c> ⇒ no cost layer yet.</summary>
    public decimal? InwardQuantity { get; set; }

    /// <summary>Per-batch inward rate (paisa-exact, authoritative for this batch's issues — DP-8); <c>null</c> ⇒ none.</summary>
    public Money? InwardRate { get; set; }

    public BatchMaster(
        Guid id,
        Guid stockItemId,
        string batchNumber,
        DateOnly? manufacturingDate = null,
        DateOnly? expiryDate = null,
        ExpiryPeriod? expiryPeriod = null,
        Guid? godownId = null,
        decimal? inwardQuantity = null,
        Money? inwardRate = null)
    {
        if (string.IsNullOrWhiteSpace(batchNumber))
            throw new ArgumentException("A batch number is required.", nameof(batchNumber));
        if (inwardQuantity is { } iq)
        {
            if (iq < 0m)
                throw new ArgumentException("Batch inward quantity must be ≥ 0 when set.", nameof(inwardQuantity));
            if (!Quantities.IsWithinPrecision(iq))
                throw new InvalidOperationException(
                    $"Batch inward quantity {iq} must be to {Quantities.DecimalPlaces} decimal places.");
        }
        if (inwardRate is { } ir)
        {
            if (ir.Amount < 0m)
                throw new ArgumentException("Batch inward rate must be ≥ 0 when set.", nameof(inwardRate));
            if (!ir.IsPaisaExact)
                throw new InvalidOperationException(
                    $"Batch inward rate {ir.Amount} must be to the paisa (2 decimal places).");
        }

        Id = id;
        StockItemId = stockItemId;
        BatchNumber = batchNumber.Trim();
        ManufacturingDate = manufacturingDate;
        ExpiryDate = expiryDate;
        ExpiryPeriod = expiryPeriod;
        GodownId = godownId;
        InwardQuantity = inwardQuantity;
        InwardRate = inwardRate;
    }

    /// <summary>
    /// The single concrete expiry date the engine uses (RQ-4): the explicit <see cref="ExpiryDate"/> if set,
    /// else the <see cref="ExpiryPeriod"/> resolved from <see cref="ManufacturingDate"/> (both required for a
    /// period to resolve), else <c>null</c> (no expiry tracked).
    /// </summary>
    public DateOnly? ResolvedExpiryDate
    {
        get
        {
            if (ExpiryDate is { } d) return d;
            if (ExpiryPeriod is { } p && ManufacturingDate is { } mfg) return p.ResolveFrom(mfg);
            return null;
        }
    }

    /// <summary>True iff this batch tracks an expiry (an absolute date or a resolvable period).</summary>
    public bool TracksExpiry => ResolvedExpiryDate is not null;

    /// <summary>
    /// Whether this batch is expired as of <paramref name="asOf"/> — i.e. it tracks an expiry and that resolved
    /// date is strictly before <paramref name="asOf"/> (RQ-7). A batch expiring exactly on <paramref name="asOf"/>
    /// is <b>not</b> yet expired.
    /// </summary>
    public bool IsExpiredAsOf(DateOnly asOf) => ResolvedExpiryDate is { } e && e < asOf;

    /// <summary>
    /// The whole-day gap from <paramref name="asOf"/> to the resolved expiry date (RQ-7/RQ-8): positive when the
    /// batch expires in the future, 0 on the expiry day, negative once past expiry; <c>null</c> when the batch
    /// tracks no expiry.
    /// </summary>
    public int? DaysToExpiry(DateOnly asOf)
        => ResolvedExpiryDate is { } e ? e.DayNumber - asOf.DayNumber : null;
}
