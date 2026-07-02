namespace Apex.Ledger.Domain;

/// <summary>
/// The built-in kinds a <see cref="VoucherType"/> derives from (catalog §4).
/// Every predefined voucher type maps onto exactly one of these base behaviours.
/// </summary>
public enum VoucherBaseType
{
    Contra,
    Payment,
    Receipt,
    Journal,
    Sales,
    Purchase,
    CreditNote,
    DebitNote,
    StockJournal,
    PhysicalStock,
    SalesOrder,
    PurchaseOrder,
    DeliveryNote,
    ReceiptNote,
    RejectionOut,
    RejectionIn,
    Memorandum,
    ReversingJournal,
    JobWorkInOrder,
    MaterialIn,
    JobWorkOutOrder,
    MaterialOut,
    Attendance,
    Payroll,
}
