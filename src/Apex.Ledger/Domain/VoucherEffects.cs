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
        or VoucherBaseType.ReversingJournal
        // Payroll (Phase 8 slice 3): the Payroll voucher posts a balanced integrated accounting entry — earnings
        // Dr to expense, deductions/net Cr to payable/Salary-Payable, employer contributions a separate balanced
        // pair (ER-1) — so it AFFECTS ACCOUNTS. (Attendance is the non-accounting sibling — it records attendance
        // values, books no ledger entry, and is stored as AttendanceEntry rows, never a posted Voucher.)
        or VoucherBaseType.Payroll;

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

    // ======================================================= item-invoice carriers (census 4.7/4.8; defect T0-10)

    /// <summary>
    /// 🔴 <b>The base kinds that may carry <see cref="Voucher.InventoryLines"/> — item-invoice mode.</b>
    ///
    /// <para><b>The defect this widened, measured.</b> Until now this set was Purchase and Sales alone, hard-coded
    /// at SIX separate sites. A Credit Note (sales return) or Debit Note (purchase return) therefore <b>could not
    /// carry a stock line at all</b>: <c>VoucherValidator.EnsureItemInvoiceValid</c> threw, and the entry screen
    /// never offered the grid. The consequence was not a missing screen but <b>wrong closing stock</b> — a
    /// business's returns moved money and moved no goods, so on-hand was overstated by every sales return it took
    /// back in and understated by every purchase return it sent out, for ever.</para>
    ///
    /// <para><b>Vendor, cited.</b> help.tallysolutions.com "How to Record a Sales Return Using Credit Note Under
    /// GST in TallyPrime" — <i>"Press Ctrl+H (Change Mode) &gt; select Item Invoice"</i>, then <i>"Select the
    /// stock item that you initially sold and specify the Quantity and Rate based on the returns received"</i>;
    /// and "How to Record Purchase Returns under GST | Debit Note for Purchase Returns" — <i>"Enter the Name of
    /// Stock Item, Quantity, Rate, and tax ledgers"</i>. Both notes are item-invoice carriers in the reference
    /// product.</para>
    ///
    /// <para>🔴 <b>This is the ONE home.</b> Six readers used to restate the set for themselves; the resolver
    /// hierarchy in <c>GstService</c> records what that costs (four hand-written walks that came to disagree).
    /// Every carrier gate now reads this predicate.</para>
    /// </summary>
    public static bool CanCarryItemInvoiceLines(VoucherBaseType baseType) => baseType is
        VoucherBaseType.Purchase
        or VoucherBaseType.Sales
        or VoucherBaseType.CreditNote
        or VoucherBaseType.DebitNote;

    /// <summary>
    /// Whether an item-invoice carrier sits on the <b>purchase side</b> of the books — which decides the
    /// accounting family its stock leg is drawn from (Purchase Accounts / Stock-in-Hand, versus Sales Accounts)
    /// and which GST head it touches (Input, versus Output).
    ///
    /// <para>A <b>Debit Note is a purchase return</b>, so it is purchase-side and reverses INPUT tax; a
    /// <b>Credit Note is a sales return</b>, so it is sales-side and reverses OUTPUT tax. The head does not flip
    /// with the return — only the SIDE the leg is posted on does (<see cref="IsReturnNote"/>). A credit note that
    /// reversed Input GST would claim ITC on a sale, which is a wrong filed return, not merely a wrong report.</para>
    /// </summary>
    public static bool IsPurchaseSideInvoice(VoucherBaseType baseType) => baseType is
        VoucherBaseType.Purchase or VoucherBaseType.DebitNote;

    /// <summary>
    /// Whether the carrier is a <b>return note</b> — the two kinds that REVERSE an earlier document rather than
    /// raise a new one. Every accounting leg of a note is posted on the opposite side from the invoice it mirrors
    /// (party, stock leg and each tax leg alike), and its stock moves the opposite way
    /// (<see cref="ItemInvoiceStockDirection"/>).
    /// </summary>
    public static bool IsReturnNote(VoucherBaseType baseType) => baseType is
        VoucherBaseType.CreditNote or VoucherBaseType.DebitNote;

    /// <summary>
    /// 🔴 <b>Which way an item-invoice line moves stock, by carrier nature.</b> Purchase and Credit Note bring
    /// goods IN (a bought-in receipt; a sales return coming back to us); Sales and Debit Note send goods OUT (a
    /// delivery; a purchase return going back to the supplier).
    ///
    /// <para>🔴 <b>The trap this replaced.</b> The entry screen stamped the direction as
    /// <c>IsPurchaseInvoice ? Inward : Outward</c> — a two-way test on a four-way fact. Widening the carrier set
    /// without this would have stamped a sales return OUTWARD, taking the returned goods off the shelf a SECOND
    /// time: the stock error doubles instead of closing. The validator re-checks the stamp against this same
    /// function, so the screen and the engine cannot disagree.</para>
    /// </summary>
    public static StockDirection ItemInvoiceStockDirection(VoucherBaseType baseType) => baseType switch
    {
        VoucherBaseType.Purchase or VoucherBaseType.CreditNote => StockDirection.Inward,
        VoucherBaseType.Sales or VoucherBaseType.DebitNote => StockDirection.Outward,
        _ => throw new ArgumentOutOfRangeException(
            nameof(baseType), baseType, "not an item-invoice carrier; ask CanCarryItemInvoiceLines first."),
    };
}
