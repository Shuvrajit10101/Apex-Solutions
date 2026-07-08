namespace Apex.Ledger.Domain;

/// <summary>
/// The canonical effect classification of a <see cref="VoucherBaseType"/> — whether posting it produces an
/// <b>accounting</b> effect (double-entry Σ Dr = Σ Cr) and/or a <b>stock</b> effect (catalog §10;
/// phase3-inventory-requirements §2.2 effect rules, DP-4/DP-5). This is the single source of truth the seed
/// uses to stamp each <see cref="VoucherType"/>'s flags, and it lets any consumer classify a base type
/// without a persisted flag round-trip.
/// </summary>
/// <remarks>
/// The Phase-3 effect rules (catalog §10):
/// <list type="bullet">
///   <item><b>PO / SO</b> — affect <i>neither</i> stock nor accounts (outstanding order only).</item>
///   <item><b>Receipt Note (GRN)</b> — stock <i>inward</i> only; no accounting entry.</item>
///   <item><b>Delivery Note</b> — stock <i>outward</i> only; no accounting entry.</item>
///   <item><b>Rejection In</b> — stock <i>inward</i> only (customer returns to us); no accounting entry.</item>
///   <item><b>Rejection Out</b> — stock <i>outward</i> only (we return to supplier); no accounting entry.</item>
///   <item><b>Stock Journal</b> — stock <i>transfer</i> (source consumption + destination production) only;
///     no accounting posting in Phase 3 (DP-5).</item>
///   <item><b>Physical Stock</b> — stock <i>adjustment</i> to a counted quantity only (DP-3).</item>
///   <item>The accounting base kinds (Contra/Payment/Receipt/Journal/Sales/Purchase/Credit Note/Debit
///     Note, and the provisional Memorandum/Reversing Journal) affect <i>accounts</i>. Sales/Purchase also
///     affect stock when run in Item-Invoice mode; that mode is a later slice, so their default stock flag
///     is <c>false</c> here and is set per-voucher when Item-Invoice mode is enabled.</item>
/// </list>
/// The Job-Work / Payroll / Attendance base kinds are inactive in Phase 3 and classified as no-effect until
/// their feature slice.
/// </remarks>
public static class VoucherEffects
{
    /// <summary>The base kinds whose posting moves stock (an inventory voucher; catalog §10).</summary>
    public static bool AffectsStock(VoucherBaseType baseType) => baseType is
        VoucherBaseType.ReceiptNote
        or VoucherBaseType.DeliveryNote
        or VoucherBaseType.RejectionIn
        or VoucherBaseType.RejectionOut
        or VoucherBaseType.StockJournal
        or VoucherBaseType.PhysicalStock
        // Job Work (Phase 6 slice 8; RQ-48): Material In / Material Out MOVE stock (a third-party transfer or a
        // consumption transform). The two Job-Work ORDER kinds move nothing (they are order-only, below). Accounts
        // stay unaffected for all four (D-4: the job-charge invoice rides the existing accounting path).
        or VoucherBaseType.MaterialIn
        or VoucherBaseType.MaterialOut;

    /// <summary>The base kinds whose posting produces a double-entry accounting effect (catalog §4/§10).</summary>
    public static bool AffectsAccounts(VoucherBaseType baseType) => baseType is
        VoucherBaseType.Contra
        or VoucherBaseType.Payment
        or VoucherBaseType.Receipt
        or VoucherBaseType.Journal
        or VoucherBaseType.Sales
        or VoucherBaseType.Purchase
        or VoucherBaseType.CreditNote
        or VoucherBaseType.DebitNote
        or VoucherBaseType.Memorandum
        or VoucherBaseType.ReversingJournal;

    /// <summary>Whether the base kind is a stock/order voucher kind the inventory engine posts (an inventory
    /// voucher). Phase 6 slice 8 adds the four Job-Work kinds (two order-only + Material In/Out).</summary>
    public static bool IsInventoryBaseType(VoucherBaseType baseType) => baseType is
        VoucherBaseType.PurchaseOrder
        or VoucherBaseType.SalesOrder
        or VoucherBaseType.ReceiptNote
        or VoucherBaseType.DeliveryNote
        or VoucherBaseType.RejectionIn
        or VoucherBaseType.RejectionOut
        or VoucherBaseType.StockJournal
        or VoucherBaseType.PhysicalStock
        // Job Work (Phase 6 slice 8; RQ-45..RQ-49): the inventory engine accepts all four.
        or VoucherBaseType.JobWorkInOrder
        or VoucherBaseType.JobWorkOutOrder
        or VoucherBaseType.MaterialIn
        or VoucherBaseType.MaterialOut;

    /// <summary>Whether the base kind is order-only (a commitment that moves neither stock nor accounts). Phase 6
    /// slice 8 adds the two Job-Work order kinds alongside Purchase/Sales Order (RQ-47).</summary>
    public static bool IsOrderBaseType(VoucherBaseType baseType) => baseType is
        VoucherBaseType.PurchaseOrder
        or VoucherBaseType.SalesOrder
        or VoucherBaseType.JobWorkInOrder
        or VoucherBaseType.JobWorkOutOrder;
}
