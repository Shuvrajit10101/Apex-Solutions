namespace Apex.Ledger.Domain;

/// <summary>
/// A stock/order voucher — the inventory-side transaction (catalog §10; phase3-inventory-requirements
/// RQ-8..RQ-15). It is kept <b>separate</b> from the accounting <see cref="Voucher"/> (which enforces the
/// balanced Σ Dr = Σ Cr invariant) because a Phase-3 stock/order voucher posts <b>no accounting entry</b>
/// (DP-5): a Receipt/Delivery/Rejection/Stock-Journal moves stock only; an order moves nothing. Its content
/// depends on its type's base kind:
/// <list type="bullet">
///   <item><b>Receipt Note / Delivery Note / Rejection In / Rejection Out</b> carry
///     <see cref="Allocations"/> (inward or outward stock movements).</item>
///   <item><b>Purchase Order / Sales Order</b> carry <see cref="OrderLines"/> (no stock/accounts effect).</item>
///   <item><b>Stock Journal</b> carries source <see cref="Allocations"/> (outward) + destination
///     <see cref="DestinationAllocations"/> (inward), which must balance in the base unit.</item>
///   <item><b>Physical Stock</b> carries <see cref="PhysicalLines"/> (counted quantities, DP-3).</item>
/// </list>
/// Validation of type-vs-content, the no-negative-stock guard and the Stock-Journal balance rule live in
/// <c>InventoryPostingService</c>, mirroring how <c>LedgerService</c>/<c>VoucherValidator</c> guard the
/// accounting voucher.
/// </summary>
public sealed class InventoryVoucher
{
    private readonly List<InventoryAllocation> _allocations;
    private readonly List<InventoryAllocation> _destinationAllocations;
    private readonly List<OrderLine> _orderLines;
    private readonly List<PhysicalStockLine> _physicalLines;
    private readonly List<AdditionalCostLine> _additionalCostLines;
    private readonly List<Guid> _orderLinks;

    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The <see cref="VoucherType"/> this voucher belongs to (a stock/order base kind).</summary>
    public Guid TypeId { get; }

    /// <summary>Sequence within its type; 0 until an automatic number is assigned.</summary>
    public int Number { get; set; }

    /// <summary>Voucher date; must be ≥ the company's books-begin date.</summary>
    public DateOnly Date { get; }

    /// <summary>Free text.</summary>
    public string? Narration { get; set; }

    /// <summary>Optional party (supplier/customer) ledger.</summary>
    public Guid? PartyId { get; set; }

    /// <summary>Alt+X — number retained in sequence, zero effect on stock.</summary>
    public bool Cancelled { get; set; }

    /// <summary>Ctrl+T — excluded from on-hand until its date is reached.</summary>
    public bool PostDated { get; set; }

    /// <summary>The stock movements (source lines for a Stock Journal); empty for order/physical vouchers.</summary>
    public IReadOnlyList<InventoryAllocation> Allocations => _allocations;

    /// <summary>The Stock-Journal destination (production) movements; empty for every other voucher.</summary>
    public IReadOnlyList<InventoryAllocation> DestinationAllocations => _destinationAllocations;

    /// <summary>The order lines (PO/SO); empty for stock-moving/physical vouchers.</summary>
    public IReadOnlyList<OrderLine> OrderLines => _orderLines;

    /// <summary>The counted-quantity lines (Physical Stock); empty for every other voucher.</summary>
    public IReadOnlyList<PhysicalStockLine> PhysicalLines => _physicalLines;

    /// <summary>
    /// The <b>additional-cost</b> lines on a Stock-Journal <b>transfer</b> (Phase 6 slice 3 RQ-20): each names an
    /// additional-cost ledger + amount to apportion across the <see cref="DestinationAllocations"/> (raising their
    /// landed inward rate by the ledger's <see cref="Ledger.MethodOfAppropriation"/>). Empty for every other
    /// voucher, so a plain Stock Journal / Receipt / order behaves byte-identically (ER-13).
    /// </summary>
    public IReadOnlyList<AdditionalCostLine> AdditionalCostLines => _additionalCostLines;

    /// <summary>
    /// The <b>Job Work Order</b> payload (Phase 6 slice 8; RQ-47), non-null only on a Job Work In/Out Order voucher
    /// (base <see cref="VoucherBaseType.JobWorkInOrder"/> / <see cref="VoucherBaseType.JobWorkOutOrder"/>). Carries
    /// the finished good + tracked component lines; the order moves neither stock nor accounts. <c>null</c> for
    /// every other voucher, so an existing voucher is byte-identical (ER-13).
    /// </summary>
    public JobWorkOrder? JobWorkOrder { get; }

    /// <summary>
    /// The Job Work order(s) this <b>Material In/Out</b> voucher fulfils (Phase 6 slice 8; RQ-48 "linked Order
    /// No(s)"), backing <c>material_order_links</c>. Empty for every non-material voucher and for a material
    /// movement entered without linking an order (byte-identical, ER-13).
    /// </summary>
    public IReadOnlyList<Guid> OrderLinks => _orderLinks;

    /// <summary>
    /// Creates a stock-moving voucher (Receipt/Delivery/Rejection In/Out) from its allocations. For a Stock
    /// Journal use <see cref="StockJournal"/>; for an order use <see cref="Order"/>; for a physical count use
    /// <see cref="PhysicalStock"/>.
    /// </summary>
    public InventoryVoucher(
        Guid id,
        Guid typeId,
        DateOnly date,
        IEnumerable<InventoryAllocation> allocations,
        int number = 0,
        string? narration = null,
        Guid? partyId = null,
        bool cancelled = false,
        bool postDated = false)
        : this(id, typeId, date, allocations, destinationAllocations: null, orderLines: null,
            physicalLines: null, additionalCostLines: null, number, narration, partyId, cancelled, postDated,
            jobWorkOrder: null, orderLinks: null)
    {
    }

    private InventoryVoucher(
        Guid id,
        Guid typeId,
        DateOnly date,
        IEnumerable<InventoryAllocation>? allocations,
        IEnumerable<InventoryAllocation>? destinationAllocations,
        IEnumerable<OrderLine>? orderLines,
        IEnumerable<PhysicalStockLine>? physicalLines,
        IEnumerable<AdditionalCostLine>? additionalCostLines,
        int number,
        string? narration,
        Guid? partyId,
        bool cancelled,
        bool postDated,
        JobWorkOrder? jobWorkOrder,
        IEnumerable<Guid>? orderLinks)
    {
        Id = id;
        TypeId = typeId;
        Date = date;
        _allocations = allocations?.ToList() ?? new List<InventoryAllocation>();
        _destinationAllocations = destinationAllocations?.ToList() ?? new List<InventoryAllocation>();
        _orderLines = orderLines?.ToList() ?? new List<OrderLine>();
        _physicalLines = physicalLines?.ToList() ?? new List<PhysicalStockLine>();
        _additionalCostLines = additionalCostLines?.ToList() ?? new List<AdditionalCostLine>();
        _orderLinks = orderLinks?.ToList() ?? new List<Guid>();
        JobWorkOrder = jobWorkOrder;
        Number = number;
        Narration = narration;
        PartyId = partyId;
        Cancelled = cancelled;
        PostDated = postDated;
    }

    /// <summary>Creates an order voucher (Purchase Order / Sales Order) — order lines only, no stock effect.</summary>
    public static InventoryVoucher Order(
        Guid id,
        Guid typeId,
        DateOnly date,
        IEnumerable<OrderLine> orderLines,
        int number = 0,
        string? narration = null,
        Guid? partyId = null,
        bool cancelled = false,
        bool postDated = false)
        => new(id, typeId, date, allocations: null, destinationAllocations: null, orderLines: orderLines,
            physicalLines: null, additionalCostLines: null, number, narration, partyId, cancelled, postDated,
            jobWorkOrder: null, orderLinks: null);

    /// <summary>
    /// Creates a Stock-Journal voucher — <paramref name="source"/> lines (consumption, outward) plus
    /// <paramref name="destination"/> lines (production, inward). The two sides must balance in the base unit
    /// (enforced at posting). Optional <paramref name="additionalCostLines"/> load the destination landed rate on
    /// an inter-godown transfer (RQ-20).
    /// </summary>
    public static InventoryVoucher StockJournal(
        Guid id,
        Guid typeId,
        DateOnly date,
        IEnumerable<InventoryAllocation> source,
        IEnumerable<InventoryAllocation> destination,
        int number = 0,
        string? narration = null,
        bool cancelled = false,
        bool postDated = false,
        IEnumerable<AdditionalCostLine>? additionalCostLines = null)
        => new(id, typeId, date, allocations: source, destinationAllocations: destination, orderLines: null,
            physicalLines: null, additionalCostLines: additionalCostLines, number, narration, partyId: null,
            cancelled, postDated, jobWorkOrder: null, orderLinks: null);

    /// <summary>
    /// Creates a <b>Job Work In/Out Order</b> voucher (Phase 6 slice 8; RQ-47) — the <paramref name="jobWorkOrder"/>
    /// payload only, with no allocations, order-lines or physical lines. Affects neither stock nor accounts, exactly
    /// like a Purchase/Sales Order.
    /// </summary>
    public static InventoryVoucher JobWork(
        Guid id,
        Guid typeId,
        DateOnly date,
        JobWorkOrder jobWorkOrder,
        int number = 0,
        string? narration = null,
        Guid? partyId = null,
        bool cancelled = false,
        bool postDated = false)
        => new(id, typeId, date, allocations: null, destinationAllocations: null, orderLines: null,
            physicalLines: null, additionalCostLines: null, number, narration, partyId, cancelled, postDated,
            jobWorkOrder: jobWorkOrder ?? throw new ArgumentNullException(nameof(jobWorkOrder)), orderLinks: null);

    /// <summary>
    /// Creates a <b>Material In / Material Out</b> movement voucher (Phase 6 slice 8; RQ-46/RQ-48/RQ-49) — a
    /// source (outward) + destination (inward) pair of allocations, like a Stock Journal, plus the optional Job
    /// Work order links it fulfils (<paramref name="orderLinks"/>). A Material Out is a balanced transfer (stock
    /// stays on our books at a third-party godown); a Material In with Allow Consumption is a transform (consume
    /// components, produce the finished good) and is balance-exempt. The engine posts exactly the lines carried,
    /// so principal/worker symmetry falls out with no hard-coded branch (RQ-50).
    /// </summary>
    public static InventoryVoucher MaterialMovement(
        Guid id,
        Guid typeId,
        DateOnly date,
        IEnumerable<InventoryAllocation> source,
        IEnumerable<InventoryAllocation> destination,
        IEnumerable<Guid>? orderLinks = null,
        int number = 0,
        string? narration = null,
        Guid? partyId = null,
        bool cancelled = false,
        bool postDated = false)
        => new(id, typeId, date, allocations: source, destinationAllocations: destination, orderLines: null,
            physicalLines: null, additionalCostLines: null, number, narration, partyId, cancelled, postDated,
            jobWorkOrder: null, orderLinks: orderLinks);

    /// <summary>Creates a Physical-Stock voucher — counted-quantity lines only (DP-3).</summary>
    public static InventoryVoucher PhysicalStock(
        Guid id,
        Guid typeId,
        DateOnly date,
        IEnumerable<PhysicalStockLine> lines,
        int number = 0,
        string? narration = null,
        bool cancelled = false,
        bool postDated = false)
        => new(id, typeId, date, allocations: null, destinationAllocations: null, orderLines: null,
            physicalLines: lines, additionalCostLines: null, number, narration, partyId: null, cancelled, postDated,
            jobWorkOrder: null, orderLinks: null);

    /// <summary>Rehydrates an inventory voucher from persisted parts (the SQLite adapter).</summary>
    public static InventoryVoucher FromStorage(
        Guid id,
        Guid typeId,
        DateOnly date,
        IEnumerable<InventoryAllocation> allocations,
        IEnumerable<InventoryAllocation> destinationAllocations,
        IEnumerable<OrderLine> orderLines,
        IEnumerable<PhysicalStockLine> physicalLines,
        int number,
        string? narration,
        Guid? partyId,
        bool cancelled,
        bool postDated,
        IEnumerable<AdditionalCostLine>? additionalCostLines = null,
        JobWorkOrder? jobWorkOrder = null,
        IEnumerable<Guid>? orderLinks = null)
        => new(id, typeId, date, allocations, destinationAllocations, orderLines, physicalLines,
            additionalCostLines, number, narration, partyId, cancelled, postDated, jobWorkOrder, orderLinks);
}
