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
            physicalLines: null, number, narration, partyId, cancelled, postDated)
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
        int number,
        string? narration,
        Guid? partyId,
        bool cancelled,
        bool postDated)
    {
        Id = id;
        TypeId = typeId;
        Date = date;
        _allocations = allocations?.ToList() ?? new List<InventoryAllocation>();
        _destinationAllocations = destinationAllocations?.ToList() ?? new List<InventoryAllocation>();
        _orderLines = orderLines?.ToList() ?? new List<OrderLine>();
        _physicalLines = physicalLines?.ToList() ?? new List<PhysicalStockLine>();
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
            physicalLines: null, number, narration, partyId, cancelled, postDated);

    /// <summary>
    /// Creates a Stock-Journal voucher — <paramref name="source"/> lines (consumption, outward) plus
    /// <paramref name="destination"/> lines (production, inward). The two sides must balance in the base unit
    /// (enforced at posting).
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
        bool postDated = false)
        => new(id, typeId, date, allocations: source, destinationAllocations: destination, orderLines: null,
            physicalLines: null, number, narration, partyId: null, cancelled, postDated);

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
            physicalLines: lines, number, narration, partyId: null, cancelled, postDated);

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
        bool postDated)
        => new(id, typeId, date, allocations, destinationAllocations, orderLines, physicalLines,
            number, narration, partyId, cancelled, postDated);
}
